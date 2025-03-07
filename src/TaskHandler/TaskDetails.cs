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
        /// </summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-supplied name.
        /// </summary>
        public string Name { get; set; } = null;

        /// <summary>
        /// User-supplied metadata.
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
        /// </summary>
        public Func<CancellationToken, Task> Function { get; set; }

        /// <summary>
        /// Task.
        /// </summary>
        public Task Task { get; set; }

        /// <summary>
        /// Token source.
        /// </summary>
        public CancellationTokenSource TokenSource { get; set; } = new CancellationTokenSource();

        /// <summary>
        /// Token.
        /// </summary>
        public CancellationToken Token { get; set; }

        #endregion

        #region Private-Members

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

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
