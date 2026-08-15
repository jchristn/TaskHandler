namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Central source of truth for the TaskHandler test corpus. Exposes every
    /// <see cref="TestSuiteDescriptor"/> so any Touchstone runner (CLI, xUnit, NUnit)
    /// can execute the identical set of test cases.
    /// </summary>
    public static class TaskHandlerSuites
    {
        /// <summary>
        /// All test suites covering the TaskHandler library.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All { get; } = new List<TestSuiteDescriptor>
        {
            ConstructionSuite.Build(),
            PropertiesSuite.Build(),
            ExecutionSuite.Build(),
            ConcurrencySuite.Build(),
            CancellationSuite.Build(),
            LifecycleSuite.Build(),
            EventsSuite.Build(),
            TaskHandleSuite.Build(),
            StatisticsSuite.Build(),
            ProgressSuite.Build(),
            PrioritySuite.Build(),
            TaskInfoSuite.Build(),
            TaskProgressModelSuite.Build(),
            TaskDetailsSuite.Build(),
            TimeoutSuite.Build()
        };

        /// <summary>
        /// Convenience factory for a non-skipped test case descriptor.
        /// </summary>
        /// <param name="suiteId">Parent suite identifier.</param>
        /// <param name="caseId">Case identifier within the suite.</param>
        /// <param name="displayName">Human-readable case name.</param>
        /// <param name="executeAsync">Async test body.</param>
        /// <returns>Configured <see cref="TestCaseDescriptor"/>.</returns>
        public static TestCaseDescriptor Case(
            string suiteId,
            string caseId,
            string displayName,
            Func<CancellationToken, Task> executeAsync)
        {
            return new TestCaseDescriptor(suiteId, caseId, displayName, executeAsync);
        }
    }
}
