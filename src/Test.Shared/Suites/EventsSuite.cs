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
    /// Tests for the queue lifecycle and task events.
    /// </summary>
    public static class EventsSuite
    {
        private const string Id = "Events";

        /// <summary>
        /// Build the events test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "OnTaskAddedFires", "OnTaskAdded fires when a task is enqueued", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskAdded += (s, d) => fired = true;
                        queue.AddTask(Guid.NewGuid(), "A", new Dictionary<string, object>(), async token => await Task.Delay(10, token));
                        Check.True(fired, "OnTaskAdded should fire");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "OnTaskStartedFires", "OnTaskStarted fires when a task begins", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskStarted += (s, d) => fired = true;
                        queue.AddTask(Guid.NewGuid(), "S", new Dictionary<string, object>(), async token => await Task.Delay(30, token));
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnTaskStarted should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "OnTaskFinishedFires", "OnTaskFinished fires on successful completion", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskFinished += (s, d) => fired = true;
                        queue.AddTask(Guid.NewGuid(), "F", new Dictionary<string, object>(), async token => await Task.Delay(30, token));
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnTaskFinished should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "OnTaskFaultedFires", "OnTaskFaulted fires when a task throws", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskFaulted += (s, d) => fired = true;
                        queue.AddTask(Guid.NewGuid(), "Fault", new Dictionary<string, object>(), async token =>
                        {
                            await Task.Delay(20, token);
                            throw new InvalidOperationException("boom");
                        });
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnTaskFaulted should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "OnTaskCanceledFires", "OnTaskCanceled fires when a task is canceled", async ct =>
                {
                    bool fired = false;
                    Guid guid = Guid.NewGuid();
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskCanceled += (s, d) => { if (d.Guid == guid) fired = true; };
                        queue.AddTask(guid, "Cancel", new Dictionary<string, object>(), async token => await Task.Delay(5000, token));
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "task should start");
                        queue.Stop(guid);
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnTaskCanceled should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "OnProcessingStartedFires", "OnProcessingStarted fires on Start", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnProcessingStarted += (s, e) => fired = true;
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnProcessingStarted should fire");
                        queue.Stop();
                    }
                }),

                TaskHandlerSuites.Case(Id, "OnProcessingStoppedFires", "OnProcessingStopped fires on Stop", async ct =>
                {
                    bool fired = false;
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnProcessingStopped += (s, e) => fired = true;
                        queue.Start();
                        queue.Stop();
                        Check.True(await Check.WaitUntilAsync(() => fired), "OnProcessingStopped should fire");
                    }
                }),

                TaskHandlerSuites.Case(Id, "EventHandlerExceptionCaught", "An exception thrown by an event handler is caught and logged", async ct =>
                {
                    bool fired = false;
                    List<string> logs = new List<string>();
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.Logger = msg => { lock (logs) { logs.Add(msg); } };
                        queue.OnTaskAdded += (s, d) =>
                        {
                            fired = true;
                            throw new Exception("handler failure marker");
                        };
                        queue.AddTask(Guid.NewGuid(), "E", new Dictionary<string, object>(), async token => await Task.Delay(20, token));
                        queue.Start();
                        await Task.Delay(150);
                        queue.Stop();
                    }
                    Check.True(fired, "handler should have fired");
                    bool logged;
                    lock (logs) { logged = logs.Any(m => m.Contains("handler failure marker")); }
                    Check.True(logged, "handler exception should be logged");
                }),

                TaskHandlerSuites.Case(Id, "MetadataPreservedInEvent", "Task metadata is visible in the started event", async ct =>
                {
                    string? observed = null;
                    Dictionary<string, object> md = new Dictionary<string, object> { { "TestKey", "TestValue123" } };
                    using (TaskQueue queue = new TaskQueue())
                    {
                        queue.OnTaskStarted += (s, d) =>
                        {
                            if (d.Metadata != null && d.Metadata.ContainsKey("TestKey"))
                            {
                                observed = d.Metadata["TestKey"] as string;
                            }
                        };
                        queue.AddTask(Guid.NewGuid(), "M", md, async token => await Task.Delay(30, token));
                        queue.Start();
                        Check.True(await Check.WaitUntilAsync(() => observed != null), "metadata should be observed");
                        queue.Stop();
                    }
                    Check.Equal("TestValue123", observed!, "observed metadata value");
                })
            };

            return new TestSuiteDescriptor(Id, "Events", cases);
        }
    }
}
