namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the TaskDetails value object: defaults and property validation.
    /// </summary>
    public static class TaskDetailsSuite
    {
        private const string Id = "TaskDetails";

        /// <summary>
        /// Build the TaskDetails test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "Defaults", "TaskDetails has sane defaults", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Check.True(details.Guid != Guid.Empty, "Guid should be assigned");
                    Check.Equal(0, details.Priority, "default priority");
                    Check.NotNull(details.Metadata, "Metadata not null");
                    Check.Equal(0, details.Metadata.Count, "Metadata empty by default");
                    Check.NotNull(details.TokenSource, "TokenSource not null");
                    Check.Equal(details.TokenSource.Token, details.Token, "Token derived from TokenSource");
                    Check.Null(details.Task, "Task null by default");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NullNameThrows", "Setting a null name throws", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Check.Throws<ArgumentNullException>(() => details.Name = null!, "Name = null");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "EmptyNameThrows", "Setting an empty name throws", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Check.Throws<ArgumentNullException>(() => details.Name = string.Empty, "Name = empty");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NullFunctionThrows", "Setting a null function throws", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Check.Throws<ArgumentNullException>(() => details.Function = null!, "Function = null");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NullMetadataResetsToEmpty", "Setting null metadata resets to an empty dictionary", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    details.Metadata = null!;
                    Check.NotNull(details.Metadata, "Metadata should not be null after setting null");
                    Check.Equal(0, details.Metadata.Count, "Metadata should be empty");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NullTokenSourceThrows", "Setting a null token source throws", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Check.Throws<ArgumentNullException>(() => details.TokenSource = null!, "TokenSource = null");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ValidAssignments", "Valid property assignments are stored", async ct =>
                {
                    TaskDetails details = new TaskDetails();
                    Guid g = Guid.NewGuid();
                    details.Guid = g;
                    details.Name = "worker";
                    details.Function = async token => await Task.Delay(1, token);
                    details.Priority = 3;
                    Check.Equal(g, details.Guid, "Guid");
                    Check.Equal("worker", details.Name, "Name");
                    Check.NotNull(details.Function, "Function");
                    Check.Equal(3, details.Priority, "Priority");
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "TaskDetails Model", cases);
        }
    }
}
