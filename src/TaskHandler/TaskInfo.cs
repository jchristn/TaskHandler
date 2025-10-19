namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Read-only snapshot of task information.
    /// </summary>
    public class TaskInfo
    {
        /// <summary>
        /// Task identifier.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Task name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Task status.
        /// </summary>
        public TaskStatus Status { get; }

        /// <summary>
        /// Task priority. Lower number = higher priority.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Read-only copy of task metadata.
        /// </summary>
        public IReadOnlyDictionary<string, object> Metadata { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Task identifier.</param>
        /// <param name="name">Task name.</param>
        /// <param name="status">Task status.</param>
        /// <param name="priority">Task priority.</param>
        /// <param name="metadata">Task metadata.</param>
        public TaskInfo(
            Guid id,
            string name,
            TaskStatus status,
            int priority,
            IReadOnlyDictionary<string, object> metadata)
        {
            Id = id;
            Name = name;
            Status = status;
            Priority = priority;
            Metadata = metadata;
        }
    }
}
