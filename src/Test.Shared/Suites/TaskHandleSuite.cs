namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for TaskHandle&lt;T&gt; result, exception, and identity behavior.
    /// </summary>
    public static class TaskHandleSuite
    {
        private const string Id = "TaskHandle";

        /// <summary>
        /// Build the task handle test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "HandleReturnsResult", "Handle completes with the task result", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<int> handle = await queue.EnqueueAsync("R", async token =>
                        {
                            await Task.Delay(30, token);
                            return 42;
                        });
                        Check.Equal(42, await handle.Task, "result");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleHasIdAndName", "Handle exposes id and name", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<string> handle = await queue.EnqueueAsync("MyName", async token =>
                        {
                            await Task.Delay(20, token);
                            return "ok";
                        });
                        Check.True(handle.Id != Guid.Empty, "handle id non-empty");
                        Check.Equal("MyName", handle.Name, "handle name");
                        await handle.Task;
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleExceptionPropagates", "Handle propagates the task exception with type and message", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<string> handle = await queue.EnqueueAsync<string>("Faulting", async token =>
                        {
                            await Task.Delay(20, token);
                            throw new InvalidOperationException("expected failure");
                        });

                        bool threw = false;
                        try
                        {
                            await handle.Task;
                        }
                        catch (InvalidOperationException ex)
                        {
                            threw = true;
                            Check.Equal("expected failure", ex.Message, "exception message");
                        }
                        Check.True(threw, "handle should propagate exception");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "GenericNullNameThrows", "EnqueueAsync<T> with null name throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await Check.ThrowsAsync<ArgumentNullException>(async () =>
                            await queue.EnqueueAsync<int>(null!, async token => { await Task.Delay(1, token); return 1; }),
                            "EnqueueAsync<int>(null, ...)");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "GenericNullFuncThrows", "EnqueueAsync<T> with null func throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await Check.ThrowsAsync<ArgumentNullException>(async () =>
                            await queue.EnqueueAsync<int>("NullFunc", null!),
                            "EnqueueAsync<int>(name, null)");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleWithinTimeoutReturnsResult", "EnqueueAsync<T> with a generous timeout returns the result", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<int> handle = await queue.EnqueueAsync<int>("QuickWithTimeout", async token =>
                        {
                            await Task.Delay(30, token);
                            return 7;
                        }, priority: 0, timeout: TimeSpan.FromSeconds(5));
                        Check.Equal(7, await handle.Task, "result within timeout");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleTimesOutWithTimeoutException", "EnqueueAsync<T> exceeding its timeout completes the handle with TimeoutException", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<int> handle = await queue.EnqueueAsync<int>("SlowWithTimeout", async token =>
                        {
                            await Task.Delay(5000, token);
                            return 1;
                        }, priority: 0, timeout: TimeSpan.FromMilliseconds(150));

                        await Check.ThrowsAsync<TimeoutException>(async () => await handle.Task, "awaiting a timed-out handle");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleExternalCancellationStillCancels", "External Stop() cancels a handle task that observes its token", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        bool started = false;
                        queue.OnTaskStarted = (s, d) => started = true;
                        queue.Start();
                        TaskHandle<int> handle = await queue.EnqueueAsync<int>("CancelMe", async token =>
                        {
                            await Task.Delay(5000, token);
                            return 1;
                        });

                        Check.True(await Check.WaitUntilAsync(() => started), "task should start");
                        queue.Stop();

                        bool canceled = false;
                        try
                        {
                            await handle.Task;
                        }
                        catch (OperationCanceledException)
                        {
                            canceled = true;
                        }
                        Check.True(canceled, "handle should surface cancellation");
                    }
                }),

                TaskHandlerSuites.Case(Id, "HandleCompletesWhenDroppedBeforeStart", "A result task that never starts because the queue stopped completes its handle as canceled", async ct =>
                {
                    TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1);
                    queue.Start();

                    // The holder ignores cancellation so the only slot stays held after Stop().
                    await queue.EnqueueAsync("holder", async token => await Task.Delay(300));
                    Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "holder should start");
                    TaskHandle<int> handle = await queue.EnqueueAsync<int>("never-runs", token => Task.FromResult(1));
                    Check.True(await Check.WaitUntilAsync(() => queue.QueuedCount == 0), "result task dequeued and waiting for a slot");

                    queue.Stop();

                    Task completed = await Task.WhenAny(handle.Task, Task.Delay(5000));
                    Check.True(completed == handle.Task, "handle should complete instead of hanging");
                    Check.True(handle.Task.IsCanceled, "handle should be canceled");

                    await Task.Delay(400);
                    queue.Dispose();
                })
            };

            return new TestSuiteDescriptor(Id, "TaskHandle<T> Results", cases);
        }
    }
}
