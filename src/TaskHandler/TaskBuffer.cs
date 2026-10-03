namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Holds tasks that have been accepted but not yet read by the task runner, and decides the order in which the
    /// runner receives them. Writes after <see cref="Close"/> fail; reads after close throw
    /// <see cref="System.Threading.Channels.ChannelClosedException"/> once empty.
    /// </summary>
    internal abstract class TaskBuffer
    {
        /// <summary>
        /// Capacity reported for telemetry and <see cref="TaskQueue.MaxQueueSize"/>. -1 for unbounded.
        /// </summary>
        internal abstract int Capacity { get; }

        /// <summary>
        /// Add a task without waiting.
        /// </summary>
        /// <param name="taskDetails">Task details.</param>
        /// <returns>Null when accepted; otherwise the bounded error type of the rejection (queue_full,
        /// queue_closed, or unclassified).</returns>
        internal abstract string TryWrite(TaskDetails taskDetails);

        /// <summary>
        /// Add a task, waiting for space when the buffer is full and its policy is to wait.
        /// </summary>
        /// <param name="taskDetails">Task details.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Null when accepted; otherwise the bounded error type of the rejection (queue_full or
        /// unclassified). Throws <see cref="System.Threading.Channels.ChannelClosedException"/> when closed and
        /// <see cref="OperationCanceledException"/> when the token is canceled.</returns>
        internal abstract ValueTask<string> WriteAsync(TaskDetails taskDetails, CancellationToken cancellationToken);

        /// <summary>
        /// Wait for and remove the next task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Next task.</returns>
        internal abstract ValueTask<TaskDetails> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stop accepting tasks, release any waiting writers, and return the tasks still held.
        /// </summary>
        /// <returns>Tasks still held.</returns>
        internal abstract List<TaskDetails> Close();
    }
}
