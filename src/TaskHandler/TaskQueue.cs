namespace TaskHandler
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Task queue.
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
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTasks));
                _MaxConcurrentTasks = value;
            }
        }

        /// <summary>
        /// Maximum queue size. -1 for unbounded.
        /// Default: -1 (unbounded)
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
        /// Number of tasks waiting in the queue.
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
        /// Event to fire when a task is canceled.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskCanceled { get; set; } = null;

        /// <summary>
        /// Event to fire when a task is faulted.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFaulted { get; set; } = null;

        /// <summary>
        /// Event to fire when a task is finished.
        /// Default: null.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFinished { get; set; } = null;

        /// <summary>
        /// Event to fire when processing starts.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStarted { get; set; } = null;

        /// <summary>
        /// Event to fire when processing stops.
        /// Default: null.
        /// </summary>
        public EventHandler OnProcessingStopped { get; set; } = null;

        /// <summary>
        /// Boolean to indicate whether or not the task runner is running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return (_TaskRunner != null && _TaskRunner.Status == TaskStatus.Running);
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[TaskHandler] ";
        private int _MaxConcurrentTasks = 32;
        private int _MaxQueueSize = -1;
        private ConcurrentDictionary<Guid, TaskDetails> _RunningTasks = new ConcurrentDictionary<Guid, TaskDetails>();
        private Channel<TaskDetails> _queueChannel;
        private SemaphoreSlim _concurrencySemaphore;
        private int _queuedCount = 0;

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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="maxConcurrentTasks">Maximum concurrent tasks. Default: 32. Minimum: 1.</param>
        /// <param name="maxQueueSize">Maximum queue size. -1 for unbounded. Default: -1.</param>
        public TaskQueue(int maxConcurrentTasks = 32, int maxQueueSize = -1)
        {
            _TaskRunnerToken = _TaskRunnerTokenSource.Token;

            MaxConcurrentTasks = maxConcurrentTasks;
            MaxQueueSize = maxQueueSize;

            if (maxQueueSize > 0)
            {
                _queueChannel = Channel.CreateBounded<TaskDetails>(new BoundedChannelOptions(maxQueueSize)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            }
            else
            {
                _queueChannel = Channel.CreateUnbounded<TaskDetails>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            }

            _concurrencySemaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">TaskQueue options.</param>
        public TaskQueue(TaskQueueOptions options)
            : this(options.MaxConcurrentTasks, options.MaxQueueSize)
        {
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
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            lock (_StateLock)
            {
                if (_IsDisposed) return;
                _IsDisposed = true;

                Logger?.Invoke(_Header + "disposing");

                foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                {
                    Logger?.Invoke(_Header + "canceling task GUID " + task.Key.ToString());
                    task.Value.TokenSource.Cancel();
                }

                if (!_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Cancel();
                }

                _TaskRunnerTokenSource?.Dispose();
                _concurrencySemaphore?.Dispose();
            }
        }

        /// <summary>
        /// Dispose asynchronously.
        /// </summary>
        /// <returns>ValueTask.</returns>
        public async ValueTask DisposeAsync()
        {
            await StopAsync(waitForCompletion: true).ConfigureAwait(false);
            Dispose();
        }

        /// <summary>
        /// Add a task.
        /// </summary>
        /// <param name="guid">Guid.</param>
        /// <param name="name">Name of the task.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="func">Action.</param>
        /// <returns>TaskDetails.</returns>
        public TaskDetails AddTask(Guid guid, string name, Dictionary<string, object> metadata, Func<CancellationToken, Task> func)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            // If channel is completed (after Stop()), recreate it
            if (_queueChannel.Reader.Completion.IsCompleted)
            {
                lock (_StateLock)
                {
                    if (_queueChannel.Reader.Completion.IsCompleted)
                    {
                        if (_MaxQueueSize > 0)
                        {
                            _queueChannel = Channel.CreateBounded<TaskDetails>(new BoundedChannelOptions(_MaxQueueSize)
                            {
                                SingleReader = true,
                                SingleWriter = false,
                                FullMode = BoundedChannelFullMode.Wait
                            });
                        }
                        else
                        {
                            _queueChannel = Channel.CreateUnbounded<TaskDetails>(new UnboundedChannelOptions
                            {
                                SingleReader = true,
                                SingleWriter = false
                            });
                        }
                    }
                }
            }

            TaskDetails details = new TaskDetails
            {
                Guid = guid,
                Name = name,
                Metadata = metadata,
                Function = func
            };

            if (!_queueChannel.Writer.TryWrite(details))
            {
                throw new InvalidOperationException("Failed to enqueue task.");
            }

            Interlocked.Increment(ref _queuedCount);
            Interlocked.Increment(ref _TotalEnqueued);
            SafeInvokeEvent(OnTaskAdded, details);
            return details;
        }

        /// <summary>
        /// Add a task asynchronously.
        /// </summary>
        /// <param name="guid">Guid.</param>
        /// <param name="name">Name of the task.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="func">Action.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>TaskDetails.</returns>
        public async Task<TaskDetails> AddTaskAsync(
            Guid guid,
            string name,
            Dictionary<string, object> metadata,
            Func<CancellationToken, Task> func,
            CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            // If channel is completed (after Stop()), recreate it
            if (_queueChannel.Reader.Completion.IsCompleted)
            {
                lock (_StateLock)
                {
                    if (_queueChannel.Reader.Completion.IsCompleted)
                    {
                        if (_MaxQueueSize > 0)
                        {
                            _queueChannel = Channel.CreateBounded<TaskDetails>(new BoundedChannelOptions(_MaxQueueSize)
                            {
                                SingleReader = true,
                                SingleWriter = false,
                                FullMode = BoundedChannelFullMode.Wait
                            });
                        }
                        else
                        {
                            _queueChannel = Channel.CreateUnbounded<TaskDetails>(new UnboundedChannelOptions
                            {
                                SingleReader = true,
                                SingleWriter = false
                            });
                        }
                    }
                }
            }

            TaskDetails details = new TaskDetails
            {
                Guid = guid,
                Name = name,
                Metadata = metadata,
                Function = func
            };

            await _queueChannel.Writer.WriteAsync(details, cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _queuedCount);
            Interlocked.Increment(ref _TotalEnqueued);
            SafeInvokeEvent(OnTaskAdded, details);
            return details;
        }

        /// <summary>
        /// Enqueue a task with priority and timeout support.
        /// </summary>
        /// <param name="name">Name of the task.</param>
        /// <param name="func">Task function.</param>
        /// <param name="priority">Task priority (lower number = higher priority). Default: 0.</param>
        /// <param name="timeout">Optional timeout for task execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task GUID.</returns>
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

            TaskDetails details = await AddTaskAsync(Guid.NewGuid(), name, null, wrappedFunc, cancellationToken).ConfigureAwait(false);
            details.Priority = priority;
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

            // Wrap function to capture result
            Func<CancellationToken, Task> wrappedFunc = async (CancellationToken token) =>
            {
                try
                {
                    T result = await func(token).ConfigureAwait(false);
                    handle.SetResult(result);
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
                            TimeoutException tex = new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                            handle.SetException(tex);
                            throw tex;
                        }
                    }
                };
            }

            TaskDetails details = await AddTaskAsync(handle.Id, name, null, wrappedFunc, cancellationToken).ConfigureAwait(false);
            details.Priority = priority;
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

            // Wrap function to capture result and provide progress
            Func<CancellationToken, Task> wrappedFunc = async (CancellationToken token) =>
            {
                try
                {
                    T result = await func(token, progress).ConfigureAwait(false);
                    handle.SetResult(result);
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
                            TimeoutException tex = new TimeoutException($"Task '{name}' timed out after {timeout.Value.TotalSeconds}s");
                            handle.SetException(tex);
                            throw tex;
                        }
                    }
                };
            }

            TaskDetails details = await AddTaskAsync(handle.Id, name, null, wrappedFunc, cancellationToken).ConfigureAwait(false);
            details.Priority = priority;
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

            TaskDetails details = await AddTaskAsync(Guid.NewGuid(), name, null, wrappedFunc, cancellationToken).ConfigureAwait(false);
            details.Priority = priority;
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
        /// Start running tasks.
        /// </summary>
        public void Start()
        {
            lock (_StateLock)
            {
                if (_IsDisposed) throw new ObjectDisposedException(nameof(TaskQueue));
                if (_IsStarted) throw new InvalidOperationException("Task queue is already started.");

                Logger?.Invoke(_Header + "starting");
                _IsStarted = true;

                // Recreate cancellation token source if it was canceled
                if (_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Dispose();
                    _TaskRunnerTokenSource = new CancellationTokenSource();
                    _TaskRunnerToken = _TaskRunnerTokenSource.Token;
                }

                // Recreate channel if it was completed
                if (_queueChannel.Reader.Completion.IsCompleted)
                {
                    if (_MaxQueueSize > 0)
                    {
                        _queueChannel = Channel.CreateBounded<TaskDetails>(new BoundedChannelOptions(_MaxQueueSize)
                        {
                            SingleReader = true,
                            SingleWriter = false,
                            FullMode = BoundedChannelFullMode.Wait
                        });
                    }
                    else
                    {
                        _queueChannel = Channel.CreateUnbounded<TaskDetails>(new UnboundedChannelOptions
                        {
                            SingleReader = true,
                            SingleWriter = false
                        });
                    }
                }

                _TaskRunner = Task.Run(() => TaskRunner(_TaskRunnerToken), _TaskRunnerToken);
                SafeInvokeEvent(OnProcessingStarted, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Start running tasks asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Start();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop running tasks.
        /// </summary>
        public void Stop()
        {
            lock (_StateLock)
            {
                if (_IsDisposed) throw new ObjectDisposedException(nameof(TaskQueue));
                if (!_IsStarted) return;

                Logger?.Invoke(_Header + "stopping (" + _RunningTasks.Count + " running tasks)");
                _IsStarted = false;

                // Complete the channel (no more tasks accepted)
                _queueChannel.Writer.Complete();

                // Cancel all running tasks
                if (_RunningTasks.Count > 0)
                {
                    foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                    {
                        Logger?.Invoke(_Header + "evaluating task " + task.Key.ToString());
                        if (!task.Value.TokenSource.IsCancellationRequested)
                        {
                            Logger?.Invoke(_Header + "canceling task " + task.Key.ToString());
                            task.Value.TokenSource.Cancel();
                            SafeInvokeEvent(OnTaskCanceled, task.Value);
                        }
                    }
                }

                if (!_TaskRunnerTokenSource.IsCancellationRequested)
                {
                    _TaskRunnerTokenSource.Cancel();
                }

                SafeInvokeEvent(OnProcessingStopped, EventArgs.Empty);
                _TaskRunner = null;
            }
        }

        /// <summary>
        /// Stop running tasks asynchronously.
        /// </summary>
        /// <param name="waitForCompletion">Whether to wait for running tasks to complete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(bool waitForCompletion = false, CancellationToken cancellationToken = default)
        {
            Stop();

            if (waitForCompletion && _TaskRunner != null)
            {
                try
                {
                    await _TaskRunner.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
        }

        /// <summary>
        /// Wait for all tasks to complete.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
        {
            while (_queueChannel.Reader.Count > 0 || _RunningTasks.Count > 0)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stop a running task by GUID.
        /// </summary>
        /// <param name="guid">GUID.</param>
        public void Stop(Guid guid)
        {
            Logger?.Invoke(_Header + "attempting to stop task " + guid.ToString());

            if (_TaskRunner == null) return;

            TaskDetails task = null;
            if (_RunningTasks.TryGetValue(guid, out task))
            {
                Logger?.Invoke(_Header + "canceling task " + guid.ToString());
                task.TokenSource.Cancel();
                SafeInvokeEvent(OnTaskCanceled, task);
            }
            else
            {
                Logger?.Invoke(_Header + "task " + guid.ToString() + " not found");
            }
        }

        #endregion

        #region Private-Methods

        private void SafeInvokeEvent<T>(EventHandler<T> handler, T args)
        {
            if (handler == null) return;

            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Logger?.Invoke(_Header + "exception in event handler: " + ex.ToString());
            }
        }

        private void SafeInvokeEvent(EventHandler handler, EventArgs args)
        {
            if (handler == null) return;

            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Logger?.Invoke(_Header + "exception in event handler: " + ex.ToString());
            }
        }

        private async Task TaskRunner(CancellationToken token)
        {
            try
            {
#if NETSTANDARD2_0
                while (await _queueChannel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_queueChannel.Reader.TryRead(out TaskDetails taskDetails))
                    {
                        Interlocked.Decrement(ref _queuedCount);

                        // Wait for available slot
                        await _concurrencySemaphore.WaitAsync(token).ConfigureAwait(false);

                        // Add to running tasks
                        _RunningTasks.TryAdd(taskDetails.Guid, taskDetails);

                        // Track timing
                        taskDetails.StartedAt = DateTime.UtcNow;
                        _LastTaskStarted = taskDetails.StartedAt;
                        TimeSpan waitTime = taskDetails.StartedAt.Value - taskDetails.EnqueuedAt;
                        _WaitTimes.Enqueue(waitTime);
                        while (_WaitTimes.Count > _MaxTimingSamples)
                        {
                            _WaitTimes.TryDequeue(out TimeSpan discarded);
                        }

                        // Start task with continuation
                        taskDetails.Task = Task.Run(() => taskDetails.Function(taskDetails.Token), taskDetails.Token);

                        Logger?.Invoke(_Header + "started task " + taskDetails.Guid.ToString() + " (" + _RunningTasks.Count + " running tasks)");
                        SafeInvokeEvent(OnTaskStarted, taskDetails);

                        // Set up continuation to handle completion
                        Task continuation = taskDetails.Task.ContinueWith(
                            completedTask => HandleTaskCompletion(taskDetails, completedTask),
                            TaskScheduler.Default
                        );
                    }
                }
#else
                await foreach (TaskDetails taskDetails in _queueChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    Interlocked.Decrement(ref _queuedCount);

                    // Wait for available slot
                    await _concurrencySemaphore.WaitAsync(token).ConfigureAwait(false);

                    // Add to running tasks
                    _RunningTasks.TryAdd(taskDetails.Guid, taskDetails);

                    // Track timing
                    taskDetails.StartedAt = DateTime.UtcNow;
                    _LastTaskStarted = taskDetails.StartedAt;
                    TimeSpan waitTime = taskDetails.StartedAt.Value - taskDetails.EnqueuedAt;
                    _WaitTimes.Enqueue(waitTime);
                    while (_WaitTimes.Count > _MaxTimingSamples)
                    {
                        _WaitTimes.TryDequeue(out TimeSpan discarded);
                    }

                    // Start task with continuation
                    taskDetails.Task = Task.Run(() => taskDetails.Function(taskDetails.Token), taskDetails.Token);

                    Logger?.Invoke(_Header + "started task " + taskDetails.Guid.ToString() + " (" + _RunningTasks.Count + " running tasks)");
                    SafeInvokeEvent(OnTaskStarted, taskDetails);

                    // Set up continuation to handle completion
                    Task continuation = taskDetails.Task.ContinueWith(
                        completedTask => HandleTaskCompletion(taskDetails, completedTask),
                        TaskScheduler.Default
                    );
                }
#endif
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(_Header + "task runner canceled");
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "task runner exception: " + Environment.NewLine + e.ToString());
            }
            finally
            {
                SafeInvokeEvent(OnProcessingStopped, EventArgs.Empty);
            }

            Logger?.Invoke(_Header + "task runner exiting");
        }

        private void HandleTaskCompletion(TaskDetails taskDetails, Task completedTask)
        {
            try
            {
                // Remove from running tasks
                _RunningTasks.TryRemove(taskDetails.Guid, out TaskDetails removed);

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

                // Fire appropriate event and update counters
                if (completedTask.Status == TaskStatus.RanToCompletion)
                {
                    Interlocked.Increment(ref _TotalCompleted);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " completed");
                    SafeInvokeEvent(OnTaskFinished, taskDetails);
                }
                else if (completedTask.Status == TaskStatus.Faulted)
                {
                    Interlocked.Increment(ref _TotalFailed);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " faulted");
                    SafeInvokeEvent(OnTaskFaulted, taskDetails);
                }
                else if (completedTask.Status == TaskStatus.Canceled)
                {
                    Interlocked.Increment(ref _TotalCanceled);
                    Logger?.Invoke(_Header + "task " + taskDetails.Guid.ToString() + " canceled");
                    SafeInvokeEvent(OnTaskCanceled, taskDetails);
                }
            }
            finally
            {
                // Release semaphore slot for next task
                _concurrencySemaphore.Release();
            }
        }

        #endregion
    }
}
