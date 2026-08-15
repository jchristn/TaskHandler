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
            };

            return new TestSuiteDescriptor(Id, "Property Validation", cases);
        }
    }
}
