namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// NUnit adapter that runs the shared Touchstone test corpus, one NUnit test per descriptor.
    /// </summary>
    [TestFixture]
    public sealed class TaskHandlerNunitTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(TaskHandlerSuites.All);
        }

        /// <summary>
        /// Execute a single shared test case descriptor.
        /// </summary>
        /// <param name="testCase">Test case descriptor supplied by the source.</param>
        /// <returns>Task.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
