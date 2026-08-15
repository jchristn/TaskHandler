namespace Test.Automated
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Touchstone CLI runner for the TaskHandler test corpus. Executes every suite defined in
    /// <see cref="TaskHandlerSuites.All"/> and returns a CI-friendly exit code (0 = pass, 1 = fail).
    /// Pass an optional first argument to export JSON results to that path.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Optional: args[0] is a path for JSON result export.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = args.Length > 0 ? args[0] : null;
            return await ConsoleRunner.RunAsync(TaskHandlerSuites.All, null, resultsPath, CancellationToken.None);
        }
    }
}
