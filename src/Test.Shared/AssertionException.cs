namespace Test.Shared
{
    using System;

    /// <summary>
    /// Exception thrown when a Touchstone test assertion fails. Touchstone treats a
    /// thrown exception as a failed test, so all assertion helpers throw this type.
    /// </summary>
    public class AssertionException : Exception
    {
        /// <summary>
        /// Instantiate with a message describing the failed assertion.
        /// </summary>
        /// <param name="message">Human-readable description of the failure.</param>
        public AssertionException(string message)
            : base(message)
        {
        }
    }
}
