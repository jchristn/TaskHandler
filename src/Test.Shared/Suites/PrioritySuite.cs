namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the TaskPriority enum and per-task priority storage.
    /// </summary>
    public static class PrioritySuite
    {
        private const string Id = "Priority";

        /// <summary>
        /// Build the priority test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "EnumValues", "TaskPriority enum has expected numeric values", async ct =>
                {
                    Check.Equal(0, (int)TaskPriority.Urgent, "Urgent");
                    Check.Equal(1, (int)TaskPriority.High, "High");
                    Check.Equal(2, (int)TaskPriority.Normal, "Normal");
                    Check.Equal(3, (int)TaskPriority.Low, "Low");
                    Check.Equal(4, (int)TaskPriority.Background, "Background");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "EnumOrdering", "Lower numeric priority means higher priority", async ct =>
                {
                    Check.True((int)TaskPriority.Urgent < (int)TaskPriority.High, "Urgent < High");
                    Check.True((int)TaskPriority.High < (int)TaskPriority.Normal, "High < Normal");
                    Check.True((int)TaskPriority.Normal < (int)TaskPriority.Low, "Normal < Low");
                    Check.True((int)TaskPriority.Low < (int)TaskPriority.Background, "Low < Background");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "DetailsPrioritySettable", "TaskDetails.Priority is settable", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        TaskDetails details = queue.AddTask(Guid.NewGuid(), "P", new Dictionary<string, object>(), async token => await Task.Delay(10, token));
                        Check.Equal(0, details.Priority, "default priority");
                        details.Priority = (int)TaskPriority.High;
                        Check.Equal((int)TaskPriority.High, details.Priority, "updated priority");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "EnqueuePriorityStored", "EnqueueAsync stores the supplied priority", async ct =>
                {
                    int observed = -1;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskStarted += (s, d) => observed = d.Priority;
                        queue.Start();
                        await queue.EnqueueAsync("Pri", async token => await Task.Delay(30, token), priority: (int)TaskPriority.High);
                        Check.True(await Check.WaitUntilAsync(() => observed == (int)TaskPriority.High), $"priority should be stored (got {observed})");
                        queue.Stop();
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "Task Priority", cases);
        }
    }
}
