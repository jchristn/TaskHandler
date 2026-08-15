namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests that the queue honors its concurrency limits.
    /// </summary>
    public static class ConcurrencySuite
    {
        private const string Id = "Concurrency";

        /// <summary>
        /// Build the concurrency test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "LimitNeverExceeded", "Concurrency limit of 2 is never exceeded", async ct =>
                {
                    int max = 0;
                    int current = 0;
                    object gate = new object();
                    using (TaskQueue queue = new TaskQueue(2))
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"T{i}", new Dictionary<string, object>(), async token =>
                            {
                                lock (gate)
                                {
                                    current++;
                                    if (current > max) max = current;
                                }
                                await Task.Delay(100, token);
                                lock (gate) { current--; }
                            });
                        }
                        queue.Start();
                        await queue.WaitForCompletionAsync();
                        queue.Stop();
                    }
                    Check.InRange(max, 1, 2, "max observed concurrency");
                }),

                TaskHandlerSuites.Case(Id, "OneSerializes", "Concurrency limit of 1 serializes execution", async ct =>
                {
                    int max = 0;
                    int current = 0;
                    object gate = new object();
                    using (TaskQueue queue = new TaskQueue(1))
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"S{i}", new Dictionary<string, object>(), async token =>
                            {
                                lock (gate)
                                {
                                    current++;
                                    if (current > max) max = current;
                                }
                                await Task.Delay(40, token);
                                lock (gate) { current--; }
                            });
                        }
                        queue.Start();
                        await queue.WaitForCompletionAsync();
                        queue.Stop();
                    }
                    Check.Equal(1, max, "max observed concurrency with limit 1");
                }),

                TaskHandlerSuites.Case(Id, "RunningCountReflectsActive", "RunningCount reflects active tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(3))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"A{i}", new Dictionary<string, object>(), async token => await Task.Delay(400, token));
                        }
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 3), $"three tasks should be running (got {queue.RunningCount})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "QueuedCountReflectsBacklog", "QueuedCount reflects the pending backlog", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(1))
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"Q{i}", new Dictionary<string, object>(), async token => await Task.Delay(200, token));
                        }
                        // Not started: all five remain queued.
                        Check.Equal(5, queue.QueuedCount, "queued count before start");
                        queue.Stop();
                    }
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "Concurrency Control", cases);
        }
    }
}
