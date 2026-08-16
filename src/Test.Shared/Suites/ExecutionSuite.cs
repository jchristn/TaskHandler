namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for task enqueue and execution across the AddTask, AddTaskAsync, and EnqueueAsync APIs.
    /// </summary>
    public static class ExecutionSuite
    {
        private const string Id = "Execution";

        /// <summary>
        /// Build the execution test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "AddTaskExecutesOnce", "AddTask executes the task exactly once", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.AddTask(Guid.NewGuid(), "Basic", new Dictionary<string, object>(), async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(30, token);
                        });
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "task should run once");
                        await Task.Delay(100);
                        queue.Stop();
                    }
                    Check.Equal(1, count, "execution count");
                }),

                TaskHandlerSuites.Case(Id, "AddTaskReturnsDetails", "AddTask returns populated TaskDetails", async ct =>
                {
                    Guid guid = Guid.NewGuid();
                    Dictionary<string, object> md = new Dictionary<string, object> { { "k", "v" } };
                    using (TaskQueue queue = new TaskQueue())
                    {
                        TaskDetails details = queue.AddTask(guid, "Named", md, async token => await Task.Delay(10, token));
                        Check.Equal(guid, details.Guid, "Guid");
                        Check.Equal("Named", details.Name, "Name");
                        Check.NotNull(details.Metadata, "Metadata");
                        Check.True(details.Metadata.ContainsKey("k"), "metadata key preserved");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "AddTaskAsyncExecutes", "AddTaskAsync executes the task", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        await queue.AddTaskAsync(Guid.NewGuid(), "AsyncAdd", new Dictionary<string, object>(), async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(20, token);
                        });
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "task should run once");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "EnqueueAsyncNoResultReturnsGuid", "EnqueueAsync (no result) returns a GUID and executes", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        Guid id = await queue.EnqueueAsync("Void", async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(20, token);
                        });
                        Check.True(id != Guid.Empty, "returned GUID should be non-empty");
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "task should run");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "EnqueueAsyncResultReturnsValue", "EnqueueAsync with result returns the value", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        TaskHandle<string> handle = await queue.EnqueueAsync("Result", async token =>
                        {
                            await Task.Delay(30, token);
                            return "hello";
                        });
                        string result = await handle.Task;
                        Check.Equal("hello", result, "result");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "MultipleConcurrentResults", "Many result tasks all return correct values", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(10))
                    {
                        queue.Start();
                        List<TaskHandle<int>> handles = new List<TaskHandle<int>>();
                        for (int i = 0; i < 20; i++)
                        {
                            int value = i;
                            handles.Add(await queue.EnqueueAsync($"R{i}", async token =>
                            {
                                await Task.Delay(20, token);
                                return value * 2;
                            }));
                        }

                        int[] results = await Task.WhenAll(handles.ConvertAll(h => h.Task));
                        for (int i = 0; i < 20; i++)
                        {
                            Check.Equal(i * 2, results[i], $"result[{i}]");
                        }
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "HighThroughput", "All tasks in a large batch complete", async ct =>
                {
                    const int total = 500;
                    int completed = 0;
                    using (TaskQueue queue = new TaskQueue(50))
                    {
                        queue.OnTaskFinished += (s, d) => Interlocked.Increment(ref completed);
                        for (int i = 0; i < total; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), $"T{i}", new Dictionary<string, object>(), async token => await Task.Delay(5, token));
                        }
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref completed) == total, 60000), $"all {total} tasks should complete (got {completed})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "AddAfterStopRecreatesChannel", "Adding after Stop recreates the channel and runs", async ct =>
                {
                    int count = 0;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        queue.AddTask(Guid.NewGuid(), "First", new Dictionary<string, object>(), async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(10, token);
                        });
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 1), "first task runs");
                        queue.Stop();

                        // Channel is completed after Stop; adding should recreate it.
                        queue.AddTask(Guid.NewGuid(), "Second", new Dictionary<string, object>(), async token =>
                        {
                            Interlocked.Increment(ref count);
                            await Task.Delay(10, token);
                        });
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref count) == 2), "second task runs after restart");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "BoundedQueueProcessesAll", "Bounded queue with backpressure processes all tasks", async ct =>
                {
                    int completed = 0;
                    using (TaskQueue queue = new TaskQueue(2, 5))
                    {
                        queue.OnTaskFinished += (s, d) => Interlocked.Increment(ref completed);
                        queue.Start();
                        for (int i = 0; i < 20; i++)
                        {
                            await queue.AddTaskAsync(Guid.NewGuid(), $"B{i}", new Dictionary<string, object>(), async token => await Task.Delay(15, token));
                        }
                        await queue.WaitForCompletionAsync();
                        Check.True(await Check.WaitUntilAsync(() => Volatile.Read(ref completed) == 20), $"all 20 should complete (got {completed})");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "BoundedOverflowThrows", "Bounded queue overflow throws when not draining", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(1, 2))
                    {
                        // Not started, so nothing drains the channel.
                        queue.AddTask(Guid.NewGuid(), "T1", new Dictionary<string, object>(), async token => await Task.Delay(10, token));
                        queue.AddTask(Guid.NewGuid(), "T2", new Dictionary<string, object>(), async token => await Task.Delay(10, token));
                        Check.Throws<InvalidOperationException>(() =>
                            queue.AddTask(Guid.NewGuid(), "T3", new Dictionary<string, object>(), async token => await Task.Delay(10, token)),
                            "third AddTask on full bounded queue");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "EnqueueTimeoutFaults", "EnqueueAsync (no result) exceeding its timeout faults and counts as failed", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        bool faulted = false;
                        queue.OnTaskFaulted += (s, d) => faulted = true;
                        queue.Start();
                        await queue.EnqueueAsync("SlowVoid", async token =>
                        {
                            await Task.Delay(5000, token);
                        }, priority: 0, timeout: TimeSpan.FromMilliseconds(150));

                        Check.True(await Check.WaitUntilAsync(() => faulted), "OnTaskFaulted should fire on timeout");
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalFailed >= 1), "TotalFailed should increment on timeout");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "EnqueueWithinTimeoutFinishes", "EnqueueAsync (no result) completing before its timeout finishes normally", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        bool finished = false;
                        bool faulted = false;
                        queue.OnTaskFinished += (s, d) => finished = true;
                        queue.OnTaskFaulted += (s, d) => faulted = true;
                        queue.Start();
                        await queue.EnqueueAsync("QuickVoid", async token =>
                        {
                            await Task.Delay(30, token);
                        }, priority: 0, timeout: TimeSpan.FromSeconds(5));

                        Check.True(await Check.WaitUntilAsync(() => finished), "OnTaskFinished should fire within timeout");
                        Check.False(faulted, "task should not fault when it completes within the timeout");
                        queue.Stop();
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Enqueue & Execution", cases);
        }
    }
}
