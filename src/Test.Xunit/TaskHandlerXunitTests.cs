namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using global::Xunit;

    /// <summary>
    /// xUnit adapter that runs the shared Touchstone test corpus, one xUnit test per descriptor.
    /// </summary>
    public sealed class TaskHandlerXunitTests
    {
        /// <summary>
        /// Theory data yielding one row per non-skipped test case in every shared suite.
        /// </summary>
        public static TouchstoneTheoryData TestCases => new TouchstoneTheoryData(TaskHandlerSuites.All);

        /// <summary>
        /// Execute a single shared test case descriptor.
        /// </summary>
        /// <param name="testCase">Test case descriptor supplied by <see cref="TestCases"/>.</param>
        /// <returns>Task.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
