namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TaskHandler;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the TaskProgress value object: percentage math, validation, and formatting.
    /// </summary>
    public static class TaskProgressModelSuite
    {
        private const string Id = "TaskProgressModel";

        /// <summary>
        /// Build the TaskProgress model test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "PercentCalc", "PercentComplete computes correctly", async ct =>
                {
                    Check.Equal(50.0, new TaskProgress(5, 10).PercentComplete, "5/10");
                    Check.Equal(100.0, new TaskProgress(10, 10).PercentComplete, "10/10");
                    Check.Equal(0.0, new TaskProgress(0, 10).PercentComplete, "0/10");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "TotalZeroPercentZero", "Total of zero yields zero percent", async ct =>
                {
                    Check.Equal(0.0, new TaskProgress(0, 0).PercentComplete, "0/0");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NegativeCurrentThrows", "Negative current throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() => new TaskProgress(-1, 10), "current = -1");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "NegativeTotalThrows", "Negative total throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() => new TaskProgress(0, -1), "total = -1");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "CurrentExceedsTotalThrows", "Current exceeding total throws", async ct =>
                {
                    Check.Throws<ArgumentOutOfRangeException>(() => new TaskProgress(11, 10), "current > total");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ToStringWithMessage", "ToString includes the message", async ct =>
                {
                    string s = new TaskProgress(3, 10, "working").ToString();
                    Check.True(s.Contains("3/10"), "contains fraction");
                    Check.True(s.Contains("working"), "contains message");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "ToStringWithoutMessage", "ToString omits the trailing dash when no message", async ct =>
                {
                    string s = new TaskProgress(3, 10).ToString();
                    Check.True(s.Contains("3/10"), "contains fraction");
                    Check.False(s.Contains(" - "), "no message separator");
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "PropertiesStored", "Current, Total, and Message are stored", async ct =>
                {
                    TaskProgress p = new TaskProgress(4, 8, "half");
                    Check.Equal(4, p.Current, "Current");
                    Check.Equal(8, p.Total, "Total");
                    Check.Equal("half", p.Message, "Message");
                    await Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor(Id, "TaskProgress Model", cases);
        }
    }
}
