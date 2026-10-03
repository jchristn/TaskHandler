namespace Test.Shared.Scheduling
{
    using System;
    using System.Threading;
    using QoSKit;

    /// <summary>
    /// Manually advanced clock for QoSKit queues, so time-dependent scheduling (priority aging) can be tested
    /// deterministically. Thread-safe.
    /// </summary>
    public sealed class ManualTimeProvider : IQoSTimeProvider
    {
        private static readonly DateTime _Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private long _Milliseconds = 0;

        /// <summary>
        /// Current UTC time: a fixed epoch plus the advanced milliseconds.
        /// </summary>
        public DateTime UtcNow
        {
            get
            {
                return _Epoch.AddMilliseconds(Interlocked.Read(ref _Milliseconds));
            }
        }

        /// <summary>
        /// Milliseconds advanced so far. Starts at 0.
        /// </summary>
        public long MonotonicMilliseconds
        {
            get
            {
                return Interlocked.Read(ref _Milliseconds);
            }
        }

        /// <summary>
        /// Move the clock forward.
        /// </summary>
        /// <param name="milliseconds">Milliseconds to advance. Minimum: 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when milliseconds is negative.</exception>
        public void Advance(long milliseconds)
        {
            if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            Interlocked.Add(ref _Milliseconds, milliseconds);
        }
    }
}
