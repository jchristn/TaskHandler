namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for IProgress-based progress reporting during task execution.
    /// </summary>
    public static class ProgressSuite
    {
        private const string Id = "Progress";

        /// <summary>
        /// Build the progress reporting test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "ProgressWithResult", "Progress reported for a task returning a result", async ct =>
                {
                    List<TaskProgress> updates = new List<TaskProgress>();
                    Progress<TaskProgress> progress = new Progress<TaskProgress>(p => { lock (updates) { updates.Add(p); } });
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<string> handle = await queue.EnqueueAsync(
                            "Prog",
                            async (token, prog) =>
                            {
                                for (int i = 0; i <= 10; i++)
                                {
                                    prog?.Report(new TaskProgress(i, 10, $"Step {i}"));
                                    await Task.Delay(20, token);
                                }
                                return "done";
                            },
                            progress);

                        Check.Equal("done", await handle.Task, "result");
                        int count;
                        lock (updates) { count = updates.Count; }
                        Check.True(count >= 10, $"expected >= 10 progress updates (got {count})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ProgressWithoutResult", "Progress reported for a task without a result", async ct =>
                {
                    List<TaskProgress> updates = new List<TaskProgress>();
                    Progress<TaskProgress> progress = new Progress<TaskProgress>(p => { lock (updates) { updates.Add(p); } });
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.EnqueueAsync(
                            "ProgNoResult",
                            async (token, prog) =>
                            {
                                for (int i = 0; i <= 5; i++)
                                {
                                    prog?.Report(new TaskProgress(i, 5, $"P{i}"));
                                    await Task.Delay(20, token);
                                }
                            },
                            progress);

                        Check.True(await Check.WaitUntilAsync(() => { lock (updates) { return updates.Count >= 5; } }), "expected >= 5 progress updates");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ProgressReachesHundred", "Progress reaches 100 percent", async ct =>
                {
                    List<double> percents = new List<double>();
                    Progress<TaskProgress> progress = new Progress<TaskProgress>(p => { lock (percents) { percents.Add(p.PercentComplete); } });
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<int> handle = await queue.EnqueueAsync(
                            "Pct",
                            async (token, prog) =>
                            {
                                for (int i = 0; i <= 100; i += 10)
                                {
                                    prog?.Report(new TaskProgress(i, 100));
                                    await Task.Delay(10, token);
                                }
                                return 100;
                            },
                            progress);

                        Check.Equal(100, await handle.Task, "result");
                        bool reached;
                        lock (percents) { reached = percents.Any(p => p >= 99.0); }
                        Check.True(reached, "progress should reach 100%");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ProgressGenericNullNameThrows", "Progress EnqueueAsync<T> with null name throws", async ct =>
                {
                    Progress<TaskProgress> progress = new Progress<TaskProgress>(p => { });
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await Check.ThrowsAsync<ArgumentNullException>(async () =>
                            await queue.EnqueueAsync<int>(null!, async (token, prog) => { await Task.Delay(1, token); return 1; }, progress),
                            "null name");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ProgressNonGenericNullFuncThrows", "Progress EnqueueAsync (no result) with null func throws", async ct =>
                {
                    Progress<TaskProgress> progress = new Progress<TaskProgress>(p => { });
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await Check.ThrowsAsync<ArgumentNullException>(async () =>
                            await queue.EnqueueAsync("NullProgFunc", (Func<CancellationToken, IProgress<TaskProgress>, Task>)null!, progress),
                            "null func");
                        queue.Stop();
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Progress Reporting", cases);
        }
    }
}
