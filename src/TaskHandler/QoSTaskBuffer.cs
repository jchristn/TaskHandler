namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using QoSKit;

    /// <summary>
    /// Buffer backed by a caller-supplied QoSKit queue, which decides the order in which tasks start (strict
    /// priority, weighted fair queuing, and so on). An incoming task the QoSKit queue refuses (full under Reject or
    /// DropNewest, or an unknown class) is reported to the writer as a rejection. An already-accepted task it
    /// evicts (DropOldest) is reported through the drop callback so it can be completed.
    /// </summary>
    internal sealed class QoSTaskBuffer : TaskBuffer
    {
        private readonly IQoSQueue<TaskDetails> _Queue;
        private readonly Action<TaskDetails, string> _OnDropped;
        private int _Closed = 0;

        // QoSKit raises ItemDropped synchronously on the enqueuing thread before TryEnqueue returns false, which is
        // how a refused incoming task is told apart from an evicted resident one.
        [ThreadStatic]
        private static DropReason? _IncomingDropReason;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="queue">QoSKit queue. Owned by this buffer from now on.</param>
        /// <param name="onDropped">Invoked with the task and a bounded error type when the queue discards a task.</param>
        internal QoSTaskBuffer(IQoSQueue<TaskDetails> queue, Action<TaskDetails, string> onDropped)
        {
            _Queue = queue;
            _OnDropped = onDropped;
            _Queue.ItemDropped += HandleItemDropped;
        }

        /// <inheritdoc/>
        internal override int Capacity
        {
            get
            {
                return _Queue.MaxDepth > 0 ? _Queue.MaxDepth : -1;
            }
        }

        /// <summary>
        /// The QoSKit queue.
        /// </summary>
        internal IQoSQueue<TaskDetails> Queue
        {
            get
            {
                return _Queue;
            }
        }

        /// <inheritdoc/>
        internal override string TryWrite(TaskDetails taskDetails)
        {
            if (Volatile.Read(ref _Closed) != 0) return TaskHandlerTelemetryNames.ErrorQueueClosed;

            bool accepted;
            _IncomingDropReason = null;
            try
            {
                accepted = _Queue.TryEnqueue(taskDetails);
            }
            catch (ObjectDisposedException)
            {
                return TaskHandlerTelemetryNames.ErrorQueueClosed;
            }
            catch (UnknownClassificationException)
            {
                return TaskHandlerTelemetryNames.ErrorUnclassified;
            }

            DropReason? reason = _IncomingDropReason;
            _IncomingDropReason = null;

            if (accepted) return null;
            if (Volatile.Read(ref _Closed) != 0) return TaskHandlerTelemetryNames.ErrorQueueClosed;
            if (reason == DropReason.UnknownClass || reason == DropReason.Unroutable) return TaskHandlerTelemetryNames.ErrorUnclassified;
            return TaskHandlerTelemetryNames.ErrorQueueFull;
        }

        /// <inheritdoc/>
        internal override async ValueTask<string> WriteAsync(TaskDetails taskDetails, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _Closed) != 0) throw new ChannelClosedException();
            cancellationToken.ThrowIfCancellationRequested();

            QoSQueueBase<TaskDetails> waitable = _Queue as QoSQueueBase<TaskDetails>;
            if (waitable == null || waitable.OverflowPolicy != OverflowPolicy.Block)
            {
                string error = TryWrite(taskDetails);
                if (error == TaskHandlerTelemetryNames.ErrorQueueClosed) throw new ChannelClosedException();
                return error;
            }

            try
            {
                await waitable.EnqueueAsync(taskDetails, cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (UnknownClassificationException)
            {
                return TaskHandlerTelemetryNames.ErrorUnclassified;
            }
            catch (ObjectDisposedException)
            {
                throw new ChannelClosedException();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // QoSKit cancels waiting producers when it is disposed.
                throw new ChannelClosedException();
            }
        }

        /// <inheritdoc/>
        internal override async ValueTask<TaskDetails> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _Queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw new ChannelClosedException();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ChannelClosedException();
            }
        }

        /// <inheritdoc/>
        internal override List<TaskDetails> Close()
        {
            if (Interlocked.Exchange(ref _Closed, 1) != 0) return new List<TaskDetails>();

            // Snapshot everything resident (including items a policer is currently holding back), then dispose, which
            // also releases waiting producers and consumers.
            List<TaskDetails> remaining = new List<TaskDetails>();
            try
            {
                remaining.AddRange(_Queue.ToArray());
            }
            catch (ObjectDisposedException)
            {
            }

            _Queue.ItemDropped -= HandleItemDropped;
            _Queue.Dispose();
            return remaining;
        }

        private void HandleItemDropped(object sender, QoSDropEventArgs<TaskDetails> e)
        {
            // Only an eviction removes a task that was already accepted. Every other reason refuses the incoming
            // task, which the writer reports as a rejection.
            if (e.Reason == DropReason.Oldest)
            {
                _OnDropped(e.Item, TaskHandlerTelemetryNames.ErrorQueueFull);
            }
            else
            {
                _IncomingDropReason = e.Reason;
            }
        }
    }
}
