namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
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
        /// Task priority. Lower number = higher priority.
        /// Default: 0 (Normal priority).
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

        #endregion

        #region Private-Members

        private string _Name = null;
        private Func<CancellationToken, Task> _Function = null;
        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private Dictionary<string, object> _Metadata = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);

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
    }
}
