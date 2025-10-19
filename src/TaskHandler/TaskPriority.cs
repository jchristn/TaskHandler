namespace TaskHandler
{
    /// <summary>
    /// Standard task priorities. Lower number = higher priority.
    /// </summary>
    public enum TaskPriority
    {
        /// <summary>
        /// Urgent priority (0).
        /// </summary>
        Urgent = 0,

        /// <summary>
        /// High priority (1).
        /// </summary>
        High = 1,

        /// <summary>
        /// Normal priority (2).
        /// </summary>
        Normal = 2,

        /// <summary>
        /// Low priority (3).
        /// </summary>
        Low = 3,

        /// <summary>
        /// Background priority (4).
        /// </summary>
        Background = 4
    }
}
