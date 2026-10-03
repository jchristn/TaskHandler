namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for start/stop/dispose lifecycle, including guard conditions and restart cycles.
    /// </summary>
    public static class LifecycleSuite
    {
        private const string Id = "Lifecycle";

        /// <summary>
        /// Build the lifecycle test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "StartStopBasic", "Start then Stop processes an enqueued task", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        queue.AddTask(Guid.NewGuid(), "SS", new Dictionary<string, object>(), async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(20, token);
                        });
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "task should run after start");
                        queue.Stop();
                    }
                    Check.Equal(1, count, "execution count");
                }),

                TaskHandlerSuites.Case(Id, "StartAsyncStopAsync", "StartAsync/StopAsync run a task to completion", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        await queue.StartAsync();
                        await queue.EnqueueAsync("L", async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(50, token);
                        });
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "task should run");
                        await queue.StopAsync(waitForCompletion: true);
                    }
                    Check.Equal(1, count, "execution count");
                }),

                TaskHandlerSuites.Case(Id, "StartTwiceThrows", "Starting an already-started queue throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        Check.Throws<InvalidOperationException>(() => queue.Start(), "second Start()");
                        queue.Stop();
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "StopWhenNotStartedNoThrow", "Stopping a never-started queue does not throw", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Stop();
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "StopAfterDisposeThrows", "Stop after dispose throws ObjectDisposedException", async ct =>
                {
                    TaskQueue queue = new TaskQueue();
                    queue.Dispose();
                    Check.Throws<ObjectDisposedException>(() => queue.Stop(), "Stop after dispose");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "StartAfterDisposeThrows", "Start after dispose throws ObjectDisposedException", async ct =>
                {
                    TaskQueue queue = new TaskQueue();
                    queue.Dispose();
                    Check.Throws<ObjectDisposedException>(() => queue.Start(), "Start after dispose");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "DisposeIdempotent", "Dispose can be called multiple times safely", async ct =>
                {
                    TaskQueue queue = new TaskQueue();
                    queue.Dispose();
                    queue.Dispose();
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MultipleStartStopCycles", "Multiple start/stop cycles each run a task", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        for (int cycle = 0; cycle < 3; cycle++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"Cycle{cycle}", new Dictionary<string, object>(), async token =>
                            {
                                Interlocked.Increment(ref count);
                                await Task.Delay(20, token);
                            });
                            queue.Start();
                            int expected = cycle + 1;
                            Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == expected), $"cycle {cycle} task should run");
                            queue.Stop();
                            await Task.Delay(50);
                        }
                    }
                    Check.Equal(3, count, "total executions across cycles");
                }),

                TaskHandlerSuites.Case(Id, "WaitForCompletionAsync", "WaitForCompletionAsync waits for all tasks", async ct =>
                {
                    const int total = 20;
                    using (TaskQueue queue = new TaskQueue(4))
                    {
                        queue.Start();
                        for (int i = 0; i < total; i++)
                        {
                            await queue.EnqueueAsync($"W{i}", async token => await Task.Delay(25, token));
                        }
                        await queue.WaitForCompletionAsync();
                        Check.Equal(0, queue.QueuedCount, "queue depth after completion");
                        Check.Equal(0, queue.RunningCount, "running count after completion");
                        TaskQueueStatistics stats = queue.GetStatistics();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalCompleted == total), $"all completed (got {stats.TotalCompleted})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "StopAsyncWaitsForRunner", "StopAsync(waitForCompletion) awaits the runner", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.EnqueueAsync("R", async token => await Task.Delay(50, token));
                        await queue.StopAsync(waitForCompletion: true);
                        Check.False(queue.IsRunning, "runner stopped");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "IsRunningReflectsState", "IsRunning is false before Start, true while started, and false after Stop and Dispose", async ct =>
                {
                    TaskQueue queue = new TaskQueue();
                    Check.False(queue.IsRunning, "before start");
                    queue.Start();
                    Check.True(await Check.WaitUntilAsync(() => queue.IsRunning), "while started");
                    await Task.Delay(100);
                    Check.True(queue.IsRunning, "still running while idle");
                    queue.Stop();
                    Check.False(queue.IsRunning, "after stop");
                    queue.Start();
                    Check.True(await Check.WaitUntilAsync(() => queue.IsRunning), "after restart");
                    queue.Dispose();
                    Check.False(queue.IsRunning, "after dispose");
                }),

                TaskHandlerSuites.Case(Id, "StopRetainsQueuedTasks", "Tasks not yet started when Stop() is called are retained and run after the next Start()", async ct =>
                {
                    int ran = 0;
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        for (int i = 0; i < 3; i++)
                        {
                            await queue.EnqueueAsync("R" + i, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        }

                        queue.Stop();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 0), "blocker canceled");
                        await Task.Delay(200);
                        Check.Equal(0, Volatile.Read(ref ran), "nothing runs while stopped");
                        Check.Equal(3, queue.QueuedCount, "all three retained");

                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref ran) == 3), "retained tasks run after restart");
                        await queue.WaitForCompletionAsync();
                        Check.Equal(0, queue.QueuedCount, "queue drained");
                    }
                }),

                TaskHandlerSuites.Case(Id, "AddWhileStoppedAndAfterRestart", "Tasks can be added while stopped and immediately after a restart that still has retained tasks", async ct =>
                {
                    int ran = 0;
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        await queue.EnqueueAsync("retained", async token => { Interlocked.Increment(ref ran); await Task.Delay(200, token); });
                        queue.Stop();

                        queue.AddTask(Guid.NewGuid(), "added-stopped", new Dictionary<string, object>(), async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        await queue.AddTaskAsync(Guid.NewGuid(), "added-stopped-async", null, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        TaskHandle<int> handle = await queue.EnqueueAsync<int>("added-stopped-result", token => Task.FromResult(7));

                        queue.Start();
                        queue.AddTask(Guid.NewGuid(), "added-after-restart", new Dictionary<string, object>(), async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });

                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref ran) == 4), $"all plain tasks run (got {ran})");
                        Check.Equal(7, await handle.Task, "result task added while stopped");
                    }
                }),

                TaskHandlerSuites.Case(Id, "RestartPreservesOrder", "Tasks retained across Stop() and Start() start in the order they were added", async ct =>
                {
                    List<int> order = new List<int>();
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await Task.Delay(10000, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        for (int i = 0; i < 5; i++)
                        {
                            int n = i;
                            await queue.EnqueueAsync("O" + n, async token => { lock (order) { order.Add(n); } await Task.Delay(5, token); });
                        }

                        queue.Stop();
                        await Task.Delay(100);
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => { lock (order) { return order.Count == 5; } }), "all ran");
                        lock (order)
                        {
                            Check.Equal("0,1,2,3,4", String.Join(",", order), "start order");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "BoundedAddAsyncWaitsWhileStopped", "AddTaskAsync on a full bounded queue waits while stopped and completes once a restart makes room", async ct =>
                {
                    int ran = 0;
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1, maxQueueSize: 1))
                    {
                        queue.AddTask(Guid.NewGuid(), "first", null, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        Task<TaskDetails> second = queue.AddTaskAsync(Guid.NewGuid(), "second", null, async token => { Interlocked.Increment(ref ran); await Task.Delay(5, token); });
                        await Task.Delay(150);
                        Check.False(second.IsCompleted, "waits for space while stopped");
                        Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "full", null, token => Task.CompletedTask), "AddTask rejects when full");

                        queue.Start();
                        Task done = await Task.WhenAny(second, Task.Delay(5000));
                        Check.True(done == second, "add completes after restart");
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref ran) == 2), "both run");
                    }
                }),

                TaskHandlerSuites.Case(Id, "AddAfterDisposeThrows", "Adding to a disposed queue throws ObjectDisposedException from every add method", async ct =>
                {
                    TaskQueue queue = new TaskQueue();
                    queue.Dispose();
                    Check.Throws<ObjectDisposedException>(() => queue.AddTask(Guid.NewGuid(), "x", null, token => Task.CompletedTask), "AddTask");
                    await Check.ThrowsAsync<ObjectDisposedException>(() => queue.AddTaskAsync(Guid.NewGuid(), "x", null, token => Task.CompletedTask), "AddTaskAsync");
                    await Check.ThrowsAsync<ObjectDisposedException>(() => queue.EnqueueAsync("x", token => Task.CompletedTask), "EnqueueAsync");
                    await Check.ThrowsAsync<ObjectDisposedException>(() => queue.EnqueueAsync<int>("x", token => Task.FromResult(1)), "EnqueueAsync<T>");
                }),

                TaskHandlerSuites.Case(Id, "DisposeWhileAddAsyncWaitsThrows", "An AddTaskAsync waiting for space on a full bounded queue throws ObjectDisposedException when the queue is disposed", async ct =>
                {
                    TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1, maxQueueSize: 1);
                    queue.AddTask(Guid.NewGuid(), "first", null, token => Task.CompletedTask);
                    Task<TaskDetails> waiting = queue.AddTaskAsync(Guid.NewGuid(), "second", null, token => Task.CompletedTask);
                    await Task.Delay(100);
                    queue.Dispose();
                    await Check.ThrowsAsync<ObjectDisposedException>(() => waiting, "waiting add");
                }),

                TaskHandlerSuites.Case(Id, "DisposeDropsUnstartedTasks", "Dispose completes every unstarted task as canceled: handle canceled, OnTaskCanceled fired, counted in TotalCanceled", async ct =>
                {
                    int canceledEvents = 0;
                    bool ran = false;
                    TaskQueue queue = new TaskQueue();
                    queue.OnTaskCanceled += (s, d) => Interlocked.Increment(ref canceledEvents);
                    await queue.EnqueueAsync("plain", async token => { ran = true; await Task.Delay(5, token); });
                    TaskHandle<int> handle = await queue.EnqueueAsync<int>("result", token => { ran = true; return Task.FromResult(1); });
                    queue.Dispose();

                    Check.True(handle.Task.IsCanceled, "handle canceled");
                    Check.Equal(2, Volatile.Read(ref canceledEvents), "OnTaskCanceled once per dropped task");
                    TaskQueueStatistics stats = queue.GetStatistics();
                    Check.Equal(2L, stats.TotalCanceled, "TotalCanceled");
                    Check.Equal(0, stats.CurrentQueueDepth, "queue depth");
                    Check.False(ran, "dropped tasks never run");
                }),

                TaskHandlerSuites.Case(Id, "StopAsyncWaitsForRunningTasks", "StopAsync(waitForCompletion: true) returns only after canceled tasks have finished", async ct =>
                {
                    bool cleanedUp = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.EnqueueAsync("slow-cleanup", async token =>
                        {
                            try { await Task.Delay(10000, token); }
                            catch (OperationCanceledException)
                            {
                                await Task.Delay(200);
                                cleanedUp = true;
                                throw;
                            }
                        });
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task running");
                        await queue.StopAsync(waitForCompletion: true);
                        Check.True(cleanedUp, "cleanup finished before StopAsync returned");
                        Check.Equal(0, queue.RunningCount, "nothing running");
                    }
                }),

                TaskHandlerSuites.Case(Id, "DisposeAsyncIdempotentAndDrops", "DisposeAsync waits for running tasks, cancels unstarted handles, and can be called repeatedly", async ct =>
                {
                    TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1);
                    queue.Start();
                    await queue.EnqueueAsync("running", async token => await Task.Delay(10000, token));
                    Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "running");
                    TaskHandle<int> handle = await queue.EnqueueAsync<int>("unstarted", token => Task.FromResult(1));

                    await queue.DisposeAsync();
                    Check.Equal(0, queue.RunningCount, "running task finished");
                    Check.True(handle.Task.IsCanceled, "unstarted handle canceled");
                    await queue.DisposeAsync();
                    queue.Dispose();
                })
            };

            return new TestSuiteDescriptor(Id, "Lifecycle", cases);
        }
    }
}
