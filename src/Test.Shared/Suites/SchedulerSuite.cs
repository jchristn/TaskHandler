namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using QoSKit;
    using TaskHandler;
    using Test.Shared.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the default FIFO order and for opt-in QoSKit schedulers (priority, fairness, capacity policies).
    /// </summary>
    public static class SchedulerSuite
    {
        private const string Id = "Scheduler";

        /// <summary>
        /// Build the scheduler test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "DefaultIsFifo", "Without a scheduler, tasks start in the order added regardless of priority", async ct =>
                {
                    List<string> order = new List<string>();
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1))
                    {
                        await EnqueueRecorded(queue, order, "background", (int)TaskPriority.Background);
                        await EnqueueRecorded(queue, order, "urgent", (int)TaskPriority.Urgent);
                        await EnqueueRecorded(queue, order, "normal", (int)TaskPriority.Normal);
                        Check.Null(queue.Scheduler, "no scheduler by default");
                        queue.Start();
                        Check.True(await WaitForCount(order, 3), "all ran");
                        Check.Equal("background,urgent,normal", Joined(order), "FIFO order");
                    }
                }),

                TaskHandlerSuites.Case(Id, "PriorityOrder", "A PriorityQoSQueue scheduler starts lower priority values first", async ct =>
                {
                    List<string> order = new List<string>();
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        await EnqueueRecorded(queue, order, "background", (int)TaskPriority.Background);
                        await EnqueueRecorded(queue, order, "normal", (int)TaskPriority.Normal);
                        await EnqueueRecorded(queue, order, "urgent", (int)TaskPriority.Urgent);
                        await EnqueueRecorded(queue, order, "high", (int)TaskPriority.High);
                        queue.Start();
                        Check.True(await WaitForCount(order, 4), "all ran");
                        Check.Equal("urgent,high,normal,background", Joined(order), "priority order");
                    }
                }),

                TaskHandlerSuites.Case(Id, "UrgentOvertakesBacklog", "An urgent task added behind a running backlog starts before the waiting low-priority tasks", async ct =>
                {
                    List<string> order = new List<string>();
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        for (int i = 0; i < 4; i++) await EnqueueRecorded(queue, order, "low" + i, (int)TaskPriority.Low, 40);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "backlog running");
                        await EnqueueRecorded(queue, order, "urgent", (int)TaskPriority.Urgent);
                        Check.True(await WaitForCount(order, 5), "all ran");
                        lock (order)
                        {
                            Check.Equal(1, order.IndexOf("urgent"), $"urgent starts right after the running task (order {Joined(order)})");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "FifoWithinPriority", "Tasks with equal priority start in the order added", async ct =>
                {
                    List<string> order = new List<string>();
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 5; i++) await EnqueueRecorded(queue, order, "n" + i, (int)TaskPriority.Normal);
                        queue.Start();
                        Check.True(await WaitForCount(order, 5), "all ran");
                        Check.Equal("n0,n1,n2,n3,n4", Joined(order), "FIFO within a band");
                    }
                }),

                TaskHandlerSuites.Case(Id, "WeightedFairByMetadata", "A WeightedFairQoSQueue keyed on task metadata runs every tenant's tasks", async ct =>
                {
                    int ran = 0;
                    WeightedFairQoSQueue<TaskDetails> wfq = new WeightedFairQoSQueue<TaskDetails>(
                        t => (string)t.Metadata["tenant"],
                        new[] { new WeightedFlow("gold", 3), new WeightedFlow("bronze", 1) });
                    using (TaskQueue queue = new TaskQueue(wfq, maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            string tenant = i % 2 == 0 ? "gold" : "bronze";
                            queue.AddTask(Guid.NewGuid(), "t" + i, new Dictionary<string, object> { { "tenant", tenant } }, async token =>
                            {
                                Interlocked.Increment(ref ran);
                                await Task.Delay(2, token);
                            });
                        }
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref ran) == 4), "all tenants served");
                    }
                }),

                TaskHandlerSuites.Case(Id, "UnclassifiedRejected", "A task the scheduler cannot classify is rejected with error.type unclassified", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = "sched-" + Guid.NewGuid().ToString("N");
                        WeightedFairQoSQueue<TaskDetails> wfq = new WeightedFairQoSQueue<TaskDetails>(
                            t => (string)t.Metadata["tenant"],
                            new[] { new WeightedFlow("gold", 1) },
                            unknownKeyPolicy: UnknownKeyPolicy.Reject);
                        using (TaskQueue queue = TaskQueue.Create(o => { o.Name = queueName; o.Scheduler = wfq; }))
                        {
                            Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "stranger", new Dictionary<string, object> { { "tenant", "nobody" } }, token => Task.CompletedTask), "unknown tenant");
                            Check.Equal(0, queue.QueuedCount, "nothing queued");
                            queue.AddTask(Guid.NewGuid(), "member", new Dictionary<string, object> { { "tenant", "gold" } }, token => Task.CompletedTask);
                            Check.Equal(1, queue.QueuedCount, "known tenant queued");
                            Check.Equal(1, capture.Count(TaskHandlerTelemetryNames.TasksRejected, queueName, TaskHandlerTelemetryNames.AttrErrorType, TaskHandlerTelemetryNames.ErrorUnclassified), "unclassified rejection recorded");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "RejectWhenFull", "A scheduler with MaxDepth and the Reject policy rejects adds when full and reports its capacity", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(2)))
                    {
                        Check.Equal(2, queue.MaxQueueSize, "MaxQueueSize reflects MaxDepth");
                        await queue.EnqueueAsync("a", token => Task.CompletedTask);
                        await queue.EnqueueAsync("b", token => Task.CompletedTask);
                        Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "c", null, token => Task.CompletedTask), "AddTask when full");
                        await Check.ThrowsAsync<InvalidOperationException>(() => queue.AddTaskAsync(Guid.NewGuid(), "d", null, token => Task.CompletedTask), "AddTaskAsync when full under Reject");
                        await Check.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync<int>("e", token => Task.FromResult(1)), "EnqueueAsync<T> when full");
                        Check.Equal(2, queue.QueuedCount, "only the admitted tasks are queued");
                    }
                }),

                TaskHandlerSuites.Case(Id, "BlockWaitsForSpace", "A scheduler with the Block policy makes AddTaskAsync wait for space, even while stopped", async ct =>
                {
                    int ran = 0;
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(1).WithOverflowPolicy(OverflowPolicy.Block), maxConcurrentTasks: 1))
                    {
                        queue.AddTask(Guid.NewGuid(), "first", null, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        Task<TaskDetails> second = queue.AddTaskAsync(Guid.NewGuid(), "second", null, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        await Task.Delay(150);
                        Check.False(second.IsCompleted, "waits while full and stopped");

                        queue.Start();
                        Task done = await Task.WhenAny(second, Task.Delay(5000));
                        Check.True(done == second, "add completes once space frees");
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref ran) == 2), "both run");
                    }
                }),

                TaskHandlerSuites.Case(Id, "DropOldestEvicts", "A scheduler with the DropOldest policy evicts the oldest waiting task, which completes as canceled", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = "sched-" + Guid.NewGuid().ToString("N");
                        int canceledEvents = 0;
                        TaskQueue queue = TaskQueue.Create(o =>
                        {
                            o.Name = queueName;
                            o.Scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(1).WithOverflowPolicy(OverflowPolicy.DropOldest);
                        });
                        queue.OnTaskCanceled += (s, d) => Interlocked.Increment(ref canceledEvents);

                        TaskHandle<int> oldest = await queue.EnqueueAsync<int>("oldest", token => Task.FromResult(1));
                        TaskHandle<int> newest = await queue.EnqueueAsync<int>("newest", token => Task.FromResult(2));

                        Check.True(oldest.Task.IsCanceled, "evicted handle canceled");
                        Check.Equal(1, Volatile.Read(ref canceledEvents), "OnTaskCanceled for the evicted task");
                        Check.Equal(1L, queue.GetStatistics().TotalCanceled, "TotalCanceled");
                        Check.Equal(1, queue.QueuedCount, "one task queued");
                        Check.Equal(1, capture.Count(TaskHandlerTelemetryNames.TasksCompleted, queueName, TaskHandlerTelemetryNames.AttrOutcome, TaskHandlerTelemetryNames.OutcomeDropped), "dropped outcome");
                        Check.True(capture.Measurements(TaskHandlerTelemetryNames.TasksCompleted, queueName)
                            .Where(m => m.Tag(TaskHandlerTelemetryNames.AttrOutcome) == TaskHandlerTelemetryNames.OutcomeDropped)
                            .All(m => m.Tag(TaskHandlerTelemetryNames.AttrErrorType) == TaskHandlerTelemetryNames.ErrorQueueFull), "eviction error.type is queue_full");

                        queue.Start();
                        Check.Equal(2, await newest.Task, "newest runs");
                        queue.Dispose();
                    }
                }),

                TaskHandlerSuites.Case(Id, "DropNewestRejects", "A scheduler with the DropNewest policy rejects the incoming task when full and keeps the queued one", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(1).WithOverflowPolicy(OverflowPolicy.DropNewest)))
                    {
                        TaskHandle<int> kept = await queue.EnqueueAsync<int>("kept", token => Task.FromResult(1));
                        Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "refused", null, token => Task.CompletedTask), "newest refused");
                        Check.Equal(1, queue.QueuedCount, "queued count");
                        queue.Start();
                        Check.Equal(1, await kept.Task, "kept task runs");
                    }
                }),

                TaskHandlerSuites.Case(Id, "StopRestartWithScheduler", "With a scheduler, Stop() retains tasks, tasks can be added while stopped, and priority applies on restart", async ct =>
                {
                    List<string> order = new List<string>();
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        await EnqueueRecorded(queue, order, "low", (int)TaskPriority.Low);
                        queue.Stop();
                        await EnqueueRecorded(queue, order, "urgent", (int)TaskPriority.Urgent);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 0), "blocker canceled");
                        Check.Equal(2, queue.QueuedCount, "both retained");

                        queue.Start();
                        Check.True(await WaitForCount(order, 2), "both ran after restart");
                        lock (order)
                        {
                            Check.True(order.Contains("urgent") && order.Contains("low"), $"both ran (order {Joined(order)})");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "DisposeDropsSchedulerTasks", "Dispose drops every task still in the scheduler and cancels their handles", async ct =>
                {
                    TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority));
                    TaskHandle<int> a = await queue.EnqueueAsync<int>("a", token => Task.FromResult(1), priority: 0);
                    TaskHandle<int> b = await queue.EnqueueAsync<int>("b", token => Task.FromResult(2), priority: 4);
                    queue.Dispose();
                    Check.True(a.Task.IsCanceled && b.Task.IsCanceled, "both handles canceled");
                    Check.Equal(2L, queue.GetStatistics().TotalCanceled, "TotalCanceled");
                    Check.Equal(0, queue.QueuedCount, "queue empty");
                    Check.Throws<ObjectDisposedException>(() => queue.AddTask(Guid.NewGuid(), "late", null, token => Task.CompletedTask), "add after dispose");
                }),

                TaskHandlerSuites.Case(Id, "DisposeReleasesBlockedAdd", "Dispose releases an AddTaskAsync waiting for space in a Block scheduler with ObjectDisposedException", async ct =>
                {
                    TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(1).WithOverflowPolicy(OverflowPolicy.Block));
                    queue.AddTask(Guid.NewGuid(), "first", null, token => Task.CompletedTask);
                    Task<TaskDetails> waiting = queue.AddTaskAsync(Guid.NewGuid(), "second", null, token => Task.CompletedTask);
                    await Task.Delay(100);
                    queue.Dispose();
                    Task done = await Task.WhenAny(waiting, Task.Delay(5000));
                    Check.True(done == waiting, "waiting add released");
                    await Check.ThrowsAsync<ObjectDisposedException>(() => waiting, "waiting add");
                }),

                TaskHandlerSuites.Case(Id, "SchedulerValidation", "Scheduler arguments are validated and MaxQueueSize cannot be combined with a scheduler", async ct =>
                {
                    Check.Throws<ArgumentNullException>(() => new TaskQueue((IQoSQueue<TaskDetails>)null!), "null scheduler");
                    Check.Throws<ArgumentNullException>(() => new TaskQueue((TaskQueueOptions)null!), "null options");

                    PriorityQoSQueue<TaskDetails> used = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority);
                    used.Enqueue(new TaskDetails());
                    Check.Throws<ArgumentException>(() => new TaskQueue(used), "non-empty scheduler");

                    Check.Throws<ArgumentException>(() => TaskQueue.Create(o =>
                    {
                        o.Scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority);
                        o.MaxQueueSize = 10;
                    }), "Scheduler plus MaxQueueSize");

                    PriorityQoSQueue<TaskDetails> scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority);
                    using (TaskQueue queue = new TaskQueue(scheduler))
                    {
                        Check.True(ReferenceEquals(scheduler, queue.Scheduler), "Scheduler property returns the scheduler");
                        Check.Equal(-1, queue.MaxQueueSize, "unbounded scheduler reports -1");
                        Check.Throws<InvalidOperationException>(() => queue.MaxQueueSize = 5, "MaxQueueSize setter with a scheduler");
                    }

                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "SchedulerCapacityGauge", "The capacity gauge reports the scheduler's MaxDepth", async ct =>
                {
                    using (TelemetryCapture capture = new TelemetryCapture())
                    {
                        string queueName = "sched-" + Guid.NewGuid().ToString("N");
                        using (TaskQueue queue = TaskQueue.Create(o =>
                        {
                            o.Name = queueName;
                            o.Scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithMaxDepth(7);
                        }))
                        {
                            await queue.EnqueueAsync("x", token => Task.CompletedTask);
                            capture.RecordObservables();
                            Check.Equal(7.0, capture.Latest(TaskHandlerTelemetryNames.QueueCapacity, queueName), "capacity");
                            Check.Equal(1.0, capture.Latest(TaskHandlerTelemetryNames.QueueDepth, queueName), "depth");
                        }
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Scheduling (FIFO and QoSKit)", cases);
        }

        private static async Task EnqueueRecorded(TaskQueue queue, List<string> order, string name, int priority, int delayMs = 2)
        {
            await queue.EnqueueAsync(name, async token =>
            {
                lock (order) { order.Add(name); }
                await Task.Delay(delayMs, token);
            }, priority: priority);
        }

        private static Task<bool> WaitForCount(List<string> order, int count)
        {
            return Check.WaitUntilAsync(() => { lock (order) { return order.Count == count; } });
        }

        private static string Joined(List<string> order)
        {
            lock (order) { return String.Join(",", order); }
        }
    }
}
