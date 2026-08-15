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
                })
            };

            return new TestSuiteDescriptor(Id, "TaskHandle<T> Results", cases);
        }
    }
}
