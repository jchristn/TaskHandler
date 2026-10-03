namespace TaskHandler
{
    /// <summary>
    /// Stable public names for TaskHandler telemetry: the meter and activity source names, every metric
    /// instrument name, every span name, every attribute (label) key, and the bounded attribute values.
    /// These names are a public contract consumed by collectors and dashboards. They do not change
    /// within a major version.
    /// Thread safety: all members are constants and are safe to read from any thread.
    /// </summary>
    public static class TaskHandlerTelemetryNames
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that emits all TaskHandler metrics.
        /// Subscribe a collector to this name (for example Radiant's settings.Sources.AddMeter("TaskHandler")).
        /// </summary>
        public const string MeterName = "TaskHandler";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> that emits all TaskHandler spans.
        /// Subscribe a collector to this name (for example Radiant's settings.Sources.AddActivitySource("TaskHandler")).
        /// </summary>
        public const string ActivitySourceName = "TaskHandler";

        #endregion

        #region Metrics

        /// <summary>
        /// Counter of tasks accepted into a queue. Unit: {task}. Labels: taskhandler.queue.name.
        /// </summary>
        public const string TasksEnqueued = "taskhandler.task.enqueued";

        /// <summary>
        /// Counter of enqueue attempts that were rejected (queue full, queue closed, canceled while waiting for space).
        /// Unit: {task}. Labels: taskhandler.queue.name, error.type.
        /// </summary>
        public const string TasksRejected = "taskhandler.task.rejected";

        /// <summary>
        /// Histogram of the time taken to write a task into the queue, including any backpressure wait on a bounded queue.
        /// Unit: s. Labels: taskhandler.queue.name, taskhandler.outcome.
        /// </summary>
        public const string EnqueueDuration = "taskhandler.queue.enqueue.duration";

        /// <summary>
        /// Counter of tasks that reached a terminal state. Unit: {task}.
        /// Labels: taskhandler.queue.name, taskhandler.outcome, error.type (failure, timeout, and dropped only).
        /// </summary>
        public const string TasksCompleted = "taskhandler.task.completed";

        /// <summary>
        /// Histogram of end-to-end task duration, from enqueue to terminal state. Unit: s.
        /// Labels: taskhandler.queue.name, taskhandler.outcome.
        /// </summary>
        public const string TaskDuration = "taskhandler.task.duration";

        /// <summary>
        /// Histogram of per-stage task duration (queued, slot_wait, execute). Unit: s.
        /// Labels: taskhandler.queue.name, taskhandler.stage, taskhandler.outcome.
        /// </summary>
        public const string StageDuration = "taskhandler.task.stage.duration";

        /// <summary>
        /// Counter of per-stage task events (one per stage a task leaves). Unit: {event}.
        /// Labels: taskhandler.queue.name, taskhandler.stage, taskhandler.outcome.
        /// </summary>
        public const string StageEvents = "taskhandler.task.stage.events";

        /// <summary>
        /// Counter of cancellation requests issued by the queue. Unit: {request}.
        /// Labels: taskhandler.queue.name, taskhandler.cancel.reason.
        /// </summary>
        public const string CancellationRequests = "taskhandler.task.cancellation_requests";

        /// <summary>
        /// Observable gauge of the Unix time (seconds) of the most recent successful task completion.
        /// Unit: s. Labels: taskhandler.queue.name. Not reported until a task succeeds.
        /// </summary>
        public const string LastSuccess = "taskhandler.task.last_success";

        /// <summary>
        /// Observable gauge of tasks waiting in the queue. Unit: {task}. Labels: taskhandler.queue.name.
        /// </summary>
        public const string QueueDepth = "taskhandler.queue.depth";

        /// <summary>
        /// Observable gauge of the configured queue capacity (MaxQueueSize). Reported for bounded queues only.
        /// Unit: {task}. Labels: taskhandler.queue.name.
        /// </summary>
        public const string QueueCapacity = "taskhandler.queue.capacity";

        /// <summary>
        /// Observable gauge of concurrency slots in use (running tasks). Unit: {task}. Labels: taskhandler.queue.name.
        /// </summary>
        public const string ConcurrencyInUse = "taskhandler.concurrency.in_use";

        /// <summary>
        /// Observable gauge of the configured concurrency limit (MaxConcurrentTasks). Unit: {task}.
        /// Labels: taskhandler.queue.name.
        /// </summary>
        public const string ConcurrencyLimit = "taskhandler.concurrency.limit";

        /// <summary>
        /// Observable gauge that is 1 while the queue is started and processing, and 0 otherwise. Unit: {state}.
        /// Labels: taskhandler.queue.name.
        /// </summary>
        public const string QueueProcessing = "taskhandler.queue.processing";

        /// <summary>
        /// Counter of queue lifecycle transitions. Unit: {event}. Labels: taskhandler.queue.name, taskhandler.lifecycle.event.
        /// </summary>
        public const string LifecycleEvents = "taskhandler.queue.lifecycle";

        /// <summary>
        /// Counter of exceptions thrown by user-supplied event handlers (caught and suppressed by the queue).
        /// Unit: {error}. Labels: taskhandler.queue.name, taskhandler.event, error.type.
        /// </summary>
        public const string EventHandlerErrors = "taskhandler.event_handler.errors";

        /// <summary>
        /// Counter of unexpected exceptions that terminated the background task runner. Unit: {error}.
        /// Labels: taskhandler.queue.name, error.type.
        /// </summary>
        public const string RunnerErrors = "taskhandler.runner.errors";

        /// <summary>
        /// Counter of TaskRunWithTimeout.Go operations. Unit: {operation}. Labels: taskhandler.outcome, error.type.
        /// </summary>
        public const string RunWithTimeoutOperations = "taskhandler.run_with_timeout.operations";

        /// <summary>
        /// Histogram of TaskRunWithTimeout.Go duration. Unit: s. Labels: taskhandler.outcome.
        /// </summary>
        public const string RunWithTimeoutDuration = "taskhandler.run_with_timeout.duration";

        /// <summary>
        /// Observable gauge, always 1, carrying the library version. Unit: {info}. Labels: taskhandler.version.
        /// </summary>
        public const string BuildInfo = "taskhandler.build.info";

        #endregion

        #region Spans

        /// <summary>
        /// Producer span covering a single enqueue call (including backpressure wait).
        /// </summary>
        public const string SpanEnqueue = "taskhandler enqueue";

        /// <summary>
        /// Consumer span covering one task job end to end (enqueue to terminal state). Parent: the enqueue span.
        /// </summary>
        public const string SpanTask = "taskhandler task";

        /// <summary>
        /// Child span of the task span covering time spent waiting in the queue.
        /// </summary>
        public const string SpanStageQueued = "stage:queued";

        /// <summary>
        /// Child span of the task span covering time spent waiting for a concurrency slot.
        /// </summary>
        public const string SpanStageSlotWait = "stage:slot_wait";

        /// <summary>
        /// Child span of the task span covering user code execution. Spans started by user code nest under it.
        /// </summary>
        public const string SpanStageExecute = "stage:execute";

        /// <summary>
        /// Internal span covering one TaskRunWithTimeout.Go call.
        /// </summary>
        public const string SpanRunWithTimeout = "taskhandler run_with_timeout";

        #endregion

        #region Attributes

        /// <summary>
        /// Attribute key: queue name (TaskQueue.Name). Bounded, metric-safe.
        /// </summary>
        public const string AttrQueueName = "taskhandler.queue.name";

        /// <summary>
        /// Attribute key: outcome. Bounded, metric-safe. See the Outcome* constants.
        /// </summary>
        public const string AttrOutcome = "taskhandler.outcome";

        /// <summary>
        /// Attribute key: stage. Bounded, metric-safe. See the Stage* constants.
        /// </summary>
        public const string AttrStage = "taskhandler.stage";

        /// <summary>
        /// Attribute key: cancellation reason. Bounded, metric-safe. See the CancelReason* constants.
        /// </summary>
        public const string AttrCancelReason = "taskhandler.cancel.reason";

        /// <summary>
        /// Attribute key: lifecycle event. Bounded, metric-safe. See the Lifecycle* constants.
        /// </summary>
        public const string AttrLifecycleEvent = "taskhandler.lifecycle.event";

        /// <summary>
        /// Attribute key: event handler name (for example OnTaskFinished). Bounded, metric-safe.
        /// </summary>
        public const string AttrEvent = "taskhandler.event";

        /// <summary>
        /// Attribute key: library version. Used on taskhandler.build.info only.
        /// </summary>
        public const string AttrVersion = "taskhandler.version";

        /// <summary>
        /// Attribute key (OpenTelemetry semantic convention): error type. The exception's full type name, or a
        /// short code such as queue_full. Bounded, metric-safe.
        /// </summary>
        public const string AttrErrorType = "error.type";

        /// <summary>
        /// Span-only attribute key: task GUID. High cardinality, never used as a metric label.
        /// </summary>
        public const string AttrTaskId = "taskhandler.task.id";

        /// <summary>
        /// Span-only attribute key: user-supplied task name. High cardinality, never used as a metric label.
        /// </summary>
        public const string AttrTaskName = "taskhandler.task.name";

        /// <summary>
        /// Span-only attribute key: task priority.
        /// </summary>
        public const string AttrTaskPriority = "taskhandler.task.priority";

        /// <summary>
        /// Span-only attribute key: queue depth observed when the task was enqueued.
        /// </summary>
        public const string AttrQueueDepth = "taskhandler.queue.depth_at_enqueue";

        /// <summary>
        /// Span-only attribute key: configured concurrency limit observed when the task started.
        /// </summary>
        public const string AttrConcurrencyLimit = "taskhandler.concurrency.limit_at_start";

        /// <summary>
        /// Span-only attribute key: configured timeout in milliseconds (TaskRunWithTimeout).
        /// </summary>
        public const string AttrTimeoutMs = "taskhandler.timeout_ms";

        #endregion

        #region Values

        /// <summary>
        /// Outcome value: the operation completed successfully.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome value: the operation threw an exception other than a timeout.
        /// </summary>
        public const string OutcomeFailure = "failure";

        /// <summary>
        /// Outcome value: the operation exceeded its timeout.
        /// </summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>
        /// Outcome value: the operation was canceled.
        /// </summary>
        public const string OutcomeCanceled = "canceled";

        /// <summary>
        /// Outcome value: the task was removed from the queue but never ran because the queue stopped first.
        /// </summary>
        public const string OutcomeDropped = "dropped";

        /// <summary>
        /// Outcome value: an enqueue attempt was rejected.
        /// </summary>
        public const string OutcomeRejected = "rejected";

        /// <summary>
        /// Stage value: waiting in the queue.
        /// </summary>
        public const string StageQueued = "queued";

        /// <summary>
        /// Stage value: waiting for a concurrency slot.
        /// </summary>
        public const string StageSlotWait = "slot_wait";

        /// <summary>
        /// Stage value: executing user code.
        /// </summary>
        public const string StageExecute = "execute";

        /// <summary>
        /// Cancellation reason value: Stop() canceled all running tasks.
        /// </summary>
        public const string CancelReasonStopAll = "stop_all";

        /// <summary>
        /// Cancellation reason value: Stop(Guid) canceled one task.
        /// </summary>
        public const string CancelReasonStopTask = "stop_task";

        /// <summary>
        /// Cancellation reason value: Dispose() canceled running tasks.
        /// </summary>
        public const string CancelReasonDispose = "dispose";

        /// <summary>
        /// Lifecycle value: the queue started processing.
        /// </summary>
        public const string LifecycleStart = "start";

        /// <summary>
        /// Lifecycle value: the queue stopped processing.
        /// </summary>
        public const string LifecycleStop = "stop";

        /// <summary>
        /// Lifecycle value: the queue was disposed.
        /// </summary>
        public const string LifecycleDispose = "dispose";

        /// <summary>
        /// Error type value: a bounded queue was full.
        /// </summary>
        public const string ErrorQueueFull = "queue_full";

        /// <summary>
        /// Error type value: the queue was closed (stopped) during the enqueue attempt.
        /// </summary>
        public const string ErrorQueueClosed = "queue_closed";

        #endregion
    }
}
