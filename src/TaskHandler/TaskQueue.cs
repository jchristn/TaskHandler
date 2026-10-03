namespace TaskHandler
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using QoSKit;

    /// <summary>
    /// Task queue.
    /// By default tasks start in the order they were added (FIFO). To start them by priority, weighted fairness, or
    /// another discipline, construct the queue with a QoSKit scheduler (see <see cref="Scheduler"/>).
    /// Emits metrics and traces through the BCL Meter and ActivitySource named "TaskHandler"
    /// (see <see cref="TaskHandlerTelemetryNames"/> and TELEMETRY.md). Emission is best-effort, never throws,
    /// and costs effectively nothing when no listener is subscribed.
    /// </summary>
    public class TaskQueue : IDisposable, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Method to invoke to send log messages.
        /// Default: null.
        /// </summary>
        public Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Maximum number of concurrent tasks.
        /// Can be changed at any time. Raising it lets waiting tasks start immediately; lowering it never interrupts
        /// running tasks, and takes full effect as they finish.
        /// Default: 32. Minimum: 1.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1.</exception>
        public int MaxConcurrentTasks
        {
            get
            {
                return _MaxConcurrentTasks;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTasks));

                lock (_StateLock)
                {
                    int delta = value - _MaxConcurrentTasks;
                    _MaxConcurrentTasks = value;
                    if (_concurrencySemaphore == null || delta == 0) return;

                    if (delta > 0)
                    {
                        // Cancel outstanding withheld slots first, then release the rest.
                        int remaining = delta;
                        while (remaining > 0)
                        {
                            int debt = Volatile.Read(ref _SlotDebt);
                            if (debt <= 0) break;
                            int pay = Math.Min(debt, remaining);
                            if (Interlocked.CompareExchange(ref _SlotDebt, debt - pay, debt) == debt) remaining -= pay;
                        }

                        if (remaining > 0) _concurrencySemaphore.Release(remaining);
                    }
                    else
                    {
                        // Take free slots now; withhold the rest as running tasks release them (see ReleaseSlot), so a
                        // freed slot can never reach a waiting task first.
                        int needed = -delta;
                        while (needed > 0 && _concurrencySemaphore.Wait(0)) needed--;
                        if (needed > 0) Interlocked.Add(ref _SlotDebt, needed);
                    }
                }
            }
        }

        /// <summary>
        /// Maximum number of tasks that can wait to start. -1 for unbounded.
        /// Can only be changed before the queue is first started or given a task; afterwards a different value
        /// throws, because the existing queue cannot be resized. When the queue was created with a
        /// <see cref="Scheduler"/>, this reports the scheduler's MaxDepth (-1 when unbounded) and cannot be set.
        /// Default: -1 (unbounded).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set to 0 or below -1.</exception>
        /// <exception cref="InvalidOperationException">Thrown when changed after the queue was started or given a
        /// task, or when the queue uses a <see cref="Scheduler"/>.</exception>
        public int MaxQueueSize
        {
            get
            {
                return _Buffer != null ? _Buffer.Capacity : -1;
            }
            set
            {
                if (value < -1 || value == 0) throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), "MaxQueueSize must be -1 (unbounded) or greater than 0");

                lock (_StateLock)
                {
                    if (_Buffer is QoSTaskBuffer)
                        throw new InvalidOperationException("MaxQueueSize cannot be set on a queue that uses a QoSKit scheduler; configure the scheduler's MaxDepth and OverflowPolicy instead.");
                    if (_Buffer != null && _Buffer.Capacity == value) return;
                    if (_InUse)
                        throw new InvalidOperationException("MaxQueueSize can only be changed before the queue is first started or given a task.");

                    _Buffer = new ChannelTaskBuffer(value);
                }
            }
        }

        /// <summary>
        /// The QoSKit queue that decides which waiting task starts next, or null when the queue uses the default FIFO
        /// order. Set through the <see cref="TaskQueue(IQoSQueue{TaskDetails}, int)"/> constructor or
        /// <see cref="TaskQueueOptions.Scheduler"/>; the TaskQueue owns it and disposes it. Use it for inspection
        /// (Count, Statistics) only: enqueuing or dequeuing on it directly bypasses the TaskQueue.
        /// </summary>
        public IQoSQueue<TaskDetails> Scheduler
        {
            get
            {
                QoSTaskBuffer qos = _Buffer as QoSTaskBuffer;
                return qos != null ? qos.Queue : null;
            }
        }

        /// <summary>
        /// Queue name, reported as the taskhandler.queue.name label on every TaskHandler metric and span.
        /// Must be low-cardinality (a fixed name per logical queue, such as "ingest" or "email"); never use ids or
        /// user input. Queues that share a name are aggregated together in the observable gauges.
        /// Default: "default". Must not be null or empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
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
        /// Number of running tasks.
        /// </summary>
        public int RunningCount
        {
            get
            {
                return _RunningTasks.Count;
            }
        }

        /// <summary>
        /// Running tasks dictionary.
        /// </summary>
        public ConcurrentDictionary<Guid, TaskDetails> RunningTasks
        {
            get
            {
                return _RunningTasks;
            }
        }

        /// <summary>
        /// Number of tasks accepted but not yet started, including tasks held while the queue is stopped.
        /// </summary>
        public int QueuedCount
        {
            get
            {
                return _queuedCount;
            }
        }

        /// <summary>
        /// Event to fire when a task is added.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskAdded { get; set; } = null;

        /// <summary>
        /// Event to fire when a task is started.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskStarted { get; set; } = null;

        /// <summary>
        /// Event to fire when a task ends canceled: its function observed cancellation, it was canceled with
        /// <see cref="Stop(Guid)"/> before it started, or it was dropped because the queue was disposed before it
        /// started. Each task raises exactly one of <see cref="OnTaskFinished"/>, <see cref="OnTaskFaulted"/>, or
        /// OnTaskCanceled. A task whose function catches cancellation and returns normally raises OnTaskFinished.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskCanceled { get; set; } = null;

        /// <summary>
        /// Event to fire when a task's function throws (including a timeout from EnqueueAsync).
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFaulted { get; set; } = null;

        /// <summary>
        /// Event to fire when a task's function completes successfully.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFinished { get; set; } = null;

        /// <summary>
        /// Event to fire when processing starts.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStarted { get; set; } = null;

        /// <summary>
        /// Event to fire when processing stops: once per <see cref="Stop()"/>, on <see cref="Dispose"/> of a started
        /// queue, or if the background runner fails.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStopped { get; set; } = null;

        /// <summary>
        /// True while the queue is started (not stopped or disposed) and its background runner is active.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                Task runner = _TaskRunner;
                return _IsStarted && !_IsDisposed && runner != null && !runner.IsCompleted;
            }
        }

        /// <summary>
        /// True while the queue is started and not disposed. Used by the queue-processing gauge.
        /// </summary>
        internal bool IsProcessing
        {
            get
            {
                return _IsStarted && !_IsDisposed;
            }
        }

        /// <summary>
        /// Unix time in seconds of the most recent successful task completion, or 0 if none.
        /// </summary>
        internal double LastSuccessUnixSeconds
        {
            get
            {
                long ticks = Interlocked.Read(ref _LastSuccessTicks);
                if (ticks <= 0) return 0;
                return (ticks - _UnixEpochTicks) / (double)TimeSpan.TicksPerSecond;
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[TaskHandler] ";
        private string _Name = "default";
        private int _MaxConcurrentTasks = 32;
        private ConcurrentDictionary<Guid, TaskDetails> _RunningTasks = new ConcurrentDictionary<Guid, TaskDetails>();
        private TaskBuffer _Buffer = null;
        private bool _InUse = false;
        private int _SlotDebt = 0;
        private SemaphoreSlim _concurrencySemaphore;
        private int _queuedCount = 0;
        private ConcurrentDictionary<Guid, TaskDetails> _PendingTasks = new ConcurrentDictionary<Guid, TaskDetails>();
        private TaskDetails _HeldTask = null;

        private CancellationTokenSource _TaskRunnerTokenSource = new CancellationTokenSource();
        private CancellationToken _TaskRunnerToken;
        private Task _TaskRunner = null;

        private readonly object _StateLock = new object();
        private bool _IsStarted = false;
        private bool _IsDisposed = false;

        // Statistics tracking
        private long _TotalEnqueued = 0;
        private long _TotalCompleted = 0;
        private long _TotalFailed = 0;
        private long _TotalCanceled = 0;
        private readonly ConcurrentQueue<TimeSpan> _ExecutionTimes = new ConcurrentQueue<TimeSpan>();
        private readonly ConcurrentQueue<TimeSpan> _WaitTimes = new ConcurrentQueue<TimeSpan>();
        private DateTime? _LastTaskStarted = null;
        private DateTime? _LastTaskCompleted = null;
        private const int _MaxTimingSamples = 1000;

        // Telemetry
        private static readonly long _UnixEpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private long _TelemetryId = 0;
        private long _LastSuccessTicks = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a queue that starts tasks in the order they were added (FIFO).
        /// </summary>
        /// <param name="maxConcurrentTasks">Maximum concurrent tasks. Default: 32. Minimum: 1.</param>
        /// <param name="maxQueueSize">Maximum queue size. -1 for unbounded. Default: -1.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxConcurrentTasks is below 1, or maxQueueSize
        /// is 0 or below -1.</exception>
        public TaskQueue(int maxConcurrentTasks = 32, int maxQueueSize = -1)
        {
            if (maxQueueSize < -1 || maxQueueSize == 0) throw new ArgumentOutOfRangeException(nameof(maxQueueSize), "MaxQueueSize must be -1 (unbounded) or greater than 0");
            Initialize(maxConcurrentTasks, new ChannelTaskBuffer(maxQueueSize));
        }

        /// <summary>
        /// Instantiate a queue whose start order is decided by a QoSKit scheduler, for example
        /// <c>new PriorityQoSQueue&lt;TaskDetails&gt;(5, t =&gt; t.Priority)</c> to start lower
        /// <see cref="TaskDetails.Priority"/> values first.
        /// The scheduler's MaxDepth and OverflowPolicy replace MaxQueueSize: Reject and DropNewest reject new tasks when
        /// full, Block makes asynchronous adds wait for space, and DropOldest evicts the oldest waiting task (which
        /// completes as canceled, like a task dropped by Dispose). The TaskQueue takes ownership of the scheduler and
        /// disposes it; do not share it, enqueue to it directly, or enable QoSKit persistence on it.
        /// </summary>
        /// <param name="scheduler">QoSKit queue of TaskDetails. Must not be null, and must be empty.</param>
        /// <param name="maxConcurrentTasks">Maximum concurrent tasks. Default: 32. Minimum: 1.</param>
        /// <exception cref="ArgumentNullException">Thrown when scheduler is null.</exception>
        /// <exception cref="ArgumentException">Thrown when scheduler already holds items.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxConcurrentTasks is below 1.</exception>
        public TaskQueue(IQoSQueue<TaskDetails> scheduler, int maxConcurrentTasks = 32)
        {
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
            if (scheduler.Count != 0) throw new ArgumentException("The scheduler must be empty when given to a TaskQueue.", nameof(scheduler));
            Initialize(maxConcurrentTasks, new QoSTaskBuffer(scheduler, HandleSchedulerDropped));
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">TaskQueue options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options sets both a Scheduler and a MaxQueueSize, or the
        /// Scheduler already holds items.</exception>
        public TaskQueue(TaskQueueOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (options.Scheduler != null)
            {
                if (options.MaxQueueSize != -1)
                    throw new ArgumentException("Set either Scheduler or MaxQueueSize, not both; configure the scheduler's MaxDepth instead.", nameof(options));
                if (options.Scheduler.Count != 0)
                    throw new ArgumentException("The scheduler must be empty when given to a TaskQueue.", nameof(options));
                Initialize(options.MaxConcurrentTasks, new QoSTaskBuffer(options.Scheduler, HandleSchedulerDropped));
            }
            else
            {
                Initialize(options.MaxConcurrentTasks, new ChannelTaskBuffer(options.MaxQueueSize));
            }

            Name = options.Name;
            Logger = options.Logger;
            OnTaskAdded = options.OnTaskAdded;
            OnTaskStarted = options.OnTaskStarted;
            OnTaskFinished = options.OnTaskFinished;
            OnTaskFaulted = options.OnTaskFaulted;
            OnTaskCanceled = options.OnTaskCanceled;
            OnProcessingStarted = options.OnProcessingStarted;
            OnProcessingStopped = options.OnProcessingStopped;
        }

        /// <summary>
        /// Create a TaskQueue with configuration via options pattern.
        /// </summary>
        /// <param name="configure">Configuration action.</param>
        /// <returns>Configured TaskQueue instance.</returns>
        public static TaskQueue Create(Action<TaskQueueOptions> configure)
        {
            TaskQueueOptions options = new TaskQueueOptions();
            configure?.Invoke(options);
            return new TaskQueue(options);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose. Cancels running tasks and drops tasks that have not started: each dropped task completes as
        /// canceled (its <see cref="TaskHandle{T}"/> is canceled, <see cref="OnTaskCanceled"/> fires, and it is
        /// counted in <see cref="TaskQueueStatistics.TotalCanceled"/>). Fires <see cref="OnProcessingStopped"/> if
        /// the queue was started. Does not wait for running tasks; use <see cref="DisposeAsync"/> to wait.
        /// Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            List<TaskDetails> dropped = new List<TaskDetails>();
            bool wasStarted;

            lock (_StateLock)
            {
                if (_IsDisposed) return;
                _IsDisposed = true;
                wasStarted = _IsStarted;
                _IsStarted = false;

                Logger?.Invoke(_Header + "disposing");
                TaskHandlerTelemetry.Lifecycle(_Name, TaskHandlerTelemetryNames.LifecycleDispose);

                // Stop the runner before canceling tasks so it cannot start another.
                if (!_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Cancel();
                }

                foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                {
                    Logger?.Invoke(_Header + "canceling task GUID " + task.Key.ToString());
                    if (!task.Value.TokenSource.IsCancellationRequested)
                    {
                        TaskHandlerTelemetry.CancellationRequested(_Name, task.Value, TaskHandlerTelemetryNames.CancelReasonDispose);
                    }

                    task.Value.TokenSource.Cancel();
                }

                // Reject further writes (releasing any add waiting for space) and collect everything not started.
                List<TaskDetails> unstarted = _Buffer.Close();
                if (_HeldTask != null)
                {
                    unstarted.Insert(0, _HeldTask);
                    _HeldTask = null;
                }

                foreach (TaskDetails task in unstarted)
                {
                    if (TryLeaveQueue(task)) dropped.Add(task);
                }

                // The semaphore is intentionally not disposed: SemaphoreSlim.Dispose() discards pending async waiters,
                // so a runner whose canceled WaitAsync resumes afterwards would wait forever, and running tasks still
                // release their slots as they finish. It holds no unmanaged resources because AvailableWaitHandle is
                // never used.
                _TaskRunnerTokenSource?.Dispose();
                TaskHandlerTelemetry.UnregisterQueue(_TelemetryId);
            }

            foreach (TaskDetails task in dropped)
            {
                CompleteDropped(task, TaskHandlerTelemetryNames.ErrorQueueClosed);
            }

            if (wasStarted) SafeInvokeEvent(OnProcessingStopped, nameof(OnProcessingStopped), EventArgs.Empty);
        }

        /// <summary>
        /// Dispose asynchronously. Stops the queue, waits for running tasks to finish responding to cancellation,
        /// then disposes (dropping tasks that have not started, as described for <see cref="Dispose"/>).
        /// Safe to call more than once.
        /// </summary>
        /// <returns>ValueTask.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_IsDisposed) return;

            try
            {
                await StopAsync(waitForCompletion: true).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }

            Dispose();
        }

        /// <summary>
        /// Add a task.
        /// Tasks can be added whether or not the queue is started; tasks added while it is stopped run after the
        /// next <see cref="Start"/>. Does not wait: on a bounded queue that is full the task is rejected (use
        /// <see cref="AddTaskAsync"/> to wait for space instead).
        /// Emits the "taskhandler enqueue" span and the taskhandler.task.enqueued / taskhandler.task.rejected metrics.
        /// </summary>
        /// <param name="guid">Guid.</param>
        /// <param name="name">Name of the task.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="func">Action.</param>
        /// <returns>TaskDetails.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a bounded queue is full.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        public TaskDetails AddTask(Guid guid, string name, Dictionary<string, object> metadata, Func<CancellationToken, Task> func)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            TaskDetails details = new TaskDetails
            {
                Guid = guid,
                Name = name,
                Metadata = metadata,
                Function = func
            };

            DateTime enqueueStarted = DateTime.UtcNow;
            Activity enqueueActivity = TaskHandlerTelemetry.StartEnqueue(_Name, details, _queuedCount);
            TaskHandlerTelemetry.CaptureParentContext(details, enqueueActivity);

            // Count the task before it becomes visible to the runner so QueuedCount never goes negative.
            _InUse = true;
            Interlocked.Increment(ref _queuedCount);
            _PendingTasks[details.Guid] = details;

            string rejection = _Buffer.TryWrite(details);
            if (rejection != null)
            {
                TryLeaveQueue(details);
                if (_IsDisposed) rejection = TaskHandlerTelemetryNames.ErrorQueueClosed;
                TaskHandlerTelemetry.EnqueueRejected(_Name, enqueueActivity, enqueueStarted, rejection, null);
                ThrowRejected(name, rejection);
            }

            Interlocked.Increment(ref _TotalEnqueued);
            TaskHandlerTelemetry.EnqueueSucceeded(_Name, enqueueActivity, enqueueStarted);
            SafeInvokeEvent(OnTaskAdded, nameof(OnTaskAdded), details);
            return details;
        }

        /// <summary>
        /// Add a task asynchronously.
        /// Tasks can be added whether or not the queue is started; tasks added while it is stopped run after the
        /// next <see cref="Start"/>. On a bounded queue that is full, waits for space (backpressure), including
        /// while the queue is stopped; the wait is measured by taskhandler.queue.enqueue.duration.
        /// </summary>
        /// <param name="guid">Guid.</param>
        /// <param name="name">Name of the task.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="func">Action.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>TaskDetails.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested while waiting for queue space.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been (or is, while waiting for space) disposed.</exception>
        public async Task<TaskDetails> AddTaskAsync(
            Guid guid,
            string name,
            Dictionary<string, object> metadata,
            Func<CancellationToken, Task> func,
            CancellationToken cancellationToken = default)
        {
            return await AddTaskInternalAsync(guid, name, metadata, func, 0, cancellationToken, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Enqueue a task with priority and timeout support.
        /// Like <see cref="AddTaskAsync"/>, can be called whether or not the queue is started, and waits for space
        /// on a full bounded queue. The timeout covers execution only, not time spent queued.
        /// </summary>
        /// <param name="name">Name of the task.</param>
        /// <param name="func">Task function.</param>
        /// <param name="priority">Task priority (lower number = higher priority). Default: 0.</param>
        /// <param name="timeout">Optional timeout for task execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task GUID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting for space in a bounded queue.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been (or is, while waiting for space) disposed.</exception>
        public async Task<Guid> EnqueueAsync(
            string name,
            Func<CancellationToken, Task> func,
            int priority = 0,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Func<CancellationToken, Task> wrappedFunc = func;

            // Wrap function with timeout if specified
            if (timeout.HasValue)
            {
                Func<CancellationToken, Task> originalFunc = func;
                wrappedFunc = async (CancellationToken token) =>
                {
                    using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeoutCts.CancelAfter(timeout.Value);

                        try
                        {
                            await originalFunc(timeoutCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            throw new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                        }
                    }
                };
            }

            TaskDetails details = await AddTaskInternalAsync(Guid.NewGuid(), name, null, wrappedFunc, priority, cancellationToken, null).ConfigureAwait(false);
            return details.Guid;
        }

        /// <summary>
        /// Enqueue a task with a result.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="name">Name of the task.</param>
        /// <param name="func">Task function that returns a result.</param>
        /// <param name="priority">Task priority (lower number = higher priority). Default: 0.</param>
        /// <param name="timeout">Optional timeout for task execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>TaskHandle that can be awaited for the result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting for space in a bounded queue.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been (or is, while waiting for space) disposed.</exception>
        public async Task<TaskHandle<T>> EnqueueAsync<T>(
            string name,
            Func<CancellationToken, Task<T>> func,
            int priority = 0,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            TaskHandle<T> handle = new TaskHandle<T>(Guid.NewGuid(), name);

            // Wrap function to capture result, applying an optional timeout in the same wrapper
            // so the handle is completed exactly once with the correct terminal state.
            Func<CancellationToken, Task> wrappedFunc = async (CancellationToken token) =>
            {
                CancellationTokenSource timeoutCts = null;
                CancellationToken effectiveToken = token;

                if (timeout.HasValue)
                {
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeoutCts.CancelAfter(timeout.Value);
                    effectiveToken = timeoutCts.Token;
                }

                try
                {
                    T result = await func(effectiveToken).ConfigureAwait(false);
                    handle.SetResult(result);
                }
                catch (OperationCanceledException) when (timeout.HasValue && timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    TimeoutException tex = new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                    handle.SetException(tex);
                    throw tex;
                }
                catch (OperationCanceledException)
                {
                    handle.SetCanceled();
                    throw;
                }
                catch (Exception ex)
                {
                    handle.SetException(ex);
                    throw;
                }
                finally
                {
                    timeoutCts?.Dispose();
                }
            };

            await AddTaskInternalAsync(handle.Id, name, null, wrappedFunc, priority, cancellationToken, handle.SetCanceled).ConfigureAwait(false);
            return handle;
        }

        /// <summary>
        /// Enqueue a task with progress reporting support.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="name">Name of the task.</param>
        /// <param name="func">Task function that accepts IProgress and returns a result.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="priority">Task priority (lower number = higher priority). Default: 0.</param>
        /// <param name="timeout">Optional timeout for task execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>TaskHandle that can be awaited for the result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting for space in a bounded queue.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been (or is, while waiting for space) disposed.</exception>
        public async Task<TaskHandle<T>> EnqueueAsync<T>(
            string name,
            Func<CancellationToken, IProgress<TaskProgress>, Task<T>> func,
            IProgress<TaskProgress> progress,
            int priority = 0,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            TaskHandle<T> handle = new TaskHandle<T>(Guid.NewGuid(), name);

            // Wrap function to capture result and provide progress, applying an optional timeout in
            // the same wrapper so the handle is completed exactly once with the correct terminal state.
            Func<CancellationToken, Task> wrappedFunc = async (CancellationToken token) =>
            {
                CancellationTokenSource timeoutCts = null;
                CancellationToken effectiveToken = token;

                if (timeout.HasValue)
                {
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeoutCts.CancelAfter(timeout.Value);
                    effectiveToken = timeoutCts.Token;
                }

                try
                {
                    T result = await func(effectiveToken, progress).ConfigureAwait(false);
                    handle.SetResult(result);
                }
                catch (OperationCanceledException) when (timeout.HasValue && timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    TimeoutException tex = new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                    handle.SetException(tex);
                    throw tex;
                }
                catch (OperationCanceledException)
                {
                    handle.SetCanceled();
                    throw;
                }
                catch (Exception ex)
                {
                    handle.SetException(ex);
                    throw;
                }
                finally
                {
                    timeoutCts?.Dispose();
                }
            };

            await AddTaskInternalAsync(handle.Id, name, null, wrappedFunc, priority, cancellationToken, handle.SetCanceled).ConfigureAwait(false);
            return handle;
        }

        /// <summary>
        /// Enqueue a task with progress reporting support (no result).
        /// </summary>
        /// <param name="name">Name of the task.</param>
        /// <param name="func">Task function that accepts IProgress.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="priority">Task priority (lower number = higher priority). Default: 0.</param>
        /// <param name="timeout">Optional timeout for task execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task GUID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty, or func is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting for space in a bounded queue.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been (or is, while waiting for space) disposed.</exception>
        public async Task<Guid> EnqueueAsync(
            string name,
            Func<CancellationToken, IProgress<TaskProgress>, Task> func,
            IProgress<TaskProgress> progress,
            int priority = 0,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            Func<CancellationToken, Task> wrappedFunc = async (CancellationToken token) =>
            {
                await func(token, progress).ConfigureAwait(false);
            };

            // Apply timeout if specified
            if (timeout.HasValue)
            {
                Func<CancellationToken, Task> originalFunc = wrappedFunc;
                wrappedFunc = async (CancellationToken token) =>
                {
                    using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeoutCts.CancelAfter(timeout.Value);

                        try
                        {
                            await originalFunc(timeoutCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            throw new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                        }
                    }
                };
            }

            TaskDetails details = await AddTaskInternalAsync(Guid.NewGuid(), name, null, wrappedFunc, priority, cancellationToken, null).ConfigureAwait(false);
            return details.Guid;
        }

        /// <summary>
        /// Get read-only information about all currently running tasks.
        /// </summary>
        /// <returns>Collection of TaskInfo objects.</returns>
        public IReadOnlyCollection<TaskInfo> GetRunningTasksInfo()
        {
            return _RunningTasks.Values
                .Select(t => new TaskInfo(
                    t.Guid,
                    t.Name,
                    t.Task?.Status ?? TaskStatus.Created,
                    t.Priority,
                    t.Metadata != null
                        ? new Dictionary<string, object>(t.Metadata)
                        : new Dictionary<string, object>()
                ))
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Get current queue statistics and metrics.
        /// </summary>
        /// <returns>TaskQueueStatistics instance.</returns>
        public TaskQueueStatistics GetStatistics()
        {
            TimeSpan avgExecTime = TimeSpan.Zero;
            TimeSpan avgWaitTime = TimeSpan.Zero;

            if (_ExecutionTimes.Count > 0)
            {
                double avgMs = _ExecutionTimes.Average(t => t.TotalMilliseconds);
                avgExecTime = TimeSpan.FromMilliseconds(avgMs);
            }

            if (_WaitTimes.Count > 0)
            {
                double avgMs = _WaitTimes.Average(t => t.TotalMilliseconds);
                avgWaitTime = TimeSpan.FromMilliseconds(avgMs);
            }

            return new TaskQueueStatistics
            {
                TotalEnqueued = Interlocked.Read(ref _TotalEnqueued),
                TotalCompleted = Interlocked.Read(ref _TotalCompleted),
                TotalFailed = Interlocked.Read(ref _TotalFailed),
                TotalCanceled = Interlocked.Read(ref _TotalCanceled),
                CurrentQueueDepth = QueuedCount,
                CurrentRunningCount = RunningCount,
                AverageExecutionTime = avgExecTime,
                AverageWaitTime = avgWaitTime,
                LastTaskStarted = _LastTaskStarted,
                LastTaskCompleted = _LastTaskCompleted
            };
        }

        /// <summary>
        /// Start running tasks. Tasks already queued (including tasks retained by a previous <see cref="Stop()"/>)
        /// start in the order they were added, subject to <see cref="MaxConcurrentTasks"/>.
        /// A stopped queue can be started again.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the queue is already started.</exception>
        public void Start()
        {
            lock (_StateLock)
            {
                if (_IsDisposed) throw new ObjectDisposedException(nameof(TaskQueue));
                if (_IsStarted) throw new InvalidOperationException("Task queue is already started.");

                Logger?.Invoke(_Header + "starting");
                _IsStarted = true;
                _InUse = true;

                // Recreate cancellation token source if it was canceled
                if (_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Dispose();
                    _TaskRunnerTokenSource = new CancellationTokenSource();
                    _TaskRunnerToken = _TaskRunnerTokenSource.Token;
                }

                // The new runner waits for the previous one to exit, so the two never read the queue concurrently
                // and a task the previous runner was holding is resumed first.
                Task previousRunner = _TaskRunner;
                CancellationToken token = _TaskRunnerToken;
                _TaskRunner = Task.Run(() => TaskRunner(previousRunner, token), token);
                TaskHandlerTelemetry.Lifecycle(_Name, TaskHandlerTelemetryNames.LifecycleStart);
            }

            SafeInvokeEvent(OnProcessingStarted, nameof(OnProcessingStarted), EventArgs.Empty);
        }

        /// <summary>
        /// Start running tasks asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the queue is already started.</exception>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Start();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop running tasks. Cancels every running task (through the token passed to its function) and stops
        /// starting new ones. Tasks that have not started are retained, and tasks can still be added; all of them
        /// run after the next <see cref="Start"/>. Fires <see cref="OnProcessingStopped"/>.
        /// Does nothing if the queue is not started.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        public void Stop()
        {
            lock (_StateLock)
            {
                if (_IsDisposed) throw new ObjectDisposedException(nameof(TaskQueue));
                if (!_IsStarted) return;

                Logger?.Invoke(_Header + "stopping (" + _RunningTasks.Count + " running tasks)");
                _IsStarted = false;
                TaskHandlerTelemetry.Lifecycle(_Name, TaskHandlerTelemetryNames.LifecycleStop);

                // Stop the runner first so it cannot start another task after the running set is canceled.
                if (!_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Cancel();
                }

                foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                {
                    if (!task.Value.TokenSource.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "canceling task " + task.Key.ToString());
                        TaskHandlerTelemetry.CancellationRequested(_Name, task.Value, TaskHandlerTelemetryNames.CancelReasonStopAll);
                        task.Value.TokenSource.Cancel();
                    }
                }
            }

            SafeInvokeEvent(OnProcessingStopped, nameof(OnProcessingStopped), EventArgs.Empty);
        }

        /// <summary>
        /// Stop running tasks asynchronously. See <see cref="Stop()"/>.
        /// </summary>
        /// <param name="waitForCompletion">When true, waits until the background runner has exited and every
        /// canceled task has finished. A task function that ignores its cancellation token delays this until it
        /// returns, so pass a cancellationToken to bound the wait. Default: false.</param>
        /// <param name="cancellationToken">Cancellation token for the wait.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting.</exception>
        public async Task StopAsync(bool waitForCompletion = false, CancellationToken cancellationToken = default)
        {
            Stop();

            if (!waitForCompletion) return;

            Task runner = _TaskRunner;
            if (runner != null)
            {
                try
                {
                    await runner.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The runner records its own failures
                }
            }

            while (_RunningTasks.Count > 0)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Wait until no tasks are queued or running. Tasks retained while the queue is stopped count as queued,
        /// so on a stopped queue that still holds tasks this does not return until the queue is started and they
        /// finish, or cancellationToken is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancellationToken is canceled while waiting.</exception>
        public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
        {
            // Note: use QueuedCount (tracked via Interlocked) rather than the channel reader's
            // Count property. The default unbounded, single-reader channel is backed by
            // SingleConsumerUnboundedChannel, whose Count property throws NotSupportedException.
            while (QueuedCount > 0 || _RunningTasks.Count > 0)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Cancel a task by GUID, whether it is running or still waiting in the queue (including while the queue is
        /// stopped). A running task is canceled through the token passed to its function. A waiting task never
        /// runs: its <see cref="TaskHandle{T}"/> (if any) is canceled immediately, and it completes as canceled
        /// (firing <see cref="OnTaskCanceled"/>) when the queue next reaches it. Unknown or finished GUIDs are ignored.
        /// </summary>
        /// <param name="guid">GUID.</param>
        public void Stop(Guid guid)
        {
            Logger?.Invoke(_Header + "attempting to stop task " + guid.ToString());

            TaskDetails task = null;
            if (_RunningTasks.TryGetValue(guid, out task) || _PendingTasks.TryGetValue(guid, out task))
            {
                if (task.TokenSource.IsCancellationRequested) return;

                Logger?.Invoke(_Header + "canceling task " + guid.ToString());
                TaskHandlerTelemetry.CancellationRequested(_Name, task, TaskHandlerTelemetryNames.CancelReasonStopTask);

                try
                {
                    task.TokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_PendingTasks.ContainsKey(guid)) InvokeAbandoned(task);
            }
            else
            {
                Logger?.Invoke(_Header + "task " + guid.ToString() + " not found");
            }
        }

        #endregion

        #region Private-Methods

        private void Initialize(int maxConcurrentTasks, TaskBuffer buffer)
        {
            if (maxConcurrentTasks < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrentTasks));

            _TaskRunnerToken = _TaskRunnerTokenSource.Token;
            _MaxConcurrentTasks = maxConcurrentTasks;

            // The buffer lives as long as the queue: Stop() leaves it open so queued tasks are retained and new tasks
            // can still be added, and only Dispose() closes it. The semaphore's maximum is unbounded so
            // MaxConcurrentTasks can be raised later.
            _Buffer = buffer;
            _concurrencySemaphore = new SemaphoreSlim(maxConcurrentTasks, Int32.MaxValue);
            _TelemetryId = TaskHandlerTelemetry.RegisterQueue(this);
        }

        private void HandleSchedulerDropped(TaskDetails taskDetails, string errorType)
        {
            // The scheduler evicted an accepted task to admit a newer one (DropOldest).
            if (!TryLeaveQueue(taskDetails)) return;
            CompleteDropped(taskDetails, errorType);
        }

        private void ReleaseSlot()
        {
            // Absorb the slot instead when MaxConcurrentTasks was lowered below the number of slots in use.
            while (true)
            {
                int debt = Volatile.Read(ref _SlotDebt);
                if (debt <= 0) break;
                if (Interlocked.CompareExchange(ref _SlotDebt, debt - 1, debt) == debt) return;
            }

            _concurrencySemaphore.Release();
        }

        private void ThrowRejected(string name, string rejection)
        {
            if (rejection == TaskHandlerTelemetryNames.ErrorQueueClosed) throw new ObjectDisposedException(nameof(TaskQueue));
            if (rejection == TaskHandlerTelemetryNames.ErrorUnclassified)
                throw new InvalidOperationException("Failed to enqueue task '" + name + "': the scheduler could not classify it.");
            throw new InvalidOperationException("Failed to enqueue task '" + name + "': the queue is full (capacity " + MaxQueueSize + ").");
        }

        private bool TryLeaveQueue(TaskDetails taskDetails)
        {
            // Exactly one path settles a queued task: starting it, canceling it before start, or dropping it.
            if (!taskDetails.TryClaim()) return false;
            Interlocked.Decrement(ref _queuedCount);
            RemovePending(taskDetails);
            return true;
        }

        private async Task<TaskDetails> AddTaskInternalAsync(
            Guid guid,
            string name,
            Dictionary<string, object> metadata,
            Func<CancellationToken, Task> func,
            int priority,
            CancellationToken cancellationToken,
            Action onAbandoned)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            TaskDetails details = new TaskDetails
            {
                Guid = guid,
                Name = name,
                Metadata = metadata,
                Function = func,
                Priority = priority,
                OnAbandoned = onAbandoned
            };

            DateTime enqueueStarted = DateTime.UtcNow;
            Activity enqueueActivity = TaskHandlerTelemetry.StartEnqueue(_Name, details, _queuedCount);
            TaskHandlerTelemetry.CaptureParentContext(details, enqueueActivity);

            // Count the task before it becomes visible to the runner so QueuedCount never goes negative.
            _InUse = true;
            Interlocked.Increment(ref _queuedCount);
            _PendingTasks[details.Guid] = details;

            string rejection;
            try
            {
                rejection = await _Buffer.WriteAsync(details, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TryLeaveQueue(details);
                TaskHandlerTelemetry.EnqueueRejected(_Name, enqueueActivity, enqueueStarted, TaskHandlerTelemetry.EnqueueErrorType(ex), ex);
                if (ex is ChannelClosedException) throw new ObjectDisposedException(nameof(TaskQueue));
                throw;
            }

            if (rejection != null)
            {
                TryLeaveQueue(details);
                TaskHandlerTelemetry.EnqueueRejected(_Name, enqueueActivity, enqueueStarted, rejection, null);
                ThrowRejected(name, rejection);
            }

            Interlocked.Increment(ref _TotalEnqueued);
            TaskHandlerTelemetry.EnqueueSucceeded(_Name, enqueueActivity, enqueueStarted);
            SafeInvokeEvent(OnTaskAdded, nameof(OnTaskAdded), details);
            return details;
        }

        private void InvokeAbandoned(TaskDetails taskDetails)
        {
            // A task canceled before Task.Run invoked its function never reaches the result wrapper, so its
            // handle would otherwise never complete. Handle completion is idempotent.
            try
            {
                taskDetails.OnAbandoned?.Invoke();
            }
            catch (Exception ex)
            {
                Logger?.Invoke(_Header + "exception completing abandoned task handle: " + ex.ToString());
            }
        }

        private void SafeInvokeEvent<T>(EventHandler<T> handler, string eventName, T args)
        {
            if (handler == null) return;

            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Logger?.Invoke(_Header + "exception in event handler: " + ex.ToString());
                TaskHandlerTelemetry.EventHandlerError(_Name, eventName, ex);
            }
        }

        private void SafeInvokeEvent(EventHandler handler, string eventName, EventArgs args)
        {
            if (handler == null) return;

            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Logger?.Invoke(_Header + "exception in event handler: " + ex.ToString());
                TaskHandlerTelemetry.EventHandlerError(_Name, eventName, ex);
            }
        }

        private async Task TaskRunner(Task previousRunner, CancellationToken token)
        {
            // Detach from whatever span was current when Start() was called, so each task's spans are parented
            // only to the context captured when that task was enqueued.
            Activity.Current = null;

            try
            {
                if (previousRunner != null)
                {
                    try
                    {
                        await previousRunner.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The previous runner recorded its own failure
                    }
                }

                TaskDetails held = TakeHeldTask();
                if (held != null)
                {
                    await DispatchAsync(held, token, true, false).ConfigureAwait(false);
                }

                // A QoSKit scheduler is asked for the next task only once a slot is free, so a task added while all
                // slots are busy can still overtake lower-priority work. The default FIFO order is unaffected by
                // reading ahead, so it reads first and then waits for a slot (keeping the queued and slot_wait stages
                // distinct).
                bool slotFirst = _Buffer is QoSTaskBuffer;
                while (true)
                {
                    if (slotFirst)
                    {
                        await _concurrencySemaphore.WaitAsync(token).ConfigureAwait(false);

                        TaskDetails next;
                        try
                        {
                            next = await _Buffer.ReadAsync(token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            ReleaseSlot();
                            throw;
                        }

                        await DispatchAsync(next, token, false, true).ConfigureAwait(false);
                    }
                    else
                    {
                        TaskDetails next = await _Buffer.ReadAsync(token).ConfigureAwait(false);
                        await DispatchAsync(next, token, false, false).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(_Header + "task runner canceled");
            }
            catch (ChannelClosedException)
            {
                Logger?.Invoke(_Header + "task runner exiting, queue disposed");
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "task runner exception: " + Environment.NewLine + e.ToString());

                bool stopped = false;
                lock (_StateLock)
                {
                    if (!_IsDisposed && _IsStarted && token == _TaskRunnerToken)
                    {
                        _IsStarted = false;
                        stopped = true;
                    }
                }

                if (stopped)
                {
                    TaskHandlerTelemetry.RunnerError(_Name, e);
                    SafeInvokeEvent(OnProcessingStopped, nameof(OnProcessingStopped), EventArgs.Empty);
                }
            }

            Logger?.Invoke(_Header + "task runner exiting");
        }

        private async Task DispatchAsync(TaskDetails taskDetails, CancellationToken token, bool resumed, bool slotHeld)
        {
            if (!resumed)
            {
                taskDetails.DequeuedAt = DateTime.UtcNow;
                TaskHandlerTelemetry.TaskDequeued(_Name, taskDetails);
            }

            // A task canceled with Stop(guid) while it waited never runs and does not need a slot.
            if (taskDetails.Token.IsCancellationRequested)
            {
                if (slotHeld) ReleaseSlot();
                CompleteCanceledBeforeStart(taskDetails);
                return;
            }

            // Wait for available slot unless the runner already holds one. If the queue stops first, hold the task
            // for the next Start(); if it is disposed, drop it.
            if (!slotHeld)
            {
                try
                {
                    await _concurrencySemaphore.WaitAsync(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    HoldOrDrop(taskDetails);
                    throw;
                }
            }

            bool interrupted = false;
            bool canceledBeforeStart = false;
            lock (_StateLock)
            {
                // Stop() and Dispose() take this lock, so a task is either added to the running set before they
                // cancel it, or never started.
                if (token.IsCancellationRequested)
                {
                    interrupted = true;
                    ReleaseSlot();
                }
                else if (taskDetails.Token.IsCancellationRequested)
                {
                    canceledBeforeStart = true;
                    ReleaseSlot();
                }
                else if (!TryLeaveQueue(taskDetails))
                {
                    // Already settled elsewhere (dropped); nothing to start.
                    ReleaseSlot();
                    return;
                }
                else
                {
                    // Track timing
                    taskDetails.StartedAt = DateTime.UtcNow;
                    _LastTaskStarted = taskDetails.StartedAt;
                    TimeSpan waitTime = taskDetails.StartedAt.Value - taskDetails.EnqueuedAt;
                    _WaitTimes.Enqueue(waitTime);
                    while (_WaitTimes.Count > _MaxTimingSamples)
                    {
                        _WaitTimes.TryDequeue(out TimeSpan discarded);
                    }

                    // Open the execute span before the task becomes visible in RunningTasks, so a Stop(guid) issued as
                    // soon as the task appears is recorded on it. The span (when subscribed) stays current while
                    // Task.Run captures the execution context, so spans and logs created by the user function nest
                    // under it.
                    TaskHandlerTelemetry.TaskStarting(_Name, taskDetails, _MaxConcurrentTasks);

                    // Add to running tasks
                    _RunningTasks.TryAdd(taskDetails.Guid, taskDetails);
                }
            }

            if (interrupted)
            {
                HoldOrDrop(taskDetails);
                throw new OperationCanceledException(token);
            }

            if (canceledBeforeStart)
            {
                CompleteCanceledBeforeStart(taskDetails);
                return;
            }

            // The token is deliberately not passed to Task.Run: once a task is in the running set its function is
            // always invoked, so a cancellation that arrives now is observed by the function (and its own cleanup
            // runs) instead of the task silently never starting.
            taskDetails.Task = Task.Run(() => taskDetails.Function(taskDetails.Token));
            Activity.Current = null;

            Logger?.Invoke(_Header + "started task " + taskDetails.Guid.ToString() + " (" + _RunningTasks.Count + " running tasks)");
            SafeInvokeEvent(OnTaskStarted, nameof(OnTaskStarted), taskDetails);

            // Set up continuation to handle completion
            Task continuation = taskDetails.Task.ContinueWith(
                completedTask => HandleTaskCompletion(taskDetails, completedTask, true),
                TaskScheduler.Default
            );
        }

        private void CompleteCanceledBeforeStart(TaskDetails taskDetails)
        {
            if (!TryLeaveQueue(taskDetails)) return;
            TaskHandlerTelemetry.TaskStarting(_Name, taskDetails, _MaxConcurrentTasks);
            Activity.Current = null;
            taskDetails.Task = Task.FromCanceled(taskDetails.Token);
            HandleTaskCompletion(taskDetails, taskDetails.Task, false);
        }

        private void HoldOrDrop(TaskDetails taskDetails)
        {
            bool drop;
            lock (_StateLock)
            {
                if (_IsDisposed)
                {
                    drop = TryLeaveQueue(taskDetails);
                }
                else
                {
                    drop = false;
                    _HeldTask = taskDetails;
                }
            }

            if (drop) CompleteDropped(taskDetails, TaskHandlerTelemetryNames.ErrorQueueClosed);
        }

        private TaskDetails TakeHeldTask()
        {
            lock (_StateLock)
            {
                TaskDetails held = _HeldTask;
                _HeldTask = null;
                return held;
            }
        }

        private void CompleteDropped(TaskDetails taskDetails, string errorType)
        {
            Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " dropped before it started (" + errorType + ")");
            TaskHandlerTelemetry.TaskDropped(_Name, taskDetails, errorType);
            Interlocked.Increment(ref _TotalCanceled);
            InvokeAbandoned(taskDetails);
            SafeInvokeEvent(OnTaskCanceled, nameof(OnTaskCanceled), taskDetails);
        }

        private void RemovePending(TaskDetails taskDetails)
        {
            // Remove only this instance, in case another task was added with the same GUID.
            ((ICollection<KeyValuePair<Guid, TaskDetails>>)_PendingTasks).Remove(new KeyValuePair<Guid, TaskDetails>(taskDetails.Guid, taskDetails));
        }

        private void HandleTaskCompletion(TaskDetails taskDetails, Task completedTask, bool releaseSlot)
        {
            try
            {
                // Remove from running tasks (only this instance, in case another task was added with the same GUID)
                ((ICollection<KeyValuePair<Guid, TaskDetails>>)_RunningTasks).Remove(new KeyValuePair<Guid, TaskDetails>(taskDetails.Guid, taskDetails));

                // Track completion time
                DateTime completedAt = DateTime.UtcNow;
                _LastTaskCompleted = completedAt;

                // Track execution time if task started
                if (taskDetails.StartedAt.HasValue)
                {
                    TimeSpan execTime = completedAt - taskDetails.StartedAt.Value;
                    _ExecutionTimes.Enqueue(execTime);
                    while (_ExecutionTimes.Count > _MaxTimingSamples)
                    {
                        _ExecutionTimes.TryDequeue(out TimeSpan discarded);
                    }
                }

                // Record telemetry (best-effort) before user event handlers run
                TaskHandlerTelemetry.TaskCompleted(_Name, taskDetails, completedTask, completedAt);

                // Fire appropriate event and update counters
                if (completedTask.Status == TaskStatus.RanToCompletion)
                {
                    Interlocked.Increment(ref _TotalCompleted);
                    Interlocked.Exchange(ref _LastSuccessTicks, completedAt.Ticks);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " completed");
                    SafeInvokeEvent(OnTaskFinished, nameof(OnTaskFinished), taskDetails);
                }
                else if (completedTask.Status == TaskStatus.Faulted)
                {
                    Interlocked.Increment(ref _TotalFailed);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " faulted");
                    SafeInvokeEvent(OnTaskFaulted, nameof(OnTaskFaulted), taskDetails);
                }
                else if (completedTask.Status == TaskStatus.Canceled)
                {
                    Interlocked.Increment(ref _TotalCanceled);
                    InvokeAbandoned(taskDetails);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " canceled");
                    SafeInvokeEvent(OnTaskCanceled, nameof(OnTaskCanceled), taskDetails);
                }
            }
            finally
            {
                // Release semaphore slot for next task
                if (releaseSlot) ReleaseSlot();
            }
        }

        #endregion
    }
}
