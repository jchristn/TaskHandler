namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for GetRunningTasksInfo and the read-only TaskInfo snapshot.
    /// </summary>
    public static class TaskInfoSuite
    {
        private const string Id = "TaskInfo";

        /// <summary>
        /// Build the task info test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "EmptyWhenIdle", "GetRunningTasksInfo is empty when idle", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue())
                    {
                        IReadOnlyCollection<TaskInfo> info = queue.GetRunningTasksInfo();
                        Check.Equal(0, info.Count, "running info count when idle");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ReflectsRunningCount", "GetRunningTasksInfo reflects running tasks", async ct =>
                {
                    using (TaskQueue queue = new TaskQueue(2))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("Task1", async token => await Task.Delay(400, token));
                        await queue.EnqueueAsync("Task2", async token => await Task.Delay(400, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.GetRunningTasksInfo().Count == 2), "two running tasks");
                        IReadOnlyCollection<TaskInfo> info = queue.GetRunningTasksInfo();
                        TaskInfo first = info.First();
                        Check.True(first.Name == "Task1" || first.Name == "Task2", "task name present");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "SnapshotHasMetadataAndPriority", "TaskInfo snapshot carries metadata and priority", async ct =>
                {
                    TaskInfo? observed = null;
                    Dictionary<string, object> md = new Dictionary<string, object> { { "region", "us" } };
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        queue.AddTask(Guid.NewGuid(), "InfoTask", md, async token => await Task.Delay(300, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.GetRunningTasksInfo().Count == 1), "task running");
                        observed = queue.GetRunningTasksInfo().First();
                        queue.Stop();
                    }
                    Check.NotNull(observed, "observed TaskInfo");
                    Check.Equal("InfoTask", observed!.Name, "name");
                    Check.True(observed.Metadata.ContainsKey("region"), "metadata carried");
                    Check.Equal(0, observed.Priority, "priority default");
                }),

                TaskHandlerSuites.Case(Id, "MetadataIsCopy", "TaskInfo metadata is a snapshot copy", async ct =>
                {
                    Dictionary<string, object> md = new Dictionary<string, object> { { "k", "v" } };
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Start();
                        queue.AddTask(Guid.NewGuid(), "CopyTask", md, async token => await Task.Delay(300, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.GetRunningTasksInfo().Count == 1), "task running");
                        TaskInfo info = queue.GetRunningTasksInfo().First();

                        // Mutating the source dictionary after the snapshot must not change the snapshot.
                        md["k2"] = "v2";
                        Check.False(info.Metadata.ContainsKey("k2"), "snapshot should not see later mutations");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "ConstructorStoresValues", "TaskInfo constructor stores supplied values", async ct =>
                {
                    Guid id = Guid.NewGuid();
                    Dictionary<string, object> md = new Dictionary<string, object> { { "a", 1 } };
                    TaskInfo info = new TaskInfo(id, "N", TaskStatus.Running, 2, md);
                    Check.Equal(id, info.Id, "Id");
                    Check.Equal("N", info.Name, "Name");
                    Check.Equal(TaskStatus.Running, info.Status, "Status");
                    Check.Equal(2, info.Priority, "Priority");
                    Check.True(info.Metadata.ContainsKey("a"), "Metadata");
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "TaskInfo Snapshots", cases);
        }
    }
}
