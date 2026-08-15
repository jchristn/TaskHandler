namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for TaskQueue construction, the options constructor, and the Create factory.
    /// </summary>
    public static class ConstructionSuite
    {
        private const string Id = "Construction";

        /// <summary>
        /// Build the construction test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "DefaultDefaults", "Default constructor has expected defaults", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        Check.Equal(32, queue.MaxConcurrentTasks, "MaxConcurrentTasks default");
                        Check.Equal(-1, queue.MaxQueueSize, "MaxQueueSize default");
                        Check.Equal(0, queue.RunningCount, "RunningCount default");
                        Check.Equal(0, queue.QueuedCount, "QueuedCount default");
                        Check.False(queue.IsRunning, "IsRunning default");
                        Check.NotNull(queue.RunningTasks, "RunningTasks dictionary");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "MaxConcurrentCtor", "Constructor stores max concurrent tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(8))
                    {
                        Check.Equal(8, queue.MaxConcurrentTasks, "MaxConcurrentTasks");
                        Check.Equal(-1, queue.MaxQueueSize, "MaxQueueSize");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "BoundedCtor", "Constructor stores bounded queue size", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(4, 10))
                    {
                        Check.Equal(4, queue.MaxConcurrentTasks, "MaxConcurrentTasks");
                        Check.Equal(10, queue.MaxQueueSize, "MaxQueueSize");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ZeroConcurrencyThrows", "Zero concurrency throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() =>
                    {
                        TaskQueue q = new TaskQueue(0);
                        q.Dispose();
                    }, "new TaskQueue(0)");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NegativeConcurrencyThrows", "Negative concurrency throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() =>
                    {
                        TaskQueue q = new TaskQueue(-3);
                        q.Dispose();
                    }, "new TaskQueue(-3)");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ZeroQueueSizeThrows", "Zero queue size throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() =>
                    {
                        TaskQueue q = new TaskQueue(32, 0);
                        q.Dispose();
                    }, "new TaskQueue(32, 0)");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "QueueSizeBelowNegOneThrows", "Queue size below -1 throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() =>
                    {
                        TaskQueue q = new TaskQueue(32, -2);
                        q.Dispose();
                    }, "new TaskQueue(32, -2)");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "UnboundedNegOneOk", "Queue size of -1 is accepted (unbounded)", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(32, -1))
                    {
                        Check.Equal(-1, queue.MaxQueueSize, "MaxQueueSize");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "OptionsCtorAppliesValues", "Options constructor applies values", async ct =>
                {
                    bool finished = false;
                    TaskQueueOptions options = new TaskQueueOptions
                    {
                        MaxConcurrentTasks = 5,
                        MaxQueueSize = 100,
                        Logger = msg => { },
                        OnTaskFinished = (sender, details) => finished = true
                    };

                    using (TaskQueue queue = new TaskQueue(options))
                    {
                        Check.Equal(5, queue.MaxConcurrentTasks, "MaxConcurrentTasks");
                        Check.Equal(100, queue.MaxQueueSize, "MaxQueueSize");
                        queue.Start();
                        await queue.EnqueueAsync("OptTask", async token => await Task.Delay(50, token));
                        Check.True(await Check.WaitUntilAsync(() => finished), "OnTaskFinished should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "CreateFactoryApplies", "Create factory applies configuration", async ct =>
                {
                    bool started = false;
                    using (TaskQueue queue = TaskQueue.Create(o =>
                    {
                        o.MaxConcurrentTasks = 3;
                        o.MaxQueueSize = 50;
                        o.OnTaskStarted = (s, d) => started = true;
                    }))
                    {
                        Check.Equal(3, queue.MaxConcurrentTasks, "MaxConcurrentTasks");
                        Check.Equal(50, queue.MaxQueueSize, "MaxQueueSize");
                        queue.Start();
                        await queue.EnqueueAsync("FactTask", async token => await Task.Delay(50, token));
                        Check.True(await Check.WaitUntilAsync(() => started), "OnTaskStarted should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "CreateFactoryNullConfig", "Create factory with null configure uses defaults", async ct =>
                {
                    using (TaskQueue queue = TaskQueue.Create(null!))
                    {
                        Check.Equal(32, queue.MaxConcurrentTasks, "MaxConcurrentTasks default");
                        Check.Equal(-1, queue.MaxQueueSize, "MaxQueueSize default");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "OptionsDefaults", "Options object has expected defaults", async ct =>
                {
                    TaskQueueOptions options = new TaskQueueOptions();
                    Check.Equal(32, options.MaxConcurrentTasks, "MaxConcurrentTasks default");
                    Check.Equal(-1, options.MaxQueueSize, "MaxQueueSize default");
                    Check.Null(options.Logger, "Logger default");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "OptionsInvalidConcurrencyThrows", "Options invalid max concurrent throws", async ct =>
                {
                    TaskQueueOptions options = new TaskQueueOptions();
                    Check.Throws<ArgumentOutOfRangeException>(() => options.MaxConcurrentTasks = 0, "options.MaxConcurrentTasks = 0");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "OptionsInvalidQueueSizeThrows", "Options invalid max queue size throws", async ct =>
                {
                    TaskQueueOptions options = new TaskQueueOptions();
                    Check.Throws<ArgumentOutOfRangeException>(() => options.MaxQueueSize = 0, "options.MaxQueueSize = 0");
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "Construction & Configuration", cases);
        }
    }
}
