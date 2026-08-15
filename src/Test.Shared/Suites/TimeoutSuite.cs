namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the static TaskRunWithTimeout helper.
    /// </summary>
    public static class TimeoutSuite
    {
        private const string Id = "Timeout";

        /// <summary>
        /// Build the TaskRunWithTimeout test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "Success", "Task completing within the window returns its result", async ct =>
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    Func<CancellationToken, Task<string>> task = async token =>
                    {
                        await Task.Delay(50, token);
                        return "Success";
                    };
                    string result = await TaskRunWithTimeout.Go(task(cts.Token), 1000, cts);
                    Check.Equal("Success", result, "result");
                }),

                TaskHandlerSuites.Case(Id, "Exceeded", "Task exceeding the window throws TimeoutException", async ct =>
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    Func<CancellationToken, Task<string>> task = async token =>
                    {
                        await Task.Delay(2000, token);
                        return "unreached";
                    };
                    await Check.ThrowsAsync<TimeoutException>(async () =>
                        await TaskRunWithTimeout.Go(task(cts.Token), 300, cts), "Go with short timeout");
                }),

                TaskHandlerSuites.Case(Id, "CancelsTokenSourceOnTimeout", "Timeout cancels the supplied token source", async ct =>
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    Func<CancellationToken, Task<int>> task = async token =>
                    {
                        await Task.Delay(2000, token);
                        return 1;
                    };
                    try
                    {
                        await TaskRunWithTimeout.Go(task(cts.Token), 300, cts);
                    }
                    catch (TimeoutException)
                    {
                        // expected
                    }
                    Check.True(cts.IsCancellationRequested, "token source should be canceled after timeout");
                }),

                TaskHandlerSuites.Case(Id, "InvalidTimeoutThrows", "Timeout below 1 throws ArgumentOutOfRangeException", async ct =>
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    Task<int> completed = Task.FromResult(5);
                    await Check.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                        await TaskRunWithTimeout.Go(completed, 0, cts), "Go with timeout 0");
                }),

                TaskHandlerSuites.Case(Id, "NullTokenSourceThrows", "Null token source throws ArgumentNullException", async ct =>
                {
                    Task<int> completed = Task.FromResult(5);
                    await Check.ThrowsAsync<ArgumentNullException>(async () =>
                        await TaskRunWithTimeout.Go(completed, 1000, null!), "Go with null token source");
                }),

                TaskHandlerSuites.Case(Id, "LoggerInvoked", "Logger callback is invoked when set", async ct =>
                {
                    List<string> logs = new List<string>();
                    Action<string>? previous = TaskRunWithTimeout.Logger;
                    try
                    {
                        TaskRunWithTimeout.Logger = msg => { lock (logs) { logs.Add(msg); } };
                        CancellationTokenSource cts = new CancellationTokenSource();
                        Func<CancellationToken, Task<string>> task = async token =>
                        {
                            await Task.Delay(30, token);
                            return "ok";
                        };
                        await TaskRunWithTimeout.Go(task(cts.Token), 1000, cts);
                        int count;
                        lock (logs) { count = logs.Count; }
                        Check.True(count > 0, "logger should have been invoked");
                    }
                    finally
                    {
                        TaskRunWithTimeout.Logger = previous;
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "TaskRunWithTimeout", cases);
        }
    }
}
