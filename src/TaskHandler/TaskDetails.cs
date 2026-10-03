namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Task details.
    /// </summary>
    public class TaskDetails
    {
        #region Public-Members

        /// <summary>
        /// GUID.
        /// Default: Guid.NewGuid().
        /// </summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-supplied name.
        /// Default: null.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// User-supplied metadata.
        /// Default: Empty dictionary.
        /// </summary>
        public Dictionary<string, object> Metadata
        {
            get
            {
                return _Metadata;
            }
            set
            {
                if (value == null) _Metadata = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
                else _Metadata = value;
            }
        }

        /// <summary>
        /// Action.
        /// Default: null.
        /// </summary>
        public Func<CancellationToken, Task> Function
        {
            get
            {
                return _Function;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Function));
                _Function = value;
            }
        }

        /// <summary>
        /// Task.
        /// Default: null.
        /// </summary>
        public Task Task { get; set; } = null;

        /// <summary>
        /// Token source.
        /// Default: new CancellationTokenSource().
        /// </summary>
        public CancellationTokenSource TokenSource
        {
            get
            {
                return _TokenSource;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(TokenSource));
                _TokenSource = value;
            }
        }

        /// <summary>
        /// Token.
        /// Initialized from TokenSource.Token in constructor.
        /// </summary>
        public CancellationToken Token { get; set; }

        /// <summary>
        /// Task priority. Lower number = higher priority (see <see cref="TaskPriority"/>).
        /// Informational: recorded for callers and telemetry, but it does not change execution order; tasks start
        /// in the order they were added.
        /// Default: 0.
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Timestamp when task was enqueued.
        /// </summary>
        internal DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when task started execution.
        /// Null if task has not started yet.
        /// </summary>
        internal DateTime? StartedAt { get; set; }

        /// <summary>
        /// Timestamp when the task was read from the queue by the task runner.
        /// Null if the task has not been dequeued yet.
        /// </summary>
        internal DateTime? DequeuedAt { get; set; }

        /// <summary>
        /// Callback invoked when the task ends canceled or is dropped, so that a result handle is always completed
        /// even if the task function never ran. Null when not needed.
        /// </summary>
        internal Action OnAbandoned { get; set; }

        /// <summary>
        /// Trace context captured at enqueue time; parent of the task job span.
        /// Default: default(ActivityContext), meaning no parent.
        /// </summary>
        internal ActivityContext ParentContext { get; set; }

        /// <summary>
        /// Task job span (enqueue to terminal state). Null when no listener is subscribed.
        /// </summary>
        internal Activity JobActivity { get; set; }

        /// <summary>
        /// Slot-wait stage span. Null when no listener is subscribed or once the slot is acquired.
        /// </summary>
        internal Activity SlotWaitActivity { get; set; }

        /// <summary>
        /// Execute stage span. Null when no listener is subscribed or once the task completes.
        /// </summary>
        internal Activity ExecuteActivity { get; set; }

        #endregion

        #region Private-Members

        private string _Name = null;
        private Func<CancellationToken, Task> _Function = null;
        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Dictionary<string, object> _Metadata = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
        private int _LeftQueue = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TaskDetails()
        {
            Token = TokenSource.Token;
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Claim the right to settle this task's departure from the queue (start, cancel before start, or drop).
        /// Thread-safe; returns true exactly once.
        /// </summary>
        /// <returns>True for the first caller only.</returns>
        internal bool TryClaim()
        {
            return Interlocked.Exchange(ref _LeftQueue, 1) == 0;
        }

        #endregion
    }
}
