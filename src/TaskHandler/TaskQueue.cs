namespace TaskHandler
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel.Design;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Task queue.
    /// </summary>
    public class TaskQueue : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Method to invoke to send log messages.
        /// </summary>
        public Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Maximum number of concurrent tasks.
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
        /// Queue containing tasks waiting to be started.
        /// </summary>
        public ConcurrentQueue<TaskDetails> QueuedTasks
        {
            get
            {
                return _QueuedTasks;
            }
        }

        /// <summary>
        /// Event to fire when a task is added.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskAdded { get; set; }

        /// <summary>
        /// Event to fire when a task is started.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskStarted { get; set; }

        /// <summary>
        /// Event to fire when a task is canceled.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskCanceled { get; set; }

        /// <summary>
        /// Event to fire when a task is faulted.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFaulted { get; set; }

        /// <summary>
        /// Event to fire when a task is finished.
        /// </summary>
        public EventHandler<TaskDetails> OnTaskFinished { get; set; }

        /// <summary>
        /// Event to fire when processing starts.
        /// </summary>
        public EventHandler OnProcessingStarted { get; set; }

        /// <summary>
        /// Event to fire when processing stops.
        /// </summary>
        public EventHandler OnProcessingStopped { get; set; }

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

        /// <summary>
        /// Iteration delay when checking for additional tasks to run.
        /// </summary>
        public int IterationDelayMs
        {
            get
            {
                return _IterationDelayMs;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(IterationDelayMs));
                _IterationDelayMs = value;
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[TaskHandler] ";
        private int _MaxConcurrentTasks = 32;
        private ConcurrentDictionary<Guid, TaskDetails> _RunningTasks = new ConcurrentDictionary<Guid, TaskDetails>();
        private ConcurrentQueue<TaskDetails> _QueuedTasks = new ConcurrentQueue<TaskDetails>();

        private CancellationTokenSource _TaskRunnerTokenSource = new CancellationTokenSource();
        private CancellationToken _TaskRunnerToken;
        private Task _TaskRunner = null;

        private int _IterationDelayMs = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="maxConcurrentTasks">Maximum concurrent tasks.</param>
        public TaskQueue(int maxConcurrentTasks = 32)
        {
            _TaskRunnerToken = _TaskRunnerTokenSource.Token;
            
            MaxConcurrentTasks = maxConcurrentTasks;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
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

            _RunningTasks = null;
            _QueuedTasks = null;
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
            TaskDetails details = new TaskDetails
            {
                Guid = guid,
                Name = name,
                Metadata = metadata,
                Function = func
            };

            _QueuedTasks.Enqueue(details);
            OnTaskAdded?.Invoke(this, details);
            return details;
        }

        /// <summary>
        /// Start running tasks.
        /// </summary>
        public void Start()
        {
            if (_TaskRunner != null) throw new InvalidOperationException("Task runner is already started.");
            Logger?.Invoke(_Header + "starting");
            _TaskRunner = Task.Run(() => TaskRunner(_TaskRunnerToken), _TaskRunnerToken);
            OnProcessingStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Stop running tasks.
        /// </summary>
        public void Stop()
        {
            Logger?.Invoke(_Header + "stopping (" + _RunningTasks.Count + " running tasks)");
            if (_TaskRunner == null) return;
            if (_RunningTasks.Count > 0)
            {
                foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                {
                    Logger?.Invoke(_Header + "evaluating task " + task.Key.ToString());
                    if (!task.Value.TokenSource.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "canceling task " + task.Key.ToString());
                        task.Value.TokenSource.Cancel();
                        OnTaskCanceled?.Invoke(this, task.Value);
                    }
                }
            }

            if (!_TaskRunnerTokenSource.IsCancellationRequested)
            {
                _TaskRunnerTokenSource.Cancel();
            }

            OnProcessingStopped?.Invoke(this, EventArgs.Empty);
            _TaskRunner = null;
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
                OnTaskCanceled?.Invoke(this, task);
            }
            else
            {
                Logger?.Invoke(_Header + "task " + guid.ToString() + " not found");
            }
        }

        #endregion

        #region Private-Methods

        private async Task TaskRunner(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_IterationDelayMs, token);

                    #region Dequeue-and-Run

                    if ((RunningCount < MaxConcurrentTasks)
                        && _QueuedTasks.Count > 0)
                    {
                        TaskDetails task;
                        if (!_QueuedTasks.TryDequeue(out task))
                        {
                            Logger?.Invoke(_Header + "could not dequeue task from queue");
                            continue;
                        }
                        else
                        {
                            Logger?.Invoke(_Header + "dequeued task " + task.Guid.ToString());
                        }

                        _RunningTasks.TryAdd(task.Guid, task);

                        task.Task = Task.Run(() => task.Function(task.Token), task.Token);
                        Logger?.Invoke(_Header + "started task " + task.Guid.ToString() + " (" + _RunningTasks.Count + " running tasks)");
                        OnTaskStarted?.Invoke(this, task);
                    }

                    #endregion
                    
                    #region Check-for-Completed-Tasks

                    if (_RunningTasks.Count > 0)
                    {
                        ConcurrentDictionary<Guid, TaskDetails> updatedRunningTasks = new ConcurrentDictionary<Guid, TaskDetails>();
                        
                        foreach (KeyValuePair<Guid, TaskDetails> task in _RunningTasks)
                        {
                            // Logger?.Invoke(_Header + "task " + task.Key.ToString() + " status: " + task.Value.Task.Status.ToString());

                            if (task.Value.Task.Status == TaskStatus.Created
                                || task.Value.Task.Status == TaskStatus.Running
                                || task.Value.Task.Status == TaskStatus.WaitingForActivation
                                || task.Value.Task.Status == TaskStatus.WaitingForChildrenToComplete
                                || task.Value.Task.Status == TaskStatus.WaitingToRun)
                            {
                                // Logger?.Invoke(_Header + "task " + task.Key.ToString() + " still running");
                                updatedRunningTasks.TryAdd(task.Key, task.Value);
                            }
                            else if (task.Value.Task.Status == TaskStatus.RanToCompletion)
                            {
                                // do not add it to the updated list
                                Logger?.Invoke(_Header + "task " + task.Key.ToString() + " completed");
                                OnTaskFinished?.Invoke(this, task.Value);
                            }
                            else if (task.Value.Task.Status == TaskStatus.Faulted)
                            {
                                // do not add it to the updated list
                                Logger?.Invoke(_Header + "task " + task.Key.ToString() + " faulted");
                                OnTaskFaulted?.Invoke(this, task.Value);
                            }
                            else if (task.Value.Task.Status == TaskStatus.Canceled)
                            {
                                // do not add it to the updated list
                                Logger?.Invoke(_Header + "task " + task.Key.ToString() + " canceled");
                                OnTaskCanceled?.Invoke(this, task.Value);
                            }
                        }
                        
                        _RunningTasks = updatedRunningTasks;
                    }

                    #endregion
                }
                catch (TaskCanceledException)
                {
                    Logger?.Invoke(_Header + "task runner canceled");
                    OnProcessingStopped?.Invoke(this, EventArgs.Empty);
                    Dispose();

                }
                catch (OperationCanceledException)
                {
                    Logger?.Invoke(_Header + "task runner canceled");
                    OnProcessingStopped?.Invoke(this, EventArgs.Empty);
                    Dispose();
                }
                catch (Exception e)
                {
                    Logger?.Invoke(_Header + "task runner exception: " + Environment.NewLine + e.ToString());
                    OnProcessingStopped?.Invoke(this, EventArgs.Empty);
                }
            }

            Logger?.Invoke(_Header + "task runner exiting");
        }

        #endregion
    }
}