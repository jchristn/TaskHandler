namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Default FIFO buffer backed by a System.Threading.Channels channel. A bounded channel waits for space on
    /// asynchronous writes and rejects synchronous writes when full.
    /// </summary>
    internal sealed class ChannelTaskBuffer : TaskBuffer
    {
        private readonly Channel<TaskDetails> _Channel;
        private readonly int _Capacity;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="capacity">Maximum number of buffered tasks, or -1 for unbounded.</param>
        internal ChannelTaskBuffer(int capacity)
        {
            _Capacity = capacity;

            // SingleReader is false because Close() drains the channel while a stopping runner may still be reading.
            if (capacity > 0)
            {
                _Channel = Channel.CreateBounded<TaskDetails>(new BoundedChannelOptions(capacity)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            }
            else
            {
                _Channel = Channel.CreateUnbounded<TaskDetails>(new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false
                });
            }
        }

        /// <inheritdoc/>
        internal override int Capacity
        {
            get
            {
                return _Capacity;
            }
        }

        /// <inheritdoc/>
        internal override string TryWrite(TaskDetails taskDetails)
        {
            if (_Channel.Writer.TryWrite(taskDetails)) return null;

            // An unbounded channel only refuses writes once completed.
            if (_Capacity <= 0 || _Channel.Reader.Completion.IsCompleted) return TaskHandlerTelemetryNames.ErrorQueueClosed;
            return TaskHandlerTelemetryNames.ErrorQueueFull;
        }

        /// <inheritdoc/>
        internal override async ValueTask<string> WriteAsync(TaskDetails taskDetails, CancellationToken cancellationToken)
        {
            await _Channel.Writer.WriteAsync(taskDetails, cancellationToken).ConfigureAwait(false);
            return null;
        }

        /// <inheritdoc/>
        internal override ValueTask<TaskDetails> ReadAsync(CancellationToken cancellationToken)
        {
            return _Channel.Reader.ReadAsync(cancellationToken);
        }

        /// <inheritdoc/>
        internal override List<TaskDetails> Close()
        {
            _Channel.Writer.TryComplete();

            List<TaskDetails> remaining = new List<TaskDetails>();
            while (_Channel.Reader.TryRead(out TaskDetails taskDetails))
            {
                remaining.Add(taskDetails);
            }

            return remaining;
        }
    }
}
