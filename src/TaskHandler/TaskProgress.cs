namespace TaskHandler
{
    using System;

    /// <summary>
    /// Represents progress information for a task.
    /// </summary>
    public class TaskProgress
    {
        /// <summary>
        /// Current progress value.
        /// </summary>
        public int Current { get; }

        /// <summary>
        /// Total expected value for completion.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Percentage of completion (0-100).
        /// </summary>
        public double PercentComplete
        {
            get
            {
                if (Total == 0) return 0;
                return ((double)Current / Total) * 100.0;
            }
        }

        /// <summary>
        /// Optional message describing current progress state.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Create a new TaskProgress instance.
        /// </summary>
        /// <param name="current">Current progress value.</param>
        /// <param name="total">Total expected value.</param>
        /// <param name="message">Optional progress message.</param>
        public TaskProgress(int current, int total, string message = null)
        {
            if (current < 0) throw new ArgumentOutOfRangeException(nameof(current), "Current must be non-negative");
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "Total must be non-negative");
            if (current > total) throw new ArgumentOutOfRangeException(nameof(current), "Current cannot exceed Total");

            Current = current;
            Total = total;
            Message = message;
        }

        /// <summary>
        /// Get a human-readable representation of the progress.
        /// </summary>
        /// <returns>Formatted progress string.</returns>
        public override string ToString()
        {
            string progressStr = $"{Current}/{Total} ({PercentComplete:F1}%)";
            if (!String.IsNullOrEmpty(Message))
            {
                return $"{progressStr} - {Message}";
            }
            return progressStr;
        }
    }
}
