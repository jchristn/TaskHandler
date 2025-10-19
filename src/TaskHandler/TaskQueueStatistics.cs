namespace TaskHandler
{
    using System;

    /// <summary>
    /// Statistics and metrics for TaskQueue operations.
    /// </summary>
    public class TaskQueueStatistics
    {
        #region Public-Members

        /// <summary>
        /// Total number of tasks enqueued since queue creation.
        /// Default: 0.
        /// </summary>
        public long TotalEnqueued { get; internal set; } = 0;

        /// <summary>
        /// Total number of tasks completed successfully.
        /// Default: 0.
        /// </summary>
        public long TotalCompleted { get; internal set; } = 0;

        /// <summary>
        /// Total number of tasks that failed (faulted).
        /// Default: 0.
        /// </summary>
        public long TotalFailed { get; internal set; } = 0;

        /// <summary>
        /// Total number of tasks that were canceled.
        /// Default: 0.
        /// </summary>
        public long TotalCanceled { get; internal set; } = 0;

        /// <summary>
        /// Current number of tasks waiting in the queue.
        /// Default: 0.
        /// </summary>
        public int CurrentQueueDepth { get; internal set; } = 0;

        /// <summary>
        /// Current number of tasks actively running.
        /// Default: 0.
        /// </summary>
        public int CurrentRunningCount { get; internal set; } = 0;

        /// <summary>
        /// Average execution time across completed tasks.
        /// Default: TimeSpan.Zero.
        /// </summary>
        public TimeSpan AverageExecutionTime { get; internal set; } = TimeSpan.Zero;

        /// <summary>
        /// Average time tasks spent waiting in queue before execution.
        /// Default: TimeSpan.Zero.
        /// </summary>
        public TimeSpan AverageWaitTime { get; internal set; } = TimeSpan.Zero;

        /// <summary>
        /// Timestamp when the most recent task started execution.
        /// Default: null.
        /// </summary>
        public DateTime? LastTaskStarted { get; internal set; } = null;

        /// <summary>
        /// Timestamp when the most recent task completed.
        /// Default: null.
        /// </summary>
        public DateTime? LastTaskCompleted { get; internal set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new TaskQueueStatistics instance.
        /// </summary>
        public TaskQueueStatistics()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get a human-readable summary of the statistics.
        /// </summary>
        /// <returns>Formatted summary string.</returns>
        public override string ToString()
        {
            return $"TaskQueueStatistics: Enqueued={TotalEnqueued}, Completed={TotalCompleted}, " +
                   $"Failed={TotalFailed}, Canceled={TotalCanceled}, " +
                   $"Running={CurrentRunningCount}, Queued={CurrentQueueDepth}, " +
                   $"AvgExecTime={AverageExecutionTime.TotalMilliseconds:F2}ms, " +
                   $"AvgWaitTime={AverageWaitTime.TotalMilliseconds:F2}ms";
        }

        #endregion
    }
}
