namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Lightweight assertion helpers for Touchstone test descriptors. Each helper throws
    /// an <see cref="AssertionException"/> when the condition is not satisfied.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Assert that a condition is true.
        /// </summary>
        /// <param name="condition">Condition that must be true.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionException("Expected true: " + message);
        }

        /// <summary>
        /// Assert that a condition is false.
        /// </summary>
        /// <param name="condition">Condition that must be false.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void False(bool condition, string message)
        {
            if (condition) throw new AssertionException("Expected false: " + message);
        }

        /// <summary>
        /// Assert that two values are equal.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="context">Context describing what is being compared.</param>
        public static void Equal<T>(T expected, T actual, string context)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new AssertionException(
                    context + ": expected '" + FormatValue(expected) + "' but got '" + FormatValue(actual) + "'");
            }
        }

        /// <summary>
        /// Assert that an object reference is not null.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="context">Context describing the value.</param>
        public static void NotNull(object? value, string context)
        {
            if (value == null) throw new AssertionException(context + ": expected non-null value");
        }

        /// <summary>
        /// Assert that an object reference is null.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="context">Context describing the value.</param>
        public static void Null(object? value, string context)
        {
            if (value != null) throw new AssertionException(context + ": expected null value");
        }

        /// <summary>
        /// Assert that a numeric value is within an inclusive range.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="min">Inclusive minimum.</param>
        /// <param name="max">Inclusive maximum.</param>
        /// <param name="context">Context describing the value.</param>
        public static void InRange(double value, double min, double max, string context)
        {
            if (value < min || value > max)
            {
                throw new AssertionException(
                    context + ": expected value within [" + min + ", " + max + "] but got " + value);
            }
        }

        /// <summary>
        /// Assert that the supplied synchronous action throws an exception assignable to
        /// <typeparamref name="TException"/>.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Action expected to throw.</param>
        /// <param name="context">Context describing the operation.</param>
        public static void Throws<TException>(Action action, string context)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (ex is TException) return;
                throw new AssertionException(
                    context + ": expected " + typeof(TException).Name + " but caught " + ex.GetType().Name + " (" + ex.Message + ")");
            }

            throw new AssertionException(context + ": expected " + typeof(TException).Name + " but no exception was thrown");
        }

        /// <summary>
        /// Assert that the supplied asynchronous action throws an exception assignable to
        /// <typeparamref name="TException"/>.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Async action expected to throw.</param>
        /// <param name="context">Context describing the operation.</param>
        /// <returns>Task.</returns>
        public static async Task ThrowsAsync<TException>(Func<Task> action, string context)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is TException) return;
                throw new AssertionException(
                    context + ": expected " + typeof(TException).Name + " but caught " + ex.GetType().Name + " (" + ex.Message + ")");
            }

            throw new AssertionException(context + ": expected " + typeof(TException).Name + " but no exception was thrown");
        }

        /// <summary>
        /// Poll a condition until it becomes true or a timeout elapses.
        /// </summary>
        /// <param name="condition">Condition to evaluate repeatedly.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. Default: 10000.</param>
        /// <param name="pollMs">Polling interval, in milliseconds. Default: 20.</param>
        /// <returns>True if the condition became true within the timeout; otherwise false.</returns>
        public static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000, int pollMs = 20)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (condition()) return true;
                await Task.Delay(pollMs).ConfigureAwait(false);
                waited += pollMs;
            }

            return condition();
        }

        private static string FormatValue<T>(T value)
        {
            return value == null ? "null" : value.ToString() ?? "null";
        }
    }
}
