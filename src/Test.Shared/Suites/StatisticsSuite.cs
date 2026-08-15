namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for TaskQueue statistics and metrics tracking.
    /// </summary>
    public static class StatisticsSuite
    {
        private const string Id = "Statistics";

        /// <summary>
        /// Build the statistics test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "TotalEnqueued", "TotalEnqueued counts every enqueue", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        for (int i = 0; i < 10; i++)
                        {
                            await queue.EnqueueAsync($"E{i}", async token => await Task.Delay(20, token));
                        }
                        Check.Equal(10L, queue.GetStatistics().TotalEnqueued, "TotalEnqueued");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TotalCompleted", "TotalCompleted counts successful tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        for (int i = 0; i < 10; i++)
                        {
                            await queue.EnqueueAsync($"C{i}", async token => await Task.Delay(20, token));
                        }
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalCompleted == 10), $"TotalCompleted (got {queue.GetStatistics().TotalCompleted})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TotalFailed", "TotalFailed counts faulted tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        for (int i = 0; i < 5; i++)
                        {
                            await queue.EnqueueAsync($"X{i}", async token =>
                            {
                                await Task.Delay(20, token);
                                throw new InvalidOperationException("fail");
                            });
                        }
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalFailed == 5), $"TotalFailed (got {queue.GetStatistics().TotalFailed})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TotalCanceled", "TotalCanceled counts canceled tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        List<Guid> ids = new List<Guid>();
                        for (int i = 0; i < 5; i++)
                        {
                            ids.Add(await queue.EnqueueAsync($"K{i}", async token => await Task.Delay(5000, token)));
                        }
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 5), "all should be running");
                        foreach (Guid id in ids) queue.Stop(id);
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalCanceled == 5), $"TotalCanceled (got {queue.GetStatistics().TotalCanceled})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "AverageExecutionTime", "AverageExecutionTime is within a sane range", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        for (int i = 0; i < 10; i++)
                        {
                            await queue.EnqueueAsync($"A{i}", async token => await Task.Delay(100, token));
                        }
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalCompleted == 10), "all completed");
                        double ms = queue.GetStatistics().AverageExecutionTime.TotalMilliseconds;
                        Check.InRange(ms, 80, 400, "AverageExecutionTime ms");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "AverageWaitTimeNonNegative", "AverageWaitTime is non-negative", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(1))
                    {
                        queue.Start();
                        for (int i = 0; i < 5; i++)
                        {
                            await queue.EnqueueAsync($"WT{i}", async token => await Task.Delay(30, token));
                        }
                        await queue.WaitForCompletionAsync();
                        double ms = queue.GetStatistics().AverageWaitTime.TotalMilliseconds;
                        Check.True(ms >= 0, "AverageWaitTime should be non-negative");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "QueueDepthAndRunning", "CurrentQueueDepth and CurrentRunningCount reflect state", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(2))
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"D{i}", new Dictionary<string, object>(), async token => await Task.Delay(150, token));
                        }
                        Check.Equal(10, queue.GetStatistics().CurrentQueueDepth, "queue depth before start");
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().CurrentRunningCount == 2), "running count reaches 2");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "TimestampsSet", "LastTaskStarted and LastTaskCompleted are set after a run", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.EnqueueAsync("TS", async token => await Task.Delay(30, token));
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().LastTaskCompleted != null), "LastTaskCompleted set");
                        TaskQueueStatistics stats = queue.GetStatistics();
                        Check.NotNull(stats.LastTaskStarted, "LastTaskStarted");
                        Check.NotNull(stats.LastTaskCompleted, "LastTaskCompleted");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ToStringContainsFields", "Statistics ToString contains key fields", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.EnqueueAsync("TS1", async token => await Task.Delay(20, token));
                        await queue.EnqueueAsync("TS2", async token => await Task.Delay(20, token));
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalEnqueued == 2), "both enqueued");
                        string s = queue.GetStatistics().ToString();
                        Check.True(s.Contains("Enqueued="), "ToString contains Enqueued field");
                        Check.True(s.Contains("Completed="), "ToString contains Completed field");
                        Check.True(s.Contains("AvgExecTime="), "ToString contains AvgExecTime field");
                        queue.Stop();
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Statistics", cases);
        }
    }
}
