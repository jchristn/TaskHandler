namespace Test.Shared.Scheduling
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Records the order and time at which task functions start, for scheduling assertions. Thread-safe.
    /// </summary>
    public sealed class StartLog
    {
        private readonly object _Lock = new object();
        private readonly List<string> _Names = new List<string>();
        private readonly List<double> _Milliseconds = new List<double>();
        private readonly Stopwatch _Clock = Stopwatch.StartNew();

        /// <summary>
        /// Number of recorded starts.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_Lock) return _Names.Count;
            }
        }

        /// <summary>
        /// Record that a task started.
        /// </summary>
        /// <param name="name">Task name. Must not be null.</param>
        public void Record(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            lock (_Lock)
            {
                _Names.Add(name);
                _Milliseconds.Add(_Clock.Elapsed.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Snapshot of recorded names in start order.
        /// </summary>
        /// <returns>Names.</returns>
        public List<string> Names()
        {
            lock (_Lock) return new List<string>(_Names);
        }

        /// <summary>
        /// Start times, in milliseconds since this log was created, of tasks whose name starts with the prefix.
        /// </summary>
        /// <param name="prefix">Name prefix. Must not be null.</param>
        /// <returns>Start times in start order.</returns>
        public List<double> TimesOf(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            lock (_Lock)
            {
                return _Names.Select((n, i) => n.StartsWith(prefix, StringComparison.Ordinal) ? _Milliseconds[i] : -1)
                    .Where(t => t >= 0)
                    .ToList();
            }
        }

        /// <summary>
        /// Count of tasks among the first <paramref name="take"/> starts whose name starts with the prefix.
        /// </summary>
        /// <param name="prefix">Name prefix. Must not be null.</param>
        /// <param name="take">Number of leading starts to inspect. Minimum: 0.</param>
        /// <returns>Count.</returns>
        public int CountInFirst(string prefix, int take)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (take < 0) throw new ArgumentOutOfRangeException(nameof(take));
            return Names().Take(take).Count(n => n.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Wait until at least <paramref name="count"/> starts are recorded.
        /// </summary>
        /// <param name="count">Expected count.</param>
        /// <param name="timeoutMs">Timeout in milliseconds. Default: 10000.</param>
        /// <returns>True if reached before the timeout.</returns>
        public Task<bool> WaitForAsync(int count, int timeoutMs = 10000)
        {
            return Check.WaitUntilAsync(() => Count >= count, timeoutMs);
        }

        /// <summary>
        /// Comma-separated names in start order, for failure messages.
        /// </summary>
        /// <returns>Joined names.</returns>
        public override string ToString()
        {
            return String.Join(",", Names());
        }
    }
}
