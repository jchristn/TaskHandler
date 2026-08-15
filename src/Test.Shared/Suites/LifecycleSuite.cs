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
                })
            };

            return new TestSuiteDescriptor(Id, "Lifecycle", cases);
        }
    }
}
