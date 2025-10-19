namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;

    public static class Program
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static List<TestResult> _Results = new List<TestResult>();
        private static readonly object _ConsoleLock = new object();

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=======================================================================");
            Console.WriteLine("TaskHandler Automated Test Suite");
            Console.WriteLine("=======================================================================");
            Console.WriteLine();

            // Run all tests
            await RunTest("Test 1: Basic Task Enqueue and Execution", Test_BasicEnqueueAndExecution);
            await RunTest("Test 2: Concurrency Limits", Test_ConcurrencyLimits);
            await RunTest("Test 3: Cancel Individual Task", Test_CancelIndividualTask);
            await RunTest("Test 4: Cancel All Tasks", Test_CancelAllTasks);
            await RunTest("Test 5: Disposal Cleanup", Test_DisposalCleanup);
            await RunTest("Test 6: Cannot Start Twice", Test_CannotStartTwice);
            await RunTest("Test 7: Cannot Stop When Not Started", Test_CannotStopWhenNotStarted);
            await RunTest("Test 8: Cannot Use After Disposal", Test_CannotUseAfterDisposal);
            await RunTest("Test 9: Event Handler Exceptions Are Caught", Test_EventHandlerExceptionsCaught);
            await RunTest("Test 10: Task Completion Events", Test_TaskCompletionEvents);
            await RunTest("Test 11: Task Faulted Events", Test_TaskFaultedEvents);
            await RunTest("Test 12: Task Canceled Events", Test_TaskCanceledEvents);
            await RunTest("Test 13: Multiple Start Stop Cycles", Test_MultipleStartStopCycles);
            await RunTest("Test 14: Race Condition Safety", Test_RaceConditionSafety);
            await RunTest("Test 15: TaskRunWithTimeout Success", Test_TaskRunWithTimeoutSuccess);
            await RunTest("Test 16: TaskRunWithTimeout Timeout", Test_TaskRunWithTimeoutTimeout);
            await RunTest("Test 17: High Throughput Stress Test", Test_HighThroughputStressTest);
            await RunTest("Test 18: Zero Concurrency Limit Validation", Test_ZeroConcurrencyValidation);
            await RunTest("Test 19: Task Metadata Preservation", Test_TaskMetadataPreservation);
            await RunTest("Test 20: Queue Statistics Accuracy", Test_QueueStatisticsAccuracy);

            await RunTest("Test 21: TaskHandle with Result", Test_TaskHandleWithResult);
            await RunTest("Test 22: TaskHandle with Exception", Test_TaskHandleWithException);
            await RunTest("Test 23: TaskHandle with Cancellation", Test_TaskHandleWithCancellation);
            await RunTest("Test 24: TaskQueueOptions Pattern", Test_TaskQueueOptionsPattern);
            await RunTest("Test 25: GetRunningTasksInfo", Test_GetRunningTasksInfo);
            await RunTest("Test 26: EnqueueAsync with Timeout Success", Test_EnqueueAsyncTimeoutSuccess);
            await RunTest("Test 27: EnqueueAsync with Timeout Exceeded", Test_EnqueueAsyncTimeoutExceeded);
            await RunTest("Test 28: Task Priority Property", Test_TaskPriorityProperty);
            await RunTest("Test 29: TaskQueue.Create Factory Method", Test_TaskQueueCreateFactory);
            await RunTest("Test 30: TaskHandle Multiple Concurrent Results", Test_TaskHandleMultipleConcurrentResults);

            await RunTest("Test 31: GetStatistics Total Enqueued", Test_GetStatisticsTotalEnqueued);
            await RunTest("Test 32: GetStatistics Total Completed", Test_GetStatisticsTotalCompleted);
            await RunTest("Test 33: GetStatistics Total Failed", Test_GetStatisticsTotalFailed);
            await RunTest("Test 34: GetStatistics Total Canceled", Test_GetStatisticsTotalCanceled);
            await RunTest("Test 35: GetStatistics Average Execution Time", Test_GetStatisticsAverageExecutionTime);

            await RunTest("Test 36: Progress Reporting With Result", Test_ProgressReportingWithResult);
            await RunTest("Test 37: Progress Reporting Without Result", Test_ProgressReportingWithoutResult);
            await RunTest("Test 38: Progress Reporting Percentage", Test_ProgressReportingPercentage);

            // Print summary
            Console.WriteLine();
            Console.WriteLine("=======================================================================");
            Console.WriteLine("Test Summary");
            Console.WriteLine("=======================================================================");

            int passed = 0;
            int failed = 0;

            foreach (TestResult result in _Results)
            {
                string status = result.Passed ? "PASS" : "FAIL";
                ConsoleColor color = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;

                lock (_ConsoleLock)
                {
                    Console.ForegroundColor = color;
                    Console.Write($"[{status}]");
                    Console.ResetColor();
                    Console.WriteLine($" {result.TestName}");

                    if (!result.Passed && !String.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"       Error: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }

                if (result.Passed) passed++;
                else failed++;
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {_Results.Count} | Passed: {passed} | Failed: {failed}");
            Console.WriteLine();

            // Overall result
            if (failed > 0)
            {
                lock (_ConsoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("OVERALL: FAIL");
                    Console.ResetColor();
                }
                Environment.Exit(1);
            }
            else
            {
                lock (_ConsoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OVERALL: PASS");
                    Console.ResetColor();
                }
                Environment.Exit(0);
            }
        }

        private static async Task RunTest(string testName, Func<Task<TestResult>> testFunc)
        {
            Console.WriteLine($"Running: {testName}");

            try
            {
                TestResult result = await testFunc();
                result.TestName = testName;
                _Results.Add(result);

                string status = result.Passed ? "PASS" : "FAIL";
                ConsoleColor color = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;

                lock (_ConsoleLock)
                {
                    Console.ForegroundColor = color;
                    Console.WriteLine($"  Result: {status}");
                    Console.ResetColor();

                    if (!result.Passed && !String.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  Error: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                TestResult result = new TestResult
                {
                    TestName = testName,
                    Passed = false,
                    ErrorMessage = $"Unhandled exception: {ex.Message}"
                };
                _Results.Add(result);

                lock (_ConsoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Result: FAIL");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  Error: {result.ErrorMessage}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
        }

        // =====================================================================
        // Test Cases
        // =====================================================================

        private static async Task<TestResult> Test_BasicEnqueueAndExecution()
        {
            TaskQueue queue = new TaskQueue();
            int executionCount = 0;

            TaskDetails details = queue.AddTask(
                Guid.NewGuid(),
                "BasicTask",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(100, token);
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (executionCount == 1)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 1 execution, got {executionCount}");
            }
        }

        private static async Task<TestResult> Test_ConcurrencyLimits()
        {
            TaskQueue queue = new TaskQueue(2);
            int maxConcurrent = 0;
            int currentConcurrent = 0;
            object lockObj = new object();

            for (int i = 0; i < 10; i++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{i}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        lock (lockObj)
                        {
                            currentConcurrent++;
                            if (currentConcurrent > maxConcurrent)
                            {
                                maxConcurrent = currentConcurrent;
                            }
                        }

                        await Task.Delay(200, token);

                        lock (lockObj)
                        {
                            currentConcurrent--;
                        }
                    }
                );
            }

            queue.Start();
            await Task.Delay(3000);
            queue.Stop();
            queue.Dispose();

            if (maxConcurrent <= 2)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected max 2 concurrent, got {maxConcurrent}");
            }
        }

        private static async Task<TestResult> Test_CancelIndividualTask()
        {
            TaskQueue queue = new TaskQueue();
            bool wasCanceled = false;
            Guid taskGuid = Guid.NewGuid();

            queue.AddTask(
                taskGuid,
                "CancelableTask",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    try
                    {
                        await Task.Delay(5000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        wasCanceled = true;
                    }
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop(taskGuid);
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (wasCanceled)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail("Task was not canceled");
            }
        }

        private static async Task<TestResult> Test_CancelAllTasks()
        {
            TaskQueue queue = new TaskQueue();
            int canceledCount = 0;
            int startedCount = 0;

            queue.OnTaskStarted += (sender, details) =>
            {
                Interlocked.Increment(ref startedCount);
            };

            for (int i = 0; i < 5; i++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{i}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        try
                        {
                            await Task.Delay(5000, token);
                        }
                        catch (OperationCanceledException)
                        {
                            Interlocked.Increment(ref canceledCount);
                        }
                    }
                );
            }

            queue.Start();

            // Wait for all tasks to start
            while (startedCount < 5 && startedCount < 1000)
            {
                await Task.Delay(10);
            }

            queue.Stop();
            await Task.Delay(500);
            queue.Dispose();

            if (canceledCount >= 5)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected at least 5 canceled tasks, got {canceledCount}. Started: {startedCount}");
            }
        }

        private static async Task<TestResult> Test_DisposalCleanup()
        {
            TaskQueue queue = new TaskQueue();
            queue.AddTask(
                Guid.NewGuid(),
                "Task1",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            queue.Start();
            await Task.Delay(200);
            queue.Dispose();

            // If we get here without exception, dispose worked
            return TestResult.Pass();
        }

        private static async Task<TestResult> Test_CannotStartTwice()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            try
            {
                queue.Start();
                queue.Stop();
                queue.Dispose();
                return TestResult.Fail("Should have thrown InvalidOperationException");
            }
            catch (InvalidOperationException)
            {
                queue.Stop();
                queue.Dispose();
                return TestResult.Pass();
            }
        }

        private static async Task<TestResult> Test_CannotStopWhenNotStarted()
        {
            TaskQueue queue = new TaskQueue();
            queue.Stop(); // Should not throw
            queue.Dispose();
            return TestResult.Pass();
        }

        private static async Task<TestResult> Test_CannotUseAfterDisposal()
        {
            TaskQueue queue = new TaskQueue();
            queue.Dispose();

            try
            {
                queue.Start();
                return TestResult.Fail("Should have thrown ObjectDisposedException");
            }
            catch (ObjectDisposedException)
            {
                return TestResult.Pass();
            }
        }

        private static async Task<TestResult> Test_EventHandlerExceptionsCaught()
        {
            TaskQueue queue = new TaskQueue();
            bool eventFired = false;
            List<string> logMessages = new List<string>();

            queue.Logger = (msg) =>
            {
                lock (logMessages)
                {
                    logMessages.Add(msg);
                }
            };

            queue.OnTaskAdded += (sender, details) =>
            {
                eventFired = true;
                throw new Exception("Test exception in event handler");
            };

            queue.AddTask(
                Guid.NewGuid(),
                "Task1",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            // Check if exception was logged
            bool exceptionLogged = false;
            lock (logMessages)
            {
                exceptionLogged = logMessages.Any(m => m.Contains("exception in event handler"));
            }

            if (eventFired && exceptionLogged)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Event fired: {eventFired}, Exception logged: {exceptionLogged}");
            }
        }

        private static async Task<TestResult> Test_TaskCompletionEvents()
        {
            TaskQueue queue = new TaskQueue();
            bool taskFinished = false;

            queue.OnTaskFinished += (sender, details) =>
            {
                taskFinished = true;
            };

            queue.AddTask(
                Guid.NewGuid(),
                "Task1",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (taskFinished)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail("OnTaskFinished event was not fired");
            }
        }

        private static async Task<TestResult> Test_TaskFaultedEvents()
        {
            TaskQueue queue = new TaskQueue();
            bool taskFaulted = false;

            queue.OnTaskFaulted += (sender, details) =>
            {
                taskFaulted = true;
            };

            queue.AddTask(
                Guid.NewGuid(),
                "FaultingTask",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                    throw new Exception("Test exception");
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (taskFaulted)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail("OnTaskFaulted event was not fired");
            }
        }

        private static async Task<TestResult> Test_TaskCanceledEvents()
        {
            TaskQueue queue = new TaskQueue();
            bool taskCanceled = false;
            Guid taskGuid = Guid.NewGuid();

            queue.OnTaskCanceled += (sender, details) =>
            {
                if (details.Guid == taskGuid)
                {
                    taskCanceled = true;
                }
            };

            queue.AddTask(
                taskGuid,
                "CancelableTask",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(5000, token);
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop(taskGuid);
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (taskCanceled)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail("OnTaskCanceled event was not fired");
            }
        }

        private static async Task<TestResult> Test_MultipleStartStopCycles()
        {
            TaskQueue queue = new TaskQueue();
            int executionCount = 0;

            for (int cycle = 0; cycle < 3; cycle++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{cycle}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        Interlocked.Increment(ref executionCount);
                        await Task.Delay(100, token);
                    }
                );

                queue.Start();
                await Task.Delay(500);
                queue.Stop();
                await Task.Delay(200);
            }

            queue.Dispose();

            if (executionCount == 3)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 3 executions, got {executionCount}");
            }
        }

        private static async Task<TestResult> Test_RaceConditionSafety()
        {
            TaskQueue queue = new TaskQueue(10);
            int tasksCompleted = 0;
            int tasksAdded = 100;

            queue.OnTaskFinished += (sender, details) =>
            {
                Interlocked.Increment(ref tasksCompleted);
            };

            for (int i = 0; i < tasksAdded; i++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{i}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(50, token);
                    }
                );
            }

            queue.Start();

            // Wait for all tasks to complete (max 30 seconds)
            int waited = 0;
            while (tasksCompleted < tasksAdded && waited < 30000)
            {
                await Task.Delay(100);
                waited += 100;
            }

            queue.Stop();
            queue.Dispose();

            if (tasksCompleted == tasksAdded)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected {tasksAdded} completed tasks, got {tasksCompleted}");
            }
        }

        private static async Task<TestResult> Test_TaskRunWithTimeoutSuccess()
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            Func<CancellationToken, Task<string>> task = async (CancellationToken token) =>
            {
                await Task.Delay(100, token);
                return "Success";
            };

            try
            {
                string result = await TaskRunWithTimeout.Go(task(cts.Token), 1000, cts);

                if (result == "Success")
                {
                    return TestResult.Pass();
                }
                else
                {
                    return TestResult.Fail($"Expected 'Success', got '{result}'");
                }
            }
            catch (Exception ex)
            {
                return TestResult.Fail($"Unexpected exception: {ex.Message}");
            }
        }

        private static async Task<TestResult> Test_TaskRunWithTimeoutTimeout()
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            Func<CancellationToken, Task<string>> task = async (CancellationToken token) =>
            {
                await Task.Delay(2000, token);
                return "Should not reach here";
            };

            try
            {
                string result = await TaskRunWithTimeout.Go(task(cts.Token), 500, cts);
                return TestResult.Fail("Should have thrown TimeoutException");
            }
            catch (TimeoutException)
            {
                return TestResult.Pass();
            }
            catch (Exception ex)
            {
                return TestResult.Fail($"Expected TimeoutException, got {ex.GetType().Name}");
            }
        }

        private static async Task<TestResult> Test_HighThroughputStressTest()
        {
            TaskQueue queue = new TaskQueue(50);
            int tasksAdded = 1000;
            int tasksCompleted = 0;

            queue.OnTaskFinished += (sender, details) =>
            {
                Interlocked.Increment(ref tasksCompleted);
            };

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < tasksAdded; i++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{i}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(10, token);
                    }
                );
            }

            queue.Start();

            // Wait for all tasks to complete (max 60 seconds)
            int waited = 0;
            while (tasksCompleted < tasksAdded && waited < 60000)
            {
                await Task.Delay(100);
                waited += 100;
            }

            sw.Stop();
            queue.Stop();
            queue.Dispose();

            if (tasksCompleted == tasksAdded)
            {
                return TestResult.Pass($"Completed {tasksAdded} tasks in {sw.ElapsedMilliseconds}ms");
            }
            else
            {
                return TestResult.Fail($"Expected {tasksAdded} completed tasks, got {tasksCompleted}. Waited {waited}ms. Running: {queue.RunningCount}, Queued: {queue.QueuedCount}");
            }
        }

        private static async Task<TestResult> Test_ZeroConcurrencyValidation()
        {
            try
            {
                TaskQueue queue = new TaskQueue(0);
                queue.Dispose();
                return TestResult.Fail("Should have thrown ArgumentOutOfRangeException");
            }
            catch (ArgumentOutOfRangeException)
            {
                return TestResult.Pass();
            }
        }

        private static async Task<TestResult> Test_TaskMetadataPreservation()
        {
            TaskQueue queue = new TaskQueue();
            string expectedValue = "TestValue123";
            string? actualValue = null;

            Dictionary<string, object> metadata = new Dictionary<string, object>
            {
                { "TestKey", expectedValue }
            };

            queue.OnTaskStarted += (sender, details) =>
            {
                if (details.Metadata != null && details.Metadata.ContainsKey("TestKey"))
                {
                    actualValue = details.Metadata["TestKey"] as string;
                }
            };

            queue.AddTask(
                Guid.NewGuid(),
                "MetadataTask",
                metadata,
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (actualValue == expectedValue)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected metadata '{expectedValue}', got '{actualValue}'");
            }
        }

        private static async Task<TestResult> Test_QueueStatisticsAccuracy()
        {
            TaskQueue queue = new TaskQueue(2);
            int tasksToAdd = 10;

            for (int i = 0; i < tasksToAdd; i++)
            {
                queue.AddTask(
                    Guid.NewGuid(),
                    $"Task{i}",
                    new Dictionary<string, object>(),
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(200, token);
                    }
                );
            }

            int initialQueuedCount = queue.QueuedCount;

            queue.Start();
            await Task.Delay(100);

            int runningCount = queue.RunningCount;

            // Wait for all to complete
            await Task.Delay(3000);
            queue.Stop();
            queue.Dispose();

            if (initialQueuedCount == tasksToAdd && runningCount <= 2)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Initial queued: {initialQueuedCount} (expected {tasksToAdd}), Running: {runningCount} (expected <=2)");
            }
        }

        // =====================================================================
        // =====================================================================

        private static async Task<TestResult> Test_TaskHandleWithResult()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            TaskHandle<string> handle = await queue.EnqueueAsync(
                "ResultTask",
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                    return "Success Result";
                }
            );

            string result = await handle.Task;

            queue.Stop();
            queue.Dispose();

            if (result == "Success Result")
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 'Success Result', got '{result}'");
            }
        }

        private static async Task<TestResult> Test_TaskHandleWithException()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            TaskHandle<string> handle = await queue.EnqueueAsync(
                "FaultingTask",
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                    throw new InvalidOperationException("Test exception");
#pragma warning disable CS0162
                    return "Should not reach";
#pragma warning restore CS0162
                }
            );

            try
            {
                string result = await handle.Task;
                queue.Stop();
                queue.Dispose();
                return TestResult.Fail("Should have thrown InvalidOperationException");
            }
            catch (InvalidOperationException ex)
            {
                queue.Stop();
                queue.Dispose();
                if (ex.Message == "Test exception")
                {
                    return TestResult.Pass();
                }
                else
                {
                    return TestResult.Fail($"Wrong exception message: {ex.Message}");
                }
            }
        }

        private static async Task<TestResult> Test_TaskHandleWithCancellation()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            TaskHandle<string> handle = await queue.EnqueueAsync(
                "CancelTask",
                async (CancellationToken token) =>
                {
                    await Task.Delay(5000, token);
                    return "Should not complete";
                }
            );

            await Task.Delay(200);
            queue.Stop(handle.Id);

            try
            {
                string result = await handle.Task;
                queue.Stop();
                queue.Dispose();
                return TestResult.Fail("Should have thrown OperationCanceledException");
            }
            catch (OperationCanceledException)
            {
                queue.Stop();
                queue.Dispose();
                return TestResult.Pass();
            }
        }

        private static async Task<TestResult> Test_TaskQueueOptionsPattern()
        {
            bool eventFired = false;

            TaskQueue queue = new TaskQueue(new TaskQueueOptions
            {
                MaxConcurrentTasks = 5,
                MaxQueueSize = 100,
                Logger = (msg) => { },
                OnTaskFinished = (sender, details) =>
                {
                    eventFired = true;
                }
            });

            queue.Start();

            await queue.EnqueueAsync(
                "OptionsTest",
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (eventFired && queue.MaxConcurrentTasks == 5)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Event fired: {eventFired}, MaxConcurrentTasks: {queue.MaxConcurrentTasks}");
            }
        }

        private static async Task<TestResult> Test_GetRunningTasksInfo()
        {
            TaskQueue queue = new TaskQueue(2);
            queue.Start();

            await queue.EnqueueAsync(
                "Task1",
                async (CancellationToken token) => await Task.Delay(500, token)
            );

            await queue.EnqueueAsync(
                "Task2",
                async (CancellationToken token) => await Task.Delay(500, token)
            );

            await Task.Delay(100);

            IReadOnlyCollection<TaskInfo> runningTasks = queue.GetRunningTasksInfo();

            queue.Stop();
            await Task.Delay(200);
            queue.Dispose();

            if (runningTasks.Count == 2)
            {
                TaskInfo firstTask = runningTasks.First();
                if (firstTask.Name == "Task1" || firstTask.Name == "Task2")
                {
                    return TestResult.Pass();
                }
                else
                {
                    return TestResult.Fail($"Wrong task name: {firstTask.Name}");
                }
            }
            else
            {
                return TestResult.Fail($"Expected 2 running tasks, got {runningTasks.Count}");
            }
        }

        private static async Task<TestResult> Test_EnqueueAsyncTimeoutSuccess()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            Guid taskId = await queue.EnqueueAsync(
                "QuickTask",
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                },
                timeout: TimeSpan.FromSeconds(2)
            );

            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            return TestResult.Pass();
        }

        private static async Task<TestResult> Test_EnqueueAsyncTimeoutExceeded()
        {
            TaskQueue queue = new TaskQueue();
            bool taskFaulted = false;

            queue.OnTaskFaulted += (sender, details) =>
            {
                if (details.Task?.Exception != null)
                {
                    Exception? innerEx = details.Task.Exception.InnerException;
                    if (innerEx is TimeoutException)
                    {
                        taskFaulted = true;
                    }
                }
            };

            queue.Start();

            Guid taskId = await queue.EnqueueAsync(
                "SlowTask",
                async (CancellationToken token) =>
                {
                    await Task.Delay(5000, token);
                },
                timeout: TimeSpan.FromMilliseconds(200)
            );

            await Task.Delay(1000);
            queue.Stop();
            queue.Dispose();

            if (taskFaulted)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail("Task did not fault with TimeoutException");
            }
        }

        private static async Task<TestResult> Test_TaskPriorityProperty()
        {
            TaskQueue queue = new TaskQueue();

            TaskDetails details = queue.AddTask(
                Guid.NewGuid(),
                "PriorityTask",
                new Dictionary<string, object>(),
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            details.Priority = (int)TaskPriority.High;

            queue.Start();
            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (details.Priority == (int)TaskPriority.High)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected priority {(int)TaskPriority.High}, got {details.Priority}");
            }
        }

        private static async Task<TestResult> Test_TaskQueueCreateFactory()
        {
            bool eventFired = false;

            TaskQueue queue = TaskQueue.Create(options =>
            {
                options.MaxConcurrentTasks = 3;
                options.MaxQueueSize = 50;
                options.OnTaskStarted = (sender, details) =>
                {
                    eventFired = true;
                };
            });

            queue.Start();

            await queue.EnqueueAsync(
                "FactoryTest",
                async (CancellationToken token) =>
                {
                    await Task.Delay(100, token);
                }
            );

            await Task.Delay(500);
            queue.Stop();
            queue.Dispose();

            if (eventFired && queue.MaxConcurrentTasks == 3 && queue.MaxQueueSize == 50)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Event fired: {eventFired}, MaxConcurrentTasks: {queue.MaxConcurrentTasks}, MaxQueueSize: {queue.MaxQueueSize}");
            }
        }

        private static async Task<TestResult> Test_TaskHandleMultipleConcurrentResults()
        {
            TaskQueue queue = new TaskQueue(10);
            queue.Start();

            List<TaskHandle<int>> handles = new List<TaskHandle<int>>();

            for (int i = 0; i < 20; i++)
            {
                int value = i;
                TaskHandle<int> handle = await queue.EnqueueAsync(
                    $"Task{i}",
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(50, token);
                        return value * 2;
                    }
                );
                handles.Add(handle);
            }

            int[] results = await Task.WhenAll(handles.Select(h => h.Task));

            queue.Stop();
            queue.Dispose();

            bool allCorrect = true;
            for (int i = 0; i < 20; i++)
            {
                if (results[i] != i * 2)
                {
                    allCorrect = false;
                    break;
                }
            }

            if (allCorrect && results.Length == 20)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 20 correct results, got {results.Length} results. All correct: {allCorrect}");
            }
        }

        // =====================================================================

        private static async Task<TestResult> Test_GetStatisticsTotalEnqueued()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            for (int i = 0; i < 10; i++)
            {
                await queue.EnqueueAsync(
                    $"Task{i}",
                    async (CancellationToken token) => await Task.Delay(50, token)
                );
            }

            await Task.Delay(200);
            TaskQueueStatistics stats = queue.GetStatistics();

            queue.Stop();
            queue.Dispose();

            if (stats.TotalEnqueued == 10)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 10 enqueued, got {stats.TotalEnqueued}");
            }
        }

        private static async Task<TestResult> Test_GetStatisticsTotalCompleted()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            for (int i = 0; i < 10; i++)
            {
                await queue.EnqueueAsync(
                    $"Task{i}",
                    async (CancellationToken token) => await Task.Delay(50, token)
                );
            }

            await Task.Delay(1000);
            TaskQueueStatistics stats = queue.GetStatistics();

            queue.Stop();
            queue.Dispose();

            if (stats.TotalCompleted == 10)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 10 completed, got {stats.TotalCompleted}");
            }
        }

        private static async Task<TestResult> Test_GetStatisticsTotalFailed()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            for (int i = 0; i < 5; i++)
            {
                await queue.EnqueueAsync(
                    $"FaultingTask{i}",
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(50, token);
                        throw new InvalidOperationException("Test exception");
                    }
                );
            }

            await Task.Delay(500);
            TaskQueueStatistics stats = queue.GetStatistics();

            queue.Stop();
            queue.Dispose();

            if (stats.TotalFailed == 5)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 5 failed, got {stats.TotalFailed}");
            }
        }

        private static async Task<TestResult> Test_GetStatisticsTotalCanceled()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            List<Guid> taskIds = new List<Guid>();

            for (int i = 0; i < 5; i++)
            {
                Guid taskId = await queue.EnqueueAsync(
                    $"CancelableTask{i}",
                    async (CancellationToken token) =>
                    {
                        await Task.Delay(5000, token);
                    }
                );
                taskIds.Add(taskId);
            }

            await Task.Delay(200);

            foreach (Guid taskId in taskIds)
            {
                queue.Stop(taskId);
            }

            await Task.Delay(500);
            TaskQueueStatistics stats = queue.GetStatistics();

            queue.Stop();
            queue.Dispose();

            if (stats.TotalCanceled == 5)
            {
                return TestResult.Pass();
            }
            else
            {
                return TestResult.Fail($"Expected 5 canceled, got {stats.TotalCanceled}");
            }
        }

        private static async Task<TestResult> Test_GetStatisticsAverageExecutionTime()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            for (int i = 0; i < 10; i++)
            {
                await queue.EnqueueAsync(
                    $"Task{i}",
                    async (CancellationToken token) => await Task.Delay(100, token)
                );
            }

            await Task.Delay(2000);
            TaskQueueStatistics stats = queue.GetStatistics();

            queue.Stop();
            queue.Dispose();

            if (stats.AverageExecutionTime.TotalMilliseconds >= 90 && stats.AverageExecutionTime.TotalMilliseconds <= 200)
            {
                return TestResult.Pass($"Average execution time: {stats.AverageExecutionTime.TotalMilliseconds:F2}ms");
            }
            else
            {
                return TestResult.Fail($"Expected average execution time around 100ms, got {stats.AverageExecutionTime.TotalMilliseconds:F2}ms");
            }
        }

        // =====================================================================
        // =====================================================================

        private static async Task<TestResult> Test_ProgressReportingWithResult()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            List<TaskProgress> progressUpdates = new List<TaskProgress>();
            Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
            {
                lock (progressUpdates)
                {
                    progressUpdates.Add(p);
                }
            });

            TaskHandle<string> handle = await queue.EnqueueAsync(
                "ProgressTask",
                async (CancellationToken token, IProgress<TaskProgress> prog) =>
                {
                    for (int i = 0; i <= 10; i++)
                    {
                        prog?.Report(new TaskProgress(i, 10, $"Step {i}"));
                        await Task.Delay(50, token);
                    }
                    return "Completed";
                },
                progress
            );

            string result = await handle.Task;

            queue.Stop();
            queue.Dispose();

            int progressCount = 0;
            lock (progressUpdates)
            {
                progressCount = progressUpdates.Count;
            }

            if (result == "Completed" && progressCount >= 10)
            {
                return TestResult.Pass($"Progress updates: {progressCount}");
            }
            else
            {
                return TestResult.Fail($"Expected 'Completed' with >= 10 progress updates, got result='{result}', updates={progressCount}");
            }
        }

        private static async Task<TestResult> Test_ProgressReportingWithoutResult()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            List<TaskProgress> progressUpdates = new List<TaskProgress>();
            Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
            {
                lock (progressUpdates)
                {
                    progressUpdates.Add(p);
                }
            });

            Guid taskId = await queue.EnqueueAsync(
                "ProgressTaskNoResult",
                async (CancellationToken token, IProgress<TaskProgress> prog) =>
                {
                    for (int i = 0; i <= 5; i++)
                    {
                        prog?.Report(new TaskProgress(i, 5, $"Processing {i}"));
                        await Task.Delay(50, token);
                    }
                },
                progress
            );

            await Task.Delay(1000);

            queue.Stop();
            queue.Dispose();

            int progressCount = 0;
            lock (progressUpdates)
            {
                progressCount = progressUpdates.Count;
            }

            if (progressCount >= 5)
            {
                return TestResult.Pass($"Progress updates: {progressCount}");
            }
            else
            {
                return TestResult.Fail($"Expected >= 5 progress updates, got {progressCount}");
            }
        }

        private static async Task<TestResult> Test_ProgressReportingPercentage()
        {
            TaskQueue queue = new TaskQueue();
            queue.Start();

            List<double> percentages = new List<double>();
            Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
            {
                lock (percentages)
                {
                    percentages.Add(p.PercentComplete);
                }
            });

            TaskHandle<int> handle = await queue.EnqueueAsync(
                "PercentageTask",
                async (CancellationToken token, IProgress<TaskProgress> prog) =>
                {
                    for (int i = 0; i <= 100; i += 10)
                    {
                        prog?.Report(new TaskProgress(i, 100));
                        await Task.Delay(20, token);
                    }
                    return 100;
                },
                progress
            );

            int result = await handle.Task;

            queue.Stop();
            queue.Dispose();

            bool has100Percent = false;
            lock (percentages)
            {
                has100Percent = percentages.Any(p => p >= 99.0);
            }

            if (has100Percent && result == 100)
            {
                return TestResult.Pass($"Progress reached 100%");
            }
            else
            {
                return TestResult.Fail($"Expected 100% progress, got has100%={has100Percent}, result={result}");
            }
        }
    }

    public class TestResult
    {
        public string TestName { get; set; } = "";
        public bool Passed { get; set; }
        public string ErrorMessage { get; set; } = "";

        public static TestResult Pass(string message = "")
        {
            return new TestResult { Passed = true, ErrorMessage = message };
        }

        public static TestResult Fail(string message)
        {
            return new TestResult { Passed = false, ErrorMessage = message };
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}
