namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for individual and bulk cancellation behavior.
    /// </summary>
    public static class CancellationSuite
    {
        private const string Id = "Cancellation";

        /// <summary>
        /// Build the cancellation test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "StopByGuidCancels", "Stop(guid) cancels an individual task", async ct =>
                {
                    bool canceled = false;
                    Guid guid = Guid.NewGuid();
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.AddTask(guid, "Cancelable", new Dictionary<string, object>(), async token =>
                        {
                            try { await Task.Delay(5000, token); }
                            catch (OperationCanceledException) { canceled = true; throw; }
                        });
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task should start");
                        queue.Stop(guid);
                        Check.True(await Check.WaitUntilAsync(() => canceled), "task should be canceled");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "StopAllCancels", "Stop() cancels all running tasks", async ct =>
                {
                    int canceled = 0;
                    int started = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskStarted += (s, d) => Interlocked.Increment(ref started);
                        for (int i = 0; i < 5; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"C{i}", new Dictionary<string, object>(), async token =>
                            {
                                try { await Task.Delay(5000, token); }
                                catch (OperationCanceledException) { Interlocked.Increment(ref canceled); }
                            });
                        }
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref started) == 5), "all should start");
                        queue.Stop();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref canceled) == 5), $"all should cancel (got {canceled})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "StopUnknownGuidGraceful", "Stop(unknown guid) does not throw", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        queue.Stop(Guid.NewGuid());
                        await Task.Delay(30);
                        queue.Stop();
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "DisposeCancelsRunning", "Dispose cancels running tasks", async ct =>
                {
                    bool canceled = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.AddTask(Guid.NewGuid(), "Long", new Dictionary<string, object>(), async token =>
                        {
                            try { await Task.Delay(5000, token); }
                            catch (OperationCanceledException) { canceled = true; throw; }
                        });
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task should start");
                        queue.Dispose();
                        Check.True(await Check.WaitUntilAsync(() => canceled), "task should be canceled on dispose");
                    }
                }),

                TaskHandlerSuites.Case(Id, "DisposeAsyncCancelsRunning", "DisposeAsync cancels running tasks", async ct =>
                {
                    bool canceled = false;
                    TaskQueue queue = new TaskQueue();
                    queue.Start();
                    await queue.EnqueueAsync("Long", async token =>
                    {
                        try { await Task.Delay(5000, token); }
                        catch (OperationCanceledException) { canceled = true; throw; }
                    });
                    Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task should start");
                    await queue.DisposeAsync();
                    Check.True(await Check.WaitUntilAsync(() => canceled), "task should be canceled on DisposeAsync");
                }),

                TaskHandlerSuites.Case(Id, "HandleCancellationThrows", "Canceling a result task faults its handle with OperationCanceledException", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<string> handle = await queue.EnqueueAsync("Cancelable", async token =>
                        {
                            await Task.Delay(5000, token);
                            return "unreached";
                        });
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task should start");
                        queue.Stop(handle.Id);
                        await Check.ThrowsAsync<OperationCanceledException>(async () => await handle.Task, "awaiting canceled handle");
                        queue.Stop();
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Cancellation", cases);
        }
    }
}
