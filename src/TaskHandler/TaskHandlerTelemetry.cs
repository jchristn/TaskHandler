namespace TaskHandler
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Internal holder for the TaskHandler meter, activity source, and instruments, plus best-effort
    /// recording helpers. Every helper swallows its own exceptions so that telemetry can never affect task
    /// execution. When no listener is subscribed, spans are not created and measurements are discarded by
    /// the runtime at negligible cost.
    /// Thread safety: all members are safe to call concurrently.
    /// </summary>
    internal static class TaskHandlerTelemetry
    {
        #region Internal-Members

        /// <summary>
        /// Library version reported on the meter, activity source, and build-info gauge.
        /// </summary>
        internal static readonly string Version = GetVersion();

        /// <summary>
        /// Activity source for all TaskHandler spans.
        /// </summary>
        internal static readonly ActivitySource Source = new ActivitySource(TaskHandlerTelemetryNames.ActivitySourceName, Version);

        /// <summary>
        /// Meter for all TaskHandler metrics.
        /// </summary>
        internal static readonly Meter Meter = new Meter(TaskHandlerTelemetryNames.MeterName, Version);

        #endregion

        #region Private-Members

        private static readonly double[] _DurationBuckets = new double[]
        {
            0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600
        };

        private static readonly Counter<long> _TasksEnqueued = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.TasksEnqueued, "{task}", "Tasks accepted into the queue.");

        private static readonly Counter<long> _TasksRejected = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.TasksRejected, "{task}", "Enqueue attempts that were rejected.");

        private static readonly Histogram<double> _EnqueueDuration = CreateDurationHistogram(
            TaskHandlerTelemetryNames.EnqueueDuration, "Time to write a task into the queue, including backpressure wait.");

        private static readonly Counter<long> _TasksCompleted = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.TasksCompleted, "{task}", "Tasks that reached a terminal state, by outcome.");

        private static readonly Histogram<double> _TaskDuration = CreateDurationHistogram(
            TaskHandlerTelemetryNames.TaskDuration, "End-to-end task duration from enqueue to terminal state.");

        private static readonly Histogram<double> _StageDuration = CreateDurationHistogram(
            TaskHandlerTelemetryNames.StageDuration, "Per-stage task duration (queued, slot_wait, execute).");

        private static readonly Counter<long> _StageEvents = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.StageEvents, "{event}", "Per-stage task events, by outcome.");

        private static readonly Counter<long> _CancellationRequests = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.CancellationRequests, "{request}", "Cancellation requests issued by the queue, by reason.");

        private static readonly Counter<long> _LifecycleEvents = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.LifecycleEvents, "{event}", "Queue lifecycle transitions.");

        private static readonly Counter<long> _EventHandlerErrors = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.EventHandlerErrors, "{error}", "Exceptions thrown by user event handlers (suppressed).");

        private static readonly Counter<long> _RunnerErrors = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.RunnerErrors, "{error}", "Unexpected exceptions that terminated the task runner.");

        private static readonly Counter<long> _RunWithTimeoutOperations = Meter.CreateCounter<long>(
            TaskHandlerTelemetryNames.RunWithTimeoutOperations, "{operation}", "TaskRunWithTimeout.Go operations, by outcome.");

        private static readonly Histogram<double> _RunWithTimeoutDuration = CreateDurationHistogram(
            TaskHandlerTelemetryNames.RunWithTimeoutDuration, "TaskRunWithTimeout.Go duration.");

        private static readonly ConcurrentDictionary<long, WeakReference<TaskQueue>> _Queues = new ConcurrentDictionary<long, WeakReference<TaskQueue>>();
        private static long _NextQueueId = 0;

        #endregion

        #region Constructors-and-Factories

        static TaskHandlerTelemetry()
        {
            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.QueueDepth,
                () => ObserveSum(q => q.QueuedCount, q => true),
                "{task}",
                "Tasks waiting in the queue.");

            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.QueueCapacity,
                () => ObserveSum(q => q.MaxQueueSize, q => q.MaxQueueSize > 0),
                "{task}",
                "Configured queue capacity (bounded queues only).");

            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.ConcurrencyInUse,
                () => ObserveSum(q => q.RunningCount, q => true),
                "{task}",
                "Concurrency slots in use.");

            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.ConcurrencyLimit,
                () => ObserveSum(q => q.MaxConcurrentTasks, q => true),
                "{task}",
                "Configured concurrency limit.");

            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.QueueProcessing,
                () => ObserveMax(q => q.IsProcessing ? 1 : 0),
                "{state}",
                "1 while the queue is started and processing, otherwise 0.");

            Meter.CreateObservableGauge<double>(
                TaskHandlerTelemetryNames.LastSuccess,
                () => ObserveLastSuccess(),
                "s",
                "Unix time of the most recent successful task completion.");

            Meter.CreateObservableGauge<long>(
                TaskHandlerTelemetryNames.BuildInfo,
                () => new Measurement<long>(1, new KeyValuePair<string, object>(TaskHandlerTelemetryNames.AttrVersion, Version)),
                "{info}",
                "TaskHandler build information.");
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Register a queue so its state is reported by the observable gauges.
        /// </summary>
        /// <param name="queue">Queue.</param>
        /// <returns>Registration identifier, used to unregister.</returns>
        internal static long RegisterQueue(TaskQueue queue)
        {
            long id = Interlocked.Increment(ref _NextQueueId);

            try
            {
                _Queues[id] = new WeakReference<TaskQueue>(queue);
            }
            catch (Exception)
            {
            }

            return id;
        }

        /// <summary>
        /// Unregister a queue from the observable gauges.
        /// </summary>
        /// <param name="id">Registration identifier.</param>
        internal static void UnregisterQueue(long id)
        {
            try
            {
                _Queues.TryRemove(id, out WeakReference<TaskQueue> removed);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Start the producer span for an enqueue attempt. Returns null when no listener is subscribed.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        /// <param name="queueDepth">Current queue depth.</param>
        /// <returns>Activity, or null.</returns>
        internal static Activity StartEnqueue(string queueName, TaskDetails details, int queueDepth)
        {
            try
            {
                Activity activity = Source.StartActivity(TaskHandlerTelemetryNames.SpanEnqueue, ActivityKind.Producer);
                if (activity != null)
                {
                    activity.SetTag(TaskHandlerTelemetryNames.AttrQueueName, queueName);
                    activity.SetTag(TaskHandlerTelemetryNames.AttrTaskId, details.Guid.ToString());
                    activity.SetTag(TaskHandlerTelemetryNames.AttrTaskName, details.Name);
                    activity.SetTag(TaskHandlerTelemetryNames.AttrTaskPriority, details.Priority);
                    activity.SetTag(TaskHandlerTelemetryNames.AttrQueueDepth, queueDepth);
                }

                return activity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Capture the trace context that the task job span will use as its parent. Must be called before
        /// the task is written into the queue so the consumer can never observe a missing context.
        /// </summary>
        /// <param name="details">Task details.</param>
        /// <param name="enqueueActivity">Enqueue activity, or null.</param>
        internal static void CaptureParentContext(TaskDetails details, Activity enqueueActivity)
        {
            try
            {
                Activity parent = enqueueActivity ?? Activity.Current;
                if (parent != null) details.ParentContext = parent.Context;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a successful enqueue.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="activity">Enqueue activity, or null.</param>
        /// <param name="startedUtc">Time the enqueue attempt began.</param>
        internal static void EnqueueSucceeded(string queueName, Activity activity, DateTime startedUtc)
        {
            try
            {
                TagList queueTags = QueueTags(queueName);
                _TasksEnqueued.Add(1, queueTags);

                TagList durationTags = QueueTags(queueName);
                durationTags.Add(TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess);
                _EnqueueDuration.Record(Seconds(startedUtc, DateTime.UtcNow), durationTags);

                if (activity != null)
                {
                    activity.SetTag(TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess);
                    activity.SetStatus(ActivityStatusCode.Ok);
                    activity.Stop();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a rejected enqueue.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="activity">Enqueue activity, or null.</param>
        /// <param name="startedUtc">Time the enqueue attempt began.</param>
        /// <param name="errorType">Bounded error type.</param>
        /// <param name="ex">Exception, or null.</param>
        internal static void EnqueueRejected(string queueName, Activity activity, DateTime startedUtc, string errorType, Exception ex)
        {
            try
            {
                TagList rejectTags = QueueTags(queueName);
                rejectTags.Add(TaskHandlerTelemetryNames.AttrErrorType, errorType);
                _TasksRejected.Add(1, rejectTags);

                TagList durationTags = QueueTags(queueName);
                durationTags.Add(TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeRejected);
                _EnqueueDuration.Record(Seconds(startedUtc, DateTime.UtcNow), durationTags);

                if (activity != null)
                {
                    activity.SetTag(TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeRejected);
                    activity.SetTag(TaskHandlerTelemetryNames.AttrErrorType, errorType);
                    if (ex != null) AddException(activity, ex);
                    activity.SetStatus(ActivityStatusCode.Error, errorType);
                    activity.Stop();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Map an enqueue exception to a bounded error type.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>Error type.</returns>
        internal static string EnqueueErrorType(Exception ex)
        {
            if (ex is ChannelClosedException) return TaskHandlerTelemetryNames.ErrorQueueClosed;
            if (ex is OperationCanceledException) return TaskHandlerTelemetryNames.OutcomeCanceled;
            return ErrorType(ex);
        }

        /// <summary>
        /// Called by the task runner when a task is read from the queue. Starts the job span (back-dated to the
        /// enqueue time, parented to the enqueue context), emits the queued stage, and starts the slot-wait stage.
        /// Leaves Activity.Current unchanged.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        internal static void TaskDequeued(string queueName, TaskDetails details)
        {
            Activity previous = Activity.Current;

            try
            {
                DateTime dequeuedAt = details.DequeuedAt ?? DateTime.UtcNow;
                RecordStage(queueName, TaskHandlerTelemetryNames.StageQueued, TaskHandlerTelemetryNames.OutcomeSuccess, details.EnqueuedAt, dequeuedAt);

                Activity job = Source.StartActivity(
                    TaskHandlerTelemetryNames.SpanTask,
                    ActivityKind.Consumer,
                    details.ParentContext,
                    null,
                    null,
                    new DateTimeOffset(details.EnqueuedAt));

                if (job != null)
                {
                    job.SetTag(TaskHandlerTelemetryNames.AttrQueueName, queueName);
                    job.SetTag(TaskHandlerTelemetryNames.AttrTaskId, details.Guid.ToString());
                    job.SetTag(TaskHandlerTelemetryNames.AttrTaskName, details.Name);
                    job.SetTag(TaskHandlerTelemetryNames.AttrTaskPriority, details.Priority);
                    details.JobActivity = job;

                    Activity queued = Source.StartActivity(
                        TaskHandlerTelemetryNames.SpanStageQueued,
                        ActivityKind.Internal,
                        job.Context,
                        null,
                        null,
                        new DateTimeOffset(details.EnqueuedAt));

                    if (queued != null)
                    {
                        queued.SetStatus(ActivityStatusCode.Ok);
                        queued.SetEndTime(dequeuedAt);
                        queued.Stop();
                    }

                    details.SlotWaitActivity = Source.StartActivity(
                        TaskHandlerTelemetryNames.SpanStageSlotWait,
                        ActivityKind.Internal,
                        job.Context,
                        null,
                        null,
                        new DateTimeOffset(dequeuedAt));
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Activity.Current = previous;
            }
        }

        /// <summary>
        /// Called by the task runner when a concurrency slot has been acquired. Emits the slot-wait stage and
        /// starts the execute stage span. On return, Activity.Current is the execute span (or unchanged when no
        /// listener is subscribed) so the user function nests under it; the caller restores Activity.Current.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        /// <param name="concurrencyLimit">Configured concurrency limit.</param>
        internal static void TaskStarting(string queueName, TaskDetails details, int concurrencyLimit)
        {
            try
            {
                DateTime startedAt = details.StartedAt ?? DateTime.UtcNow;
                DateTime dequeuedAt = details.DequeuedAt ?? startedAt;
                RecordStage(queueName, TaskHandlerTelemetryNames.StageSlotWait, TaskHandlerTelemetryNames.OutcomeSuccess, dequeuedAt, startedAt);

                Activity slotWait = details.SlotWaitActivity;
                if (slotWait != null)
                {
                    slotWait.SetStatus(ActivityStatusCode.Ok);
                    slotWait.SetEndTime(startedAt);
                    slotWait.Stop();
                    details.SlotWaitActivity = null;
                }

                Activity job = details.JobActivity;
                if (job != null)
                {
                    Activity execute = Source.StartActivity(
                        TaskHandlerTelemetryNames.SpanStageExecute,
                        ActivityKind.Internal,
                        job.Context,
                        null,
                        null,
                        new DateTimeOffset(startedAt));

                    if (execute != null)
                    {
                        execute.SetTag(TaskHandlerTelemetryNames.AttrConcurrencyLimit, concurrencyLimit);
                        details.ExecuteActivity = execute;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Called by the task runner when a dequeued task can never run because the runner stopped (or failed)
        /// while the task waited for a concurrency slot.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        /// <param name="ex">Exception that interrupted the slot wait.</param>
        internal static void TaskDropped(string queueName, TaskDetails details, Exception ex)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                DateTime dequeuedAt = details.DequeuedAt ?? now;
                string errorType = ErrorType(ex);
                RecordStage(queueName, TaskHandlerTelemetryNames.StageSlotWait, TaskHandlerTelemetryNames.OutcomeDropped, dequeuedAt, now);
                RecordTerminal(queueName, TaskHandlerTelemetryNames.OutcomeDropped, errorType, details.EnqueuedAt, now);

                Activity slotWait = details.SlotWaitActivity;
                if (slotWait != null)
                {
                    slotWait.SetStatus(ActivityStatusCode.Error, TaskHandlerTelemetryNames.OutcomeDropped);
                    slotWait.Stop();
                    details.SlotWaitActivity = null;
                }

                Activity job = details.JobActivity;
                if (job != null)
                {
                    job.SetTag(TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeDropped);
                    job.SetTag(TaskHandlerTelemetryNames.AttrErrorType, errorType);
                    job.SetStatus(ActivityStatusCode.Error, "Task dropped: the queue stopped before a concurrency slot became available.");
                    job.Stop();
                    details.JobActivity = null;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Called from the completion continuation. Emits the execute stage and the terminal job metrics, and
        /// closes the execute and job spans with an explicit status.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        /// <param name="completedTask">Completed task.</param>
        /// <param name="completedAt">Completion time.</param>
        /// <returns>Outcome value.</returns>
        internal static string TaskCompleted(string queueName, TaskDetails details, Task completedTask, DateTime completedAt)
        {
            string outcome = TaskHandlerTelemetryNames.OutcomeFailure;

            try
            {
                Exception ex = null;
                if (completedTask.Status == TaskStatus.RanToCompletion)
                {
                    outcome = TaskHandlerTelemetryNames.OutcomeSuccess;
                }
                else if (completedTask.Status == TaskStatus.Canceled)
                {
                    outcome = TaskHandlerTelemetryNames.OutcomeCanceled;
                }
                else
                {
                    ex = Unwrap(completedTask.Exception);
                    outcome = ex is TimeoutException ? TaskHandlerTelemetryNames.OutcomeTimeout : TaskHandlerTelemetryNames.OutcomeFailure;
                }

                string errorType = ex != null ? ErrorType(ex) : null;
                DateTime startedAt = details.StartedAt ?? completedAt;

                RecordStage(queueName, TaskHandlerTelemetryNames.StageExecute, outcome, startedAt, completedAt);
                RecordTerminal(queueName, outcome, errorType, details.EnqueuedAt, completedAt);

                CloseWithOutcome(details.ExecuteActivity, outcome, errorType, ex, completedAt);
                details.ExecuteActivity = null;
                CloseWithOutcome(details.JobActivity, outcome, errorType, null, completedAt);
                details.JobActivity = null;
            }
            catch (Exception)
            {
            }

            return outcome;
        }

        /// <summary>
        /// Record a cancellation request issued by the queue, and annotate the task's execute span.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="details">Task details.</param>
        /// <param name="reason">Bounded cancellation reason.</param>
        internal static void CancellationRequested(string queueName, TaskDetails details, string reason)
        {
            try
            {
                TagList tags = QueueTags(queueName);
                tags.Add(TaskHandlerTelemetryNames.AttrCancelReason, reason);
                _CancellationRequests.Add(1, tags);

                Activity execute = details?.ExecuteActivity;
                if (execute != null)
                {
                    ActivityTagsCollection eventTags = new ActivityTagsCollection();
                    eventTags[TaskHandlerTelemetryNames.AttrCancelReason] = reason;
                    execute.AddEvent(new ActivityEvent("cancellation_requested", default, eventTags));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a queue lifecycle transition.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="lifecycleEvent">Bounded lifecycle event.</param>
        internal static void Lifecycle(string queueName, string lifecycleEvent)
        {
            try
            {
                TagList tags = QueueTags(queueName);
                tags.Add(TaskHandlerTelemetryNames.AttrLifecycleEvent, lifecycleEvent);
                _LifecycleEvents.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an exception thrown by a user event handler.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="eventName">Event handler name.</param>
        /// <param name="ex">Exception.</param>
        internal static void EventHandlerError(string queueName, string eventName, Exception ex)
        {
            try
            {
                TagList tags = QueueTags(queueName);
                tags.Add(TaskHandlerTelemetryNames.AttrEvent, eventName);
                tags.Add(TaskHandlerTelemetryNames.AttrErrorType, ErrorType(ex));
                _EventHandlerErrors.Add(1, tags);

                Activity current = Activity.Current;
                if (current != null && current.Source == Source) AddException(current, ex);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an unexpected exception that terminated the task runner.
        /// </summary>
        /// <param name="queueName">Queue name.</param>
        /// <param name="ex">Exception.</param>
        internal static void RunnerError(string queueName, Exception ex)
        {
            try
            {
                TagList tags = QueueTags(queueName);
                tags.Add(TaskHandlerTelemetryNames.AttrErrorType, ErrorType(ex));
                _RunnerErrors.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Start the span for a TaskRunWithTimeout.Go call. Returns null when no listener is subscribed.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>Activity, or null.</returns>
        internal static Activity StartRunWithTimeout(int timeoutMs)
        {
            try
            {
                Activity activity = Source.StartActivity(TaskHandlerTelemetryNames.SpanRunWithTimeout, ActivityKind.Internal);
                activity?.SetTag(TaskHandlerTelemetryNames.AttrTimeoutMs, timeoutMs);
                return activity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Record the result of a TaskRunWithTimeout.Go call and close its span.
        /// </summary>
        /// <param name="activity">Activity, or null.</param>
        /// <param name="startedUtc">Start time.</param>
        /// <param name="outcome">Outcome value.</param>
        /// <param name="ex">Exception, or null.</param>
        internal static void RunWithTimeoutCompleted(Activity activity, DateTime startedUtc, string outcome, Exception ex)
        {
            try
            {
                Exception inner = Unwrap(ex);
                string errorType = inner != null ? ErrorType(inner) : null;

                TagList tags = new TagList();
                tags.Add(TaskHandlerTelemetryNames.AttrOutcome, outcome);
                _RunWithTimeoutDuration.Record(Seconds(startedUtc, DateTime.UtcNow), tags);
                if (errorType != null) tags.Add(TaskHandlerTelemetryNames.AttrErrorType, errorType);
                _RunWithTimeoutOperations.Add(1, tags);

                CloseWithOutcome(activity, outcome, errorType, inner, DateTime.UtcNow);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Bounded error type for an exception: its full type name.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>Error type.</returns>
        internal static string ErrorType(Exception ex)
        {
            if (ex == null) return "_OTHER";
            return ex.GetType().FullName ?? ex.GetType().Name;
        }

        #endregion

        #region Private-Methods

        private static Histogram<double> CreateDurationHistogram(string name, string description)
        {
#if NET9_0_OR_GREATER || NETSTANDARD
            return Meter.CreateHistogram<double>(
                name,
                "s",
                description,
                null,
                new InstrumentAdvice<double> { HistogramBucketBoundaries = _DurationBuckets });
#else
            return Meter.CreateHistogram<double>(name, "s", description);
#endif
        }

        private static TagList QueueTags(string queueName)
        {
            TagList tags = new TagList();
            tags.Add(TaskHandlerTelemetryNames.AttrQueueName, queueName);
            return tags;
        }

        private static void RecordStage(string queueName, string stage, string outcome, DateTime start, DateTime end)
        {
            TagList tags = QueueTags(queueName);
            tags.Add(TaskHandlerTelemetryNames.AttrStage, stage);
            tags.Add(TaskHandlerTelemetryNames.AttrOutcome, outcome);
            _StageDuration.Record(Seconds(start, end), tags);
            _StageEvents.Add(1, tags);
        }

        private static void RecordTerminal(string queueName, string outcome, string errorType, DateTime enqueuedAt, DateTime end)
        {
            TagList tags = QueueTags(queueName);
            tags.Add(TaskHandlerTelemetryNames.AttrOutcome, outcome);
            _TaskDuration.Record(Seconds(enqueuedAt, end), tags);
            if (errorType != null) tags.Add(TaskHandlerTelemetryNames.AttrErrorType, errorType);
            _TasksCompleted.Add(1, tags);
        }

        private static void CloseWithOutcome(Activity activity, string outcome, string errorType, Exception ex, DateTime end)
        {
            if (activity == null) return;

            activity.SetTag(TaskHandlerTelemetryNames.AttrOutcome, outcome);
            if (errorType != null) activity.SetTag(TaskHandlerTelemetryNames.AttrErrorType, errorType);
            if (ex != null) AddException(activity, ex);

            if (outcome == TaskHandlerTelemetryNames.OutcomeSuccess)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else if (outcome == TaskHandlerTelemetryNames.OutcomeCanceled)
            {
                // Cancellation is usually intentional (Stop, Dispose, caller token). Leave the status Unset so it
                // does not count as an error in Tempo; the outcome attribute still identifies it.
                activity.SetStatus(ActivityStatusCode.Unset);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, errorType ?? outcome);
            }

            activity.SetEndTime(end);
            activity.Stop();
        }

        private static void AddException(Activity activity, Exception ex)
        {
            ActivityTagsCollection tags = new ActivityTagsCollection();
            tags["exception.type"] = ErrorType(ex);
            tags["exception.message"] = ex.Message;
            tags["exception.stacktrace"] = ex.ToString();
            activity.AddEvent(new ActivityEvent("exception", default, tags));
        }

        private static Exception Unwrap(Exception ex)
        {
            AggregateException aggregate = ex as AggregateException;
            if (aggregate == null) return ex;

            AggregateException flattened = aggregate.Flatten();
            if (flattened.InnerExceptions.Count == 1) return flattened.InnerExceptions[0];
            return aggregate;
        }

        private static double Seconds(DateTime start, DateTime end)
        {
            double seconds = (end - start).TotalSeconds;
            return seconds < 0 ? 0 : seconds;
        }

        private static List<TaskQueue> LiveQueues()
        {
            List<TaskQueue> queues = new List<TaskQueue>();
            foreach (KeyValuePair<long, WeakReference<TaskQueue>> entry in _Queues)
            {
                if (entry.Value.TryGetTarget(out TaskQueue queue)) queues.Add(queue);
                else _Queues.TryRemove(entry.Key, out WeakReference<TaskQueue> removed);
            }

            return queues;
        }

        private static IEnumerable<Measurement<long>> ObserveSum(Func<TaskQueue, long> selector, Func<TaskQueue, bool> include)
        {
            Dictionary<string, long> totals = new Dictionary<string, long>(StringComparer.Ordinal);

            try
            {
                foreach (TaskQueue queue in LiveQueues())
                {
                    if (!include(queue)) continue;
                    string name = queue.Name;
                    totals.TryGetValue(name, out long current);
                    totals[name] = current + selector(queue);
                }
            }
            catch (Exception)
            {
            }

            return ToMeasurements(totals);
        }

        private static IEnumerable<Measurement<long>> ObserveMax(Func<TaskQueue, long> selector)
        {
            Dictionary<string, long> totals = new Dictionary<string, long>(StringComparer.Ordinal);

            try
            {
                foreach (TaskQueue queue in LiveQueues())
                {
                    string name = queue.Name;
                    long value = selector(queue);
                    if (!totals.TryGetValue(name, out long current) || value > current) totals[name] = value;
                }
            }
            catch (Exception)
            {
            }

            return ToMeasurements(totals);
        }

        private static IEnumerable<Measurement<double>> ObserveLastSuccess()
        {
            Dictionary<string, double> latest = new Dictionary<string, double>(StringComparer.Ordinal);
            List<Measurement<double>> measurements = new List<Measurement<double>>();

            try
            {
                foreach (TaskQueue queue in LiveQueues())
                {
                    double value = queue.LastSuccessUnixSeconds;
                    if (value <= 0) continue;
                    if (!latest.TryGetValue(queue.Name, out double current) || value > current) latest[queue.Name] = value;
                }

                foreach (KeyValuePair<string, double> entry in latest)
                {
                    measurements.Add(new Measurement<double>(entry.Value, new KeyValuePair<string, object>(TaskHandlerTelemetryNames.AttrQueueName, entry.Key)));
                }
            }
            catch (Exception)
            {
            }

            return measurements;
        }

        private static List<Measurement<long>> ToMeasurements(Dictionary<string, long> values)
        {
            List<Measurement<long>> measurements = new List<Measurement<long>>(values.Count);
            foreach (KeyValuePair<string, long> entry in values)
            {
                measurements.Add(new Measurement<long>(entry.Value, new KeyValuePair<string, object>(TaskHandlerTelemetryNames.AttrQueueName, entry.Key)));
            }

            return measurements;
        }

        private static string GetVersion()
        {
            try
            {
                Assembly assembly = typeof(TaskHandlerTelemetry).Assembly;
                AssemblyInformationalVersionAttribute info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string version = info?.InformationalVersion;
                if (String.IsNullOrEmpty(version)) version = assembly.GetName().Version?.ToString() ?? "unknown";

                // Strip SourceLink commit metadata (for example "2.2.0+abc123") to keep the label bounded and readable.
                int plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        #endregion
    }
}
