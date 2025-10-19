namespace TaskHandler
{
    using System;

    /// <summary>
    /// Configuration options for TaskQueue.
    /// </summary>
    public class TaskQueueOptions
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of tasks that can run concurrently.
        /// Default: 32. Minimum: 1.
        /// </summary>
        public int MaxConcurrentTasks
        {
            get
            {
                return _MaxConcurrentTasks;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTasks), "MaxConcurrentTasks must be at least 1");
                _MaxConcurrentTasks = value;
            }
        }

        /// <summary>
        /// Maximum queue size. -1 for unbounded.
        /// Default: -1 (unbounded). Minimum: -1 or greater than 0.
        /// </summary>
        public int MaxQueueSize
        {
            get
            {
                return _MaxQueueSize;
            }
            set
            {
                if (value < -1 || value == 0) throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), "MaxQueueSize must be -1 (unbounded) or greater than 0");
                _MaxQueueSize = value;
            }
        }

        /// <summary>
        /// Logger callback.
        /// Default: null.
        /// </summary>
        public Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Event fired when a task is added.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskAdded { get; set; } = null;

        /// <summary>
        /// Event fired when a task starts.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskStarted { get; set; } = null;

        /// <summary>
        /// Event fired when a task completes successfully.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFinished { get; set; } = null;

        /// <summary>
        /// Event fired when a task faults.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFaulted { get; set; } = null;

        /// <summary>
        /// Event fired when a task is canceled.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskCanceled { get; set; } = null;

        /// <summary>
        /// Event fired when queue starts processing.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStarted { get; set; } = null;

        /// <summary>
        /// Event fired when queue stops processing.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStopped { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxConcurrentTasks = 32;
        private int _MaxQueueSize = -1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Configuration options for TaskQueue.
        /// </summary>
        public TaskQueueOptions()
        {

        }

        #endregion
    }
}
