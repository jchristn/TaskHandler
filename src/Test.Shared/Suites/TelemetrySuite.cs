namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Test.Shared.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Tests proving that TaskHandler emits its documented metrics and spans (see TELEMETRY.md) through the
    /// BCL Meter and ActivitySource named "TaskHandler", across success, failure, timeout, cancellation,
    /// rejection, drop, and event-handler-error paths, and that emission is harmless with no listener.
    /// Each test uses a unique queue name so results are isolated from concurrently running tests.
    /// </summary>
    public static class TelemetrySuite
    {
        private const string Id = "Telemetry";

        /// <summary>
        /// Build the telemetry test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "SourceNames", "Meter and ActivitySource use the stable public name TaskHandler with a version", async ct =>
                {
                    Check.Equal("TaskHandler", TaskHandlerTelemetryNames.MeterName, "meter name");
                    Check.Equal("TaskHandler", TaskHandlerTelemetryNames.ActivitySourceName, "activity source name");

                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        using (TaskQueue queue = new TaskQueue(new TaskQueueOptions { Name = queueName }))
                        {
                            queue.AddTask(Guid.NewGuid(), "probe", null, token => Task.CompletedTask);
                        }

                        Check.True(capture.PublishedMeters.Any(m => m.StartsWith("TaskHandler@") && m.Length > "TaskHandler@".Length),
                            "TaskHandler meter should be published with a non-empty version");
                    }

                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "SuccessPathMetrics", "Successful tasks emit enqueue, per-stage, duration, and outcome metrics", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();

                        for (int i = 0; i < 3; i++)
                        {
                            await queue.EnqueueAsync("ok-" + i, async token => await Task.Delay(20, token));
                        }

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Sum(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess) >= 3),
                            "three successful completions recorded");

                        Check.Equal(3.0, capture.Sum(TaskHandlerTelemetryNames.TasksEnqueued, queueName), "enqueued count");
                        Check.Equal(3, capture.Count(TaskHandlerTelemetryNames.EnqueueDuration, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess), "enqueue duration observations");
                        Check.Equal(3, capture.Count(TaskHandlerTelemetryNames.TaskDuration, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess), "task duration observations");

                        foreach (string stage in new[] { TaskHandlerTelemetryNames.StageQueued, TaskHandlerTelemetryNames.StageSlotWait, TaskHandlerTelemetryNames.StageExecute })
                        {
                            Check.Equal(3, capture.Count(TaskHandlerTelemetryNames.StageDuration, queueName, TaskHandlerTelemetryNames.AttrStage, stage), "stage duration observations for " + stage);
                            Check.Equal(3.0, capture.Sum(TaskHandlerTelemetryNames.StageEvents, queueName, TaskHandlerTelemetryNames.AttrStage, stage), "stage events for " + stage);
                        }

                        List<CapturedMeasurement> execute = capture.Measurements(TaskHandlerTelemetryNames.StageDuration, queueName)
                            .Where(m => m.Tag(TaskHandlerTelemetryNames.AttrStage) == TaskHandlerTelemetryNames.StageExecute)
                            .ToList();
                        Check.True(execute.All(m => m.Value >= 0.015), "execute stage durations are in seconds and reflect the 20ms work");
                        Check.True(execute.All(m => m.Unit == "s"), "duration unit is seconds");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "FailurePath", "Faulted tasks record outcome failure with error.type, and spans carry Error status and an exception event", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();

                        await queue.EnqueueAsync("boom", token => throw new InvalidOperationException("synthetic failure"));

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeFailure) >= 1
                            && capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Count >= 1),
                            "failure recorded");

                        CapturedMeasurement completed = capture.Measurements(TaskHandlerTelemetryNames.TasksCompleted, queueName).Single();
                        Check.Equal(typeof(InvalidOperationException).FullName, completed.Tag(TaskHandlerTelemetryNames.AttrErrorType), "error.type label");

                        Activity job = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Single();
                        Check.Equal(ActivityStatusCode.Error, job.Status, "job span status");
                        Activity execute = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageExecute, job.SpanId).Single();
                        Check.Equal(ActivityStatusCode.Error, execute.Status, "execute span status");
                        ActivityEvent exceptionEvent = execute.Events.Single(e => e.Name == "exception");
                        Check.Equal(typeof(InvalidOperationException).FullName, exceptionEvent.Tags.First(t => t.Key == "exception.type").Value as string, "exception.type");
                        Check.Equal("synthetic failure", exceptionEvent.Tags.First(t => t.Key == "exception.message").Value as string, "exception.message");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TimeoutPath", "Tasks exceeding their timeout record outcome timeout with error.type System.TimeoutException", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();

                        await queue.EnqueueAsync("slow", async token => await Task.Delay(5000, token), timeout: TimeSpan.FromMilliseconds(50));
                        TaskHandle<int> handle = await queue.EnqueueAsync<int>("slow-result", async token =>
                        {
                            await Task.Delay(5000, token);
                            return 1;
                        }, timeout: TimeSpan.FromMilliseconds(50));

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeTimeout) >= 2),
                            "two timeouts recorded");

                        Check.True(capture.Measurements(TaskHandlerTelemetryNames.TasksCompleted, queueName)
                            .All(m => m.Tag(TaskHandlerTelemetryNames.AttrErrorType) == typeof(TimeoutException).FullName), "timeout error.type");
                        Check.True(capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName)
                            .All(a => a.Status == ActivityStatusCode.Error && (a.GetTagItem(TaskHandlerTelemetryNames.AttrOutcome) as string) == TaskHandlerTelemetryNames.OutcomeTimeout),
                            "job spans marked timeout and Error");

                        await Check.ThrowsAsync<TimeoutException>(async () => await handle.Task, "handle surfaces TimeoutException");
                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "CancelTaskPath", "Stop(Guid) records a stop_task cancellation request and outcome canceled with Unset span status", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();

                        Guid id = await queue.EnqueueAsync("long", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task running");
                        queue.Stop(id);

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeCanceled) >= 1
                            && capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Count >= 1),
                            "cancellation recorded");

                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.CancellationRequests, queueName, TaskHandlerTelemetryNames.AttrCancelReason, TaskHandlerTelemetryNames.CancelReasonStopTask), "stop_task request");
                        CapturedMeasurement completed = capture.Measurements(TaskHandlerTelemetryNames.TasksCompleted, queueName).Single();
                        Check.Null(completed.Tag(TaskHandlerTelemetryNames.AttrErrorType), "no error.type on canceled");

                        Activity job = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Single();
                        Check.Equal(ActivityStatusCode.Unset, job.Status, "canceled job span status");
                        Activity execute = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageExecute, job.SpanId).Single();
                        Check.True(execute.Events.Any(e => e.Name == "cancellation_requested"), "execute span carries cancellation_requested event");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "LifecycleAndStopAll", "Start, Stop, and Dispose record lifecycle events; Stop() records stop_all cancellations", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();
                        await queue.EnqueueAsync("a", async token => await Task.Delay(10000, token));
                        await queue.EnqueueAsync("b", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 2), "two running");

                        queue.Stop();
                        queue.Dispose();

                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.LifecycleEvents, queueName, TaskHandlerTelemetryNames.AttrLifecycleEvent, TaskHandlerTelemetryNames.LifecycleStart), "start event");
                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.LifecycleEvents, queueName, TaskHandlerTelemetryNames.AttrLifecycleEvent, TaskHandlerTelemetryNames.LifecycleStop), "stop event");
                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.LifecycleEvents, queueName, TaskHandlerTelemetryNames.AttrLifecycleEvent, TaskHandlerTelemetryNames.LifecycleDispose), "dispose event");
                        Check.Equal(2.0, capture.Sum(TaskHandlerTelemetryNames.CancellationRequests, queueName, TaskHandlerTelemetryNames.AttrCancelReason, TaskHandlerTelemetryNames.CancelReasonStopAll), "stop_all requests");

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeCanceled) >= 2),
                            "both tasks canceled");
                    }
                }),

                TaskHandlerSuites.Case(Id, "DisposeCancellationReason", "Dispose() of a running queue records dispose cancellation requests", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();
                        await queue.EnqueueAsync("a", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "running");

                        queue.Dispose();

                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.CancellationRequests, queueName, TaskHandlerTelemetryNames.AttrCancelReason, TaskHandlerTelemetryNames.CancelReasonDispose), "dispose request");
                    }
                }),

                TaskHandlerSuites.Case(Id, "RejectedQueueFull", "AddTask on a full bounded queue records taskhandler.task.rejected with error.type queue_full and an Error enqueue span", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        using (TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.MaxQueueSize = 1; }))
                        {
                            queue.AddTask(Guid.NewGuid(), "first", null, token => Task.CompletedTask);
                            Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "second", null, token => Task.CompletedTask), "second add rejected");
                        }

                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.TasksRejected, queueName, TaskHandlerTelemetryNames.AttrErrorType, TaskHandlerTelemetryNames.ErrorQueueFull), "queue_full rejection");
                        Check.Equal(1, capture.Count(TaskHandlerTelemetryNames.EnqueueDuration, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeRejected), "rejected enqueue duration");

                        List<Activity> enqueueSpans = capture.Spans(TaskHandlerTelemetryNames.SpanEnqueue, queueName);
                        Check.Equal(2, enqueueSpans.Count, "two enqueue spans");
                        Check.Equal(1, enqueueSpans.Count(a => a.Status == ActivityStatusCode.Error), "one Error enqueue span");
                        Check.Equal(1, enqueueSpans.Count(a => a.Status == ActivityStatusCode.Ok), "one Ok enqueue span");
                        Check.True(enqueueSpans.All(a => a.Kind == ActivityKind.Producer), "enqueue spans are Producer kind");
                    }

                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "RejectedBackpressureCanceled", "AddTaskAsync canceled while waiting on a full bounded queue records a canceled rejection", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        using (TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.MaxQueueSize = 1; }))
                        {
                            await queue.AddTaskAsync(Guid.NewGuid(), "first", null, token => Task.CompletedTask);
                            using (CancellationTokenSource cts = new CancellationTokenSource(100))
                            {
                                await Check.ThrowsAsync<OperationCanceledException>(async () =>
                                    await queue.AddTaskAsync(Guid.NewGuid(), "second", null, token => Task.CompletedTask, cts.Token), "blocked add canceled");
                            }
                        }

                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.TasksRejected, queueName, TaskHandlerTelemetryNames.AttrErrorType, TaskHandlerTelemetryNames.OutcomeCanceled), "canceled rejection");
                        CapturedMeasurement rejectedWait = capture.Measurements(TaskHandlerTelemetryNames.EnqueueDuration, queueName)
                            .Single(m => m.Tag(TaskHandlerTelemetryNames.AttrOutcome) == TaskHandlerTelemetryNames.OutcomeRejected);
                        Check.True(rejectedWait.Value >= 0.08, "backpressure wait is captured in the enqueue duration");
                    }
                }),

                TaskHandlerSuites.Case(Id, "DroppedPath", "Stop() retains unstarted tasks without a dropped outcome; Dispose() drops them from both the queue and the slot wait", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.MaxConcurrentTasks = 1; });
                        queue.Start();

                        // The first task ignores cancellation so the only slot stays held after Stop().
                        await queue.EnqueueAsync("holder", async token => await Task.Delay(400));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "holder running");
                        await queue.EnqueueAsync("waiter", async token => await Task.Delay(10, token));
                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Measurements(TaskHandlerTelemetryNames.StageEvents, queueName)
                                .Count(m => m.Tag(TaskHandlerTelemetryNames.AttrStage) == TaskHandlerTelemetryNames.StageQueued) == 2),
                            "waiter read from the queue and waiting for a slot");

                        queue.Stop();
                        await queue.EnqueueAsync("queued", async token => await Task.Delay(10, token));
                        await Task.Delay(100);

                        Check.Equal(0, capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeDropped), "nothing dropped by Stop()");
                        Check.Equal(2, queue.QueuedCount, "waiter and queued are retained");

                        queue.Dispose();

                        Check.Equal(2, capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeDropped), "both dropped by Dispose()");
                        List<CapturedMeasurement> droppedStages = capture.Measurements(TaskHandlerTelemetryNames.StageEvents, queueName)
                            .Where(m => m.Tag(TaskHandlerTelemetryNames.AttrOutcome) == TaskHandlerTelemetryNames.OutcomeDropped)
                            .ToList();
                        Check.Equal(1, droppedStages.Count(m => m.Tag(TaskHandlerTelemetryNames.AttrStage) == TaskHandlerTelemetryNames.StageSlotWait), "slot_wait stage dropped");
                        Check.Equal(1, droppedStages.Count(m => m.Tag(TaskHandlerTelemetryNames.AttrStage) == TaskHandlerTelemetryNames.StageQueued), "queued stage dropped");
                        Check.True(capture.Measurements(TaskHandlerTelemetryNames.TasksCompleted, queueName)
                            .Where(m => m.Tag(TaskHandlerTelemetryNames.AttrOutcome) == TaskHandlerTelemetryNames.OutcomeDropped)
                            .All(m => m.Tag(TaskHandlerTelemetryNames.AttrErrorType) == TaskHandlerTelemetryNames.ErrorQueueClosed), "dropped error.type is queue_closed");

                        List<Activity> dropped = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName)
                            .Where(a => (a.GetTagItem(TaskHandlerTelemetryNames.AttrOutcome) as string) == TaskHandlerTelemetryNames.OutcomeDropped)
                            .ToList();
                        Check.Equal(2, dropped.Count, "dropped job spans");
                        Check.True(dropped.All(a => a.Status == ActivityStatusCode.Error), "dropped job span status");
                        Check.True(dropped.Any(a => (a.GetTagItem(TaskHandlerTelemetryNames.AttrTaskName) as string) == "waiter"), "waiter span");
                        Check.True(dropped.Any(a => (a.GetTagItem(TaskHandlerTelemetryNames.AttrTaskName) as string) == "queued"), "never-dequeued task still gets a job span");
                        Activity waiterJob = dropped.Single(a => (a.GetTagItem(TaskHandlerTelemetryNames.AttrTaskName) as string) == "waiter");
                        Activity waiterSlot = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageSlotWait, waiterJob.SpanId).Single();
                        Check.Equal(ActivityStatusCode.Error, waiterSlot.Status, "interrupted slot_wait span closed with Error");

                        await Task.Delay(500);
                    }
                }),

                TaskHandlerSuites.Case(Id, "CancelQueuedTaskPath", "Stop(guid) on a task still in the queue records a stop_task request and a canceled outcome without running it", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        using (TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.MaxConcurrentTasks = 1; }))
                        {
                            bool ran = false;
                            Guid guid = await queue.EnqueueAsync("victim", async token => { ran = true; await Task.Delay(10, token); });
                            queue.Stop(guid);
                            queue.Start();

                            Check.True(await Check.WaitUntilAsync(() =>
                                capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeCanceled) == 1),
                                "canceled outcome recorded");
                            Check.False(ran, "canceled queued task must not run");
                            Check.Equal(1, capture.Count(TaskHandlerTelemetryNames.CancellationRequests, queueName, TaskHandlerTelemetryNames.AttrCancelReason, TaskHandlerTelemetryNames.CancelReasonStopTask), "stop_task request");
                            Activity job = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Single();
                            Check.Equal(TaskHandlerTelemetryNames.OutcomeCanceled, job.GetTagItem(TaskHandlerTelemetryNames.AttrOutcome) as string, "job span outcome");
                            Check.Equal(0, queue.QueuedCount, "queue empty");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "ObservableGauges", "Depth, capacity, in-use, limit, processing, last-success, and build-info gauges report live state", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.MaxConcurrentTasks = 2; o.MaxQueueSize = 5; });
                        TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                        for (int i = 0; i < 3; i++) await queue.EnqueueAsync("gate-" + i, async token => await release.Task);

                        capture.RecordObservables();
                        Check.Equal(3.0, capture.Latest(TaskHandlerTelemetryNames.QueueDepth, queueName), "depth before start");
                        Check.Equal(5.0, capture.Latest(TaskHandlerTelemetryNames.QueueCapacity, queueName), "capacity");
                        Check.Equal(2.0, capture.Latest(TaskHandlerTelemetryNames.ConcurrencyLimit, queueName), "limit");
                        Check.Equal(0.0, capture.Latest(TaskHandlerTelemetryNames.ConcurrencyInUse, queueName), "in use before start");
                        Check.Equal(0.0, capture.Latest(TaskHandlerTelemetryNames.QueueProcessing, queueName), "processing before start");
                        Check.Null(capture.Latest(TaskHandlerTelemetryNames.LastSuccess, queueName), "no last success yet");

                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 2), "two running");

                        capture.RecordObservables();
                        Check.Equal(2.0, capture.Latest(TaskHandlerTelemetryNames.ConcurrencyInUse, queueName), "in use at limit");
                        Check.Equal(1.0, capture.Latest(TaskHandlerTelemetryNames.QueueDepth, queueName), "depth counts the task waiting for a slot");
                        Check.Equal(1.0, capture.Latest(TaskHandlerTelemetryNames.QueueProcessing, queueName), "processing");

                        release.SetResult(true);
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Sum(TaskHandlerTelemetryNames.TasksCompleted, queueName) >= 3), "all completed");

                        capture.RecordObservables();
                        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                        Check.InRange(capture.Latest(TaskHandlerTelemetryNames.LastSuccess, queueName) ?? 0, now - 30, now + 1, "last success unix seconds");

                        CapturedMeasurement build = capture.Measurements(TaskHandlerTelemetryNames.BuildInfo, null).Last();
                        Check.Equal(1.0, build.Value, "build info value");
                        Check.True(!String.IsNullOrEmpty(build.Tag(TaskHandlerTelemetryNames.AttrVersion)), "build info version label");

                        queue.Dispose();
                        capture.RecordObservables();
                        int before = capture.Measurements(TaskHandlerTelemetryNames.ConcurrencyLimit, queueName).Count;
                        capture.RecordObservables();
                        Check.Equal(before, capture.Measurements(TaskHandlerTelemetryNames.ConcurrencyLimit, queueName).Count, "disposed queue is no longer reported");
                    }
                }),

                TaskHandlerSuites.Case(Id, "EventHandlerErrors", "Exceptions thrown by user event handlers are counted by event and error.type", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o =>
                        {
                            o.Name = queueName;
                            o.OnTaskAdded = (s, e) => throw new ArgumentException("handler bug");
                            o.OnTaskFinished = (s, e) => throw new InvalidOperationException("handler bug");
                        });
                        queue.Start();
                        await queue.EnqueueAsync("x", token => Task.CompletedTask);

                        Check.True(await Check.WaitUntilAsync(() =>
                            capture.Sum(TaskHandlerTelemetryNames.EventHandlerErrors, queueName) >= 2), "two handler errors");
                        Check.Equal(1.0, capture.Sum(TaskHandlerTelemetryNames.EventHandlerErrors, queueName, TaskHandlerTelemetryNames.AttrEvent, "OnTaskAdded"), "OnTaskAdded error");
                        CapturedMeasurement finished = capture.Measurements(TaskHandlerTelemetryNames.EventHandlerErrors, queueName)
                            .Single(m => m.Tag(TaskHandlerTelemetryNames.AttrEvent) == "OnTaskFinished");
                        Check.Equal(typeof(InvalidOperationException).FullName, finished.Tag(TaskHandlerTelemetryNames.AttrErrorType), "handler error.type");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TracePropagation", "Caller context flows enqueue -> task job -> stages -> user code, all in one trace", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();

                        Activity? seenInsideTask = null;
                        ActivityTraceId callerTrace;
                        ActivitySpanId callerSpan;
                        Guid taskId;

                        using (Activity? caller = TelemetryCapture.CallerSource.StartActivity("caller request", ActivityKind.Server))
                        {
                            Check.NotNull(caller, "caller span");
                            callerTrace = caller!.TraceId;
                            callerSpan = caller.SpanId;
                            taskId = await queue.EnqueueAsync("traced", async token =>
                            {
                                seenInsideTask = Activity.Current;
                                using (Activity? user = TelemetryCapture.CallerSource.StartActivity("user work"))
                                {
                                    await Task.Delay(10, token);
                                }
                            }, priority: 3);
                        }

                        Check.True(await Check.WaitUntilAsync(() => capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Count == 1), "job span stopped");

                        Activity enqueue = capture.Spans(TaskHandlerTelemetryNames.SpanEnqueue, queueName).Single();
                        Check.Equal(callerTrace, enqueue.TraceId, "enqueue span joins caller trace");
                        Check.Equal(callerSpan, enqueue.ParentSpanId, "enqueue span parent is caller");

                        Activity job = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Single();
                        Check.Equal(ActivityKind.Consumer, job.Kind, "job span kind");
                        Check.Equal(callerTrace, job.TraceId, "job span joins caller trace across the background hand-off");
                        Check.Equal(enqueue.SpanId, job.ParentSpanId, "job span parent is enqueue span");
                        Check.Equal(ActivityStatusCode.Ok, job.Status, "job span status");
                        Check.Equal(taskId.ToString(), job.GetTagItem(TaskHandlerTelemetryNames.AttrTaskId) as string, "task id on span");
                        Check.Equal("traced", job.GetTagItem(TaskHandlerTelemetryNames.AttrTaskName) as string, "task name on span");
                        Check.Equal(3, Convert.ToInt32(job.GetTagItem(TaskHandlerTelemetryNames.AttrTaskPriority)), "priority on span");
                        Check.True(job.StartTimeUtc <= enqueue.StartTimeUtc.Add(enqueue.Duration), "job span is back-dated to enqueue time");

                        Activity queued = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageQueued, job.SpanId).Single();
                        Activity slotWait = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageSlotWait, job.SpanId).Single();
                        Activity execute = capture.ChildSpans(TaskHandlerTelemetryNames.SpanStageExecute, job.SpanId).Single();
                        Check.Equal(ActivityStatusCode.Ok, queued.Status, "queued status");
                        Check.Equal(ActivityStatusCode.Ok, slotWait.Status, "slot_wait status");
                        Check.Equal(ActivityStatusCode.Ok, execute.Status, "execute status");

                        Check.NotNull(seenInsideTask, "Activity.Current inside task");
                        Check.Equal(execute.SpanId, seenInsideTask!.SpanId, "user code runs under the execute span");
                        Activity user = capture.Spans("user work", null).Single(a => a.TraceId == callerTrace);
                        Check.Equal(execute.SpanId, user.ParentSpanId, "user span nests under execute span");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "NoAmbientLeakFromStart", "Tasks enqueued without a caller span do not inherit the span that was current at Start()", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        ActivityTraceId startTrace;
                        using (Activity? starter = TelemetryCapture.CallerSource.StartActivity("starter"))
                        {
                            startTrace = starter!.TraceId;
                            queue.Start();
                        }

                        await queue.EnqueueAsync("orphan", token => Task.CompletedTask);
                        Check.True(await Check.WaitUntilAsync(() => capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Count == 1), "job span stopped");
                        Activity job = capture.Spans(TaskHandlerTelemetryNames.SpanTask, queueName).Single();
                        Check.True(job.TraceId != startTrace, "job span is not parented to the Start() caller");

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "RunWithTimeoutTelemetry", "TaskRunWithTimeout.Go records success, timeout, and failure outcomes with spans", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        int marker = new Random().Next(100000, 999999);

                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            await TaskRunWithTimeout.Go(Task.FromResult(1), marker, cts);
                        }

                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            Task<int> slow = Task.Run(async () => { await Task.Delay(2000, cts.Token); return 1; });
                            await Check.ThrowsAsync<TimeoutException>(async () => await TaskRunWithTimeout.Go(slow, 50, cts), "timeout");
                        }

                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            Task<int> failing = Task.FromException<int>(new FormatException("bad"));
                            await Check.ThrowsAsync<AggregateException>(async () => await TaskRunWithTimeout.Go(failing, marker, cts), "failure");
                        }

                        Check.True(capture.Count(TaskHandlerTelemetryNames.RunWithTimeoutOperations, null, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeSuccess) >= 1, "success counted");
                        Check.True(capture.Count(TaskHandlerTelemetryNames.RunWithTimeoutOperations, null, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeTimeout) >= 1, "timeout counted");
                        CapturedMeasurement failure = capture.Measurements(TaskHandlerTelemetryNames.RunWithTimeoutOperations, null)
                            .First(m => m.Tag(TaskHandlerTelemetryNames.AttrOutcome) == TaskHandlerTelemetryNames.OutcomeFailure);
                        Check.Equal(typeof(FormatException).FullName, failure.Tag(TaskHandlerTelemetryNames.AttrErrorType), "failure error.type is unwrapped");
                        Check.True(capture.Count(TaskHandlerTelemetryNames.RunWithTimeoutDuration, null) >= 3, "durations recorded");

                        List<Activity> spans = capture.Spans(TaskHandlerTelemetryNames.SpanRunWithTimeout, null)
                            .Where(a => Convert.ToInt32(a.GetTagItem(TaskHandlerTelemetryNames.AttrTimeoutMs)) == marker || Convert.ToInt32(a.GetTagItem(TaskHandlerTelemetryNames.AttrTimeoutMs)) == 50)
                            .ToList();
                        Check.True(spans.Any(a => a.Status == ActivityStatusCode.Ok), "Ok span for success");
                        Check.True(spans.Count(a => a.Status == ActivityStatusCode.Error) >= 2, "Error spans for timeout and failure");
                    }
                }),

                TaskHandlerSuites.Case(Id, "BoundedLabels", "No metric carries a task id or task name label; every TaskHandler metric is low-cardinality", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = NewQueueName();
                        TaskQueue queue = TaskQueue.Create(o => o.Name = queueName);
                        queue.Start();
                        for (int i = 0; i < 5; i++) await queue.EnqueueAsync("unique-" + Guid.NewGuid(), token => Task.CompletedTask);
                        await queue.EnqueueAsync("fail", token => throw new InvalidOperationException());
                        Check.True(await Check.WaitUntilAsync(() => capture.Sum(TaskHandlerTelemetryNames.TasksCompleted, queueName) >= 6), "completed");
                        capture.RecordObservables();

                        HashSet<string> allowed = new HashSet<string>
                        {
                            TaskHandlerTelemetryNames.AttrQueueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.AttrStage,
                            TaskHandlerTelemetryNames.AttrCancelReason, TaskHandlerTelemetryNames.AttrLifecycleEvent, TaskHandlerTelemetryNames.AttrEvent,
                            TaskHandlerTelemetryNames.AttrVersion, TaskHandlerTelemetryNames.AttrErrorType
                        };

                        List<CapturedMeasurement> all = capture.AllMeasurements.ToList();
                        Check.True(all.Count > 0, "measurements captured");
                        foreach (CapturedMeasurement m in all)
                        {
                            foreach (string key in m.Tags.Keys)
                            {
                                Check.True(allowed.Contains(key), "metric " + m.Instrument + " uses only bounded label keys (found " + key + ")");
                            }

                            Check.True(m.Instrument.StartsWith("taskhandler."), "metric " + m.Instrument + " is prefixed with the product name");
                        }

                        await queue.DisposeAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "NoListenerSafe", "With no listener subscribed every code path still works and never throws", async ct =>
                {
                    TaskQueue queue = TaskQueue.Create(o => { o.Name = NewQueueName(); o.MaxQueueSize = 10; o.OnTaskAdded = (s, e) => throw new Exception("handler"); });
                    queue.Start();
                    int ran = 0;
                    await queue.EnqueueAsync("ok", token => { Interlocked.Increment(ref ran); return Task.CompletedTask; });
                    await queue.EnqueueAsync("fail", token => throw new InvalidOperationException());
                    await queue.EnqueueAsync("slow", async token => await Task.Delay(1000, token), timeout: TimeSpan.FromMilliseconds(20));
                    TaskHandle<int> handle = await queue.EnqueueAsync<int>("result", token => Task.FromResult(42));
                    Check.Equal(42, await handle.Task, "result");
                    await queue.WaitForCompletionAsync();
                    Check.Equal(1, ran, "task body ran");
                    Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalFailed >= 2), "failure and timeout still counted in statistics");

                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        Check.Equal(7, await TaskRunWithTimeout.Go(Task.FromResult(7), 1000, cts), "run with timeout");
                    }

                    await queue.StopAsync();
                    await queue.DisposeAsync();
                }),

                TaskHandlerSuites.Case(Id, "QueueNameValidation", "TaskQueue.Name and TaskQueueOptions.Name default to 'default' and reject null or empty", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Equal("default", queue.Name, "default queue name");
                        Check.Throws<ArgumentNullException>(() => queue.Name = "", "empty name");
                        Check.Throws<ArgumentNullException>(() => queue.Name = null!, "null name");
                        queue.Name = "ingest";
                        Check.Equal("ingest", queue.Name, "assigned name");
                    }

                    TaskQueueOptions options = new TaskQueueOptions();
                    Check.Equal("default", options.Name, "default option name");
                    Check.Throws<ArgumentNullException>(() => options.Name = "", "empty option name");
                    using (TaskQueue queue = new TaskQueue(new TaskQueueOptions { Name = "email" }))
                    {
                        Check.Equal("email", queue.Name, "name from options");
                    }

                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "Telemetry (metrics and traces)", cases);
        }

        private static string NewQueueName()
        {
            return "test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }
}
