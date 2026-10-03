namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for TaskQueue public property validation and mutation.
    /// </summary>
    public static class PropertiesSuite
    {
        private const string Id = "Properties";

        /// <summary>
        /// Build the properties test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            System.Collections.Generic.List<TestCaseDescriptor> cases = new System.Collections.Generic.List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "MaxConcurrentSetZeroThrows", "Setting MaxConcurrentTasks to 0 throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Throws<ArgumentOutOfRangeException>(() => queue.MaxConcurrentTasks = 0, "MaxConcurrentTasks = 0");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxConcurrentSetNegativeThrows", "Setting MaxConcurrentTasks negative throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Throws<ArgumentOutOfRangeException>(() => queue.MaxConcurrentTasks = -1, "MaxConcurrentTasks = -1");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxConcurrentSetValid", "Setting MaxConcurrentTasks to valid value updates it", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.MaxConcurrentTasks = 16;
                        Check.Equal(16, queue.MaxConcurrentTasks, "MaxConcurrentTasks");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeSetZeroThrows", "Setting MaxQueueSize to 0 throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Throws<ArgumentOutOfRangeException>(() => queue.MaxQueueSize = 0, "MaxQueueSize = 0");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeSetBelowNegOneThrows", "Setting MaxQueueSize below -1 throws", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Throws<ArgumentOutOfRangeException>(() => queue.MaxQueueSize = -5, "MaxQueueSize = -5");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeSetValid", "Setting MaxQueueSize to valid value updates it", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.MaxQueueSize = 25;
                        Check.Equal(25, queue.MaxQueueSize, "MaxQueueSize");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeSetNegOneOk", "Setting MaxQueueSize to -1 is accepted", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(4, 10))
                    {
                        queue.MaxQueueSize = -1;
                        Check.Equal(-1, queue.MaxQueueSize, "MaxQueueSize");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "LoggerSettable", "Logger callback is settable and invoked", async ct =>
                {
                    bool logged = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Logger = msg => logged = true;
                        queue.Start();
                        await queue.EnqueueAsync("LogTask", async token => await Task.Delay(20, token));
                        Check.True(await Check.WaitUntilAsync(() => logged), "Logger should be invoked");
                        queue.Stop();
                    }
                })
,

                TaskHandlerSuites.Case(Id, "MaxConcurrentRaisedAtRuntime", "Raising MaxConcurrentTasks on a running queue lets waiting tasks start immediately", async ct =>
                {
                    TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        for (int i = 0; i < 3; i++) await queue.EnqueueAsync("g" + i, async token => await gate.Task);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "one running at limit 1");
                        queue.MaxConcurrentTasks = 3;
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 3), "three running after raising the limit");
                        gate.SetResult(true);
                        await queue.WaitForCompletionAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "MaxConcurrentLoweredAtRuntime", "Lowering MaxConcurrentTasks never interrupts running tasks and applies as they finish", async ct =>
                {
                    TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    int current = 0;
                    int peakAfterLowering = 0;
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 3))
                    {
                        queue.Start();
                        for (int i = 0; i < 3; i++) await queue.EnqueueAsync("g" + i, async token => await gate.Task);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 3), "three running");
                        for (int i = 0; i < 3; i++)
                        {
                            await queue.EnqueueAsync("late" + i, async token =>
                            {
                                int now = Interlocked.Increment(ref current);
                                lock (gate) { peakAfterLowering = Math.Max(peakAfterLowering, now); }
                                await Task.Delay(40);
                                Interlocked.Decrement(ref current);
                            });
                        }

                        queue.MaxConcurrentTasks = 1;
                        Check.Equal(3, queue.RunningCount, "running tasks are not interrupted");
                        gate.SetResult(true);
                        await queue.WaitForCompletionAsync();
                        Check.Equal(1, peakAfterLowering, "later tasks ran one at a time");
                    }
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeAppliesBeforeUse", "Setting MaxQueueSize before first use bounds the queue", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.MaxQueueSize = 1;
                        queue.AddTask(Guid.NewGuid(), "a", null, token => Task.CompletedTask);
                        Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "b", null, token => Task.CompletedTask), "second add rejected by the new bound");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxQueueSizeLockedAfterUse", "Changing MaxQueueSize after the queue was used throws, and re-setting the same value does not", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 4, maxQueueSize: 10))
                    {
                        queue.AddTask(Guid.NewGuid(), "a", null, token => Task.CompletedTask);
                        Check.Throws<InvalidOperationException>(() => queue.MaxQueueSize = 20, "change after use");
                        queue.MaxQueueSize = 10;
                        Check.Equal(10, queue.MaxQueueSize, "unchanged");
                    }
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "Property Validation", cases);
        }
    }
}
