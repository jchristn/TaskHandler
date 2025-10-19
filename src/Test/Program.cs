namespace Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using GetSomeInput;
    using TaskHandler;

    public static class Program
    {
        private static bool _RunForever = true;
        private static TaskQueue _TaskQueue = TaskQueue.Create(options =>
        {
            options.MaxConcurrentTasks = 32;
            options.Logger = Console.WriteLine;
            options.OnTaskAdded = OnTaskAdded;
            options.OnTaskStarted = OnTaskStarted;
            options.OnTaskFinished = OnTaskFinished;
            options.OnTaskFaulted = OnTaskFaulted;
            options.OnTaskCanceled = OnTaskCanceled;
            options.OnProcessingStarted = OnProcessingStarted;
            options.OnProcessingStopped = OnProcessingStopped;
        });
        private static int _NumTasks = 6;

        public static async Task Main(string[] args)
        {

            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);
                switch (userInput)
                {
                    case "q":
                        _RunForever = false;
                        break;
                    case "c":
                    case "cls":
                        Console.Clear();
                        break;
                    case "?":
                        Menu();
                        break;
                    case "add":
                        AddTasks();
                        break;
                    case "add async":
                        await AddTasksAsync();
                        break;
                    case "add result":
                        await AddTaskWithResult();
                        break;
                    case "add timeout":
                        await AddTaskWithTimeout();
                        break;
                    case "queue":
                        ShowQueue();
                        break;
                    case "info":
                        ShowTaskInfo();
                        break;
                    case "start":
                        _TaskQueue.Start();
                        break;
                    case "stop all":
                        _TaskQueue.Stop();
                        break;
                    case "stop":
                        StopTask();
                        break;
                    case "running":
                        Console.WriteLine(_TaskQueue.RunningCount + " running task(s)");
                        break;
                    case "stats":
                        ShowStatistics();
                        break;
                    case "add progress":
                        await AddTaskWithProgress();
                        break;
                    case "monitor":
                        await MonitorQueueStatistics();
                        break;
                }
            }
        }

        private static void OnProcessingStopped(object sender, EventArgs e)
        {
            Console.WriteLine("*** Processing stopped");
        }

        private static void OnProcessingStarted(object sender, EventArgs e)
        {
            Console.WriteLine("*** Processing started");
        }

        private static void OnTaskAdded(object sender, TaskDetails e)
        {
            Console.WriteLine("*** Task added: " + e.Name);
        }

        private static void OnTaskFinished(object sender, TaskDetails e)
        {
            Console.WriteLine("*** Task finished: " + e.Name);
        }

        private static void OnTaskStarted(object sender, TaskDetails e)
        {
            Console.WriteLine("*** Task started: " + e.Name);
        }

        private static void OnTaskCanceled(object sender, TaskDetails e)
        {
            Console.WriteLine("*** Task canceled: " + e.Name);
        }

        private static void OnTaskFaulted(object sender, TaskDetails e)
        {
            Console.WriteLine("*** Task faulted: " + e.Name);
        }

        private static void Menu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            Console.WriteLine("  q               Quit this program");
            Console.WriteLine("  cls             Clear the screen");
            Console.WriteLine("  ?               Help, this menu");
            Console.WriteLine("  add             Add " + _NumTasks + " tasks that wait 5, 10, 20, or 30 seconds");
            Console.WriteLine("  add async       Add tasks using async EnqueueAsync method");
            Console.WriteLine("  add result      Add a task that returns a result using TaskHandle<T>");
            Console.WriteLine("  add timeout     Add a task with timeout support");
            Console.WriteLine("  add progress    Add a task with progress reporting");
            Console.WriteLine("  queue           Display the current queue of tasks waiting to execute");
            Console.WriteLine("  info            Display detailed task information using GetRunningTasksInfo()");
            Console.WriteLine("  stats           Display queue statistics");
            Console.WriteLine("  monitor         Monitor queue statistics in real-time");
            Console.WriteLine("  start           Start processing tasks");
            Console.WriteLine("  stop all        Stop all running tasks");
            Console.WriteLine("  stop            Stop a specific task");
            Console.WriteLine("  running         Show number of running tasks");
            Console.WriteLine("");
        }

        private static void AddTasks()
        {
            for (int i = 0; i < _NumTasks; i++)
            {
                int wait = 5000;
                if (i % 2 == 0) wait = 10000;
                else if (i % 3 == 0) wait = 20000;
                else if (i % 5 == 0) wait = 30000;

                Guid guid = Guid.NewGuid();

                string name = "Task." + guid.ToString() + "." + wait.ToString();

                Func<CancellationToken, Task> action1 = async (CancellationToken token) =>
                {
                    Console.WriteLine(name + " running");
                    try
                    {
                        int waited = 0;
                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                Console.WriteLine("!!! " + name + " cancellation requested");
                                break;
                            }
                            await Task.Delay(100, token);
                            waited += 100;
                            if (waited >= wait) break;
                        }
                        Console.WriteLine(name + " finished");
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine(name + " task canceled exception");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine(name + " operation canceled exception");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception in task GUID " + guid.ToString() + ":" + Environment.NewLine + e.ToString());
                    }
                };

                _TaskQueue.AddTask(guid, name, new Dictionary<string, object>(), action1);
            }
        }

        private static void ShowQueue()
        {
            Console.WriteLine("");
            Console.WriteLine("Tasks in queue: " + _TaskQueue.QueuedCount);
            Console.WriteLine("Tasks running: " + _TaskQueue.RunningCount);
            Console.WriteLine("");
        }

        private static void StopTask()
        {
            string guidStr = Inputty.GetString("GUID:", null, true);
            if (String.IsNullOrEmpty(guidStr)) return;

            if (Guid.TryParse(guidStr, out Guid guid))
            {
                _TaskQueue.Stop(guid);
            }
        }

        private static async Task AddTasksAsync()
        {
            Console.WriteLine("Adding tasks using EnqueueAsync...");
            for (int i = 0; i < _NumTasks; i++)
            {
                int wait = 5000;
                if (i % 2 == 0) wait = 10000;
                else if (i % 3 == 0) wait = 20000;
                else if (i % 5 == 0) wait = 30000;

                string name = $"AsyncTask-{i}-{wait}ms";
                int priority = (int)TaskPriority.Normal;

                Guid taskId = await _TaskQueue.EnqueueAsync(
                    name,
                    async (CancellationToken token) =>
                    {
                        Console.WriteLine($"{name} running");
                        await Task.Delay(wait, token);
                        Console.WriteLine($"{name} finished");
                    },
                    priority: priority
                );

                Console.WriteLine($"Enqueued task {taskId} with priority {priority}");
            }
        }

        private static async Task AddTaskWithResult()
        {
            Console.WriteLine("Adding task with result using TaskHandle<T>...");

            TaskHandle<int> handle = await _TaskQueue.EnqueueAsync(
                "ResultTask",
                async (CancellationToken token) =>
                {
                    Console.WriteLine("ResultTask: Computing result...");
                    await Task.Delay(2000, token);
                    int result = new Random().Next(1, 100);
                    Console.WriteLine($"ResultTask: Computed result = {result}");
                    return result;
                }
            );

            Console.WriteLine($"Task enqueued with ID {handle.Id}, waiting for result...");

            int value = await handle.Task;
            Console.WriteLine($"Task completed with result: {value}");
        }

        private static async Task AddTaskWithTimeout()
        {
            Console.WriteLine("Adding task with timeout...");

            int timeoutMs = Inputty.GetInteger("Timeout in milliseconds:", 3000, true, false);
            int delayMs = Inputty.GetInteger("Task delay in milliseconds:", 5000, true, false);

            Guid taskId = await _TaskQueue.EnqueueAsync(
                $"TimeoutTask-{delayMs}ms",
                async (CancellationToken token) =>
                {
                    Console.WriteLine($"TimeoutTask: Starting with {delayMs}ms delay...");
                    await Task.Delay(delayMs, token);
                    Console.WriteLine("TimeoutTask: Completed successfully");
                },
                timeout: TimeSpan.FromMilliseconds(timeoutMs)
            );

            Console.WriteLine($"Task {taskId} enqueued with {timeoutMs}ms timeout");
            if (delayMs > timeoutMs)
            {
                Console.WriteLine("  (Task will timeout before completing)");
            }
        }

        private static void ShowTaskInfo()
        {
            Console.WriteLine("");
            Console.WriteLine("=== Running Tasks Info (GetRunningTasksInfo()) ===");
            IReadOnlyCollection<TaskInfo> tasks = _TaskQueue.GetRunningTasksInfo();

            if (tasks.Count == 0)
            {
                Console.WriteLine("No tasks currently running.");
            }
            else
            {
                foreach (TaskInfo task in tasks)
                {
                    Console.WriteLine($"  ID: {task.Id}");
                    Console.WriteLine($"  Name: {task.Name}");
                    Console.WriteLine($"  Status: {task.Status}");
                    Console.WriteLine($"  Priority: {task.Priority}");
                    Console.WriteLine($"  Metadata count: {task.Metadata.Count}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine($"Total running: {tasks.Count}");
            Console.WriteLine($"Total queued: {_TaskQueue.QueuedCount}");
            Console.WriteLine("");
        }

        private static void ShowStatistics()
        {
            Console.WriteLine("");
            Console.WriteLine("=== Queue Statistics (GetStatistics()) ===");
            TaskQueueStatistics stats = _TaskQueue.GetStatistics();

            Console.WriteLine($"Total Enqueued:         {stats.TotalEnqueued}");
            Console.WriteLine($"Total Completed:        {stats.TotalCompleted}");
            Console.WriteLine($"Total Failed:           {stats.TotalFailed}");
            Console.WriteLine($"Total Canceled:         {stats.TotalCanceled}");
            Console.WriteLine($"Current Queue Depth:    {stats.CurrentQueueDepth}");
            Console.WriteLine($"Currently Running:      {stats.CurrentRunningCount}");
            Console.WriteLine($"Average Execution Time: {stats.AverageExecutionTime.TotalMilliseconds:F2}ms");
            Console.WriteLine($"Average Wait Time:      {stats.AverageWaitTime.TotalMilliseconds:F2}ms");
            Console.WriteLine($"Last Task Started:      {stats.LastTaskStarted?.ToString() ?? "N/A"}");
            Console.WriteLine($"Last Task Completed:    {stats.LastTaskCompleted?.ToString() ?? "N/A"}");

            // Calculate completion rate
            if (stats.TotalEnqueued > 0)
            {
                double completionRate = (double)stats.TotalCompleted / stats.TotalEnqueued * 100.0;
                Console.WriteLine($"Completion Rate:        {completionRate:F1}%");
            }

            Console.WriteLine("");
        }

        private static async Task AddTaskWithProgress()
        {
            Console.WriteLine("Adding task with progress reporting...");

            int steps = Inputty.GetInteger("Number of steps:", 10, true, false);
            int delayPerStep = Inputty.GetInteger("Delay per step (ms):", 500, true, false);

            Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
            {
                Console.WriteLine($"  Progress: {p.Current}/{p.Total} ({p.PercentComplete:F1}%) - {p.Message}");
            });

            TaskHandle<string> handle = await _TaskQueue.EnqueueAsync(
                "ProgressTask",
                async (CancellationToken token, IProgress<TaskProgress> prog) =>
                {
                    Console.WriteLine($"ProgressTask: Starting {steps} steps...");

                    for (int i = 0; i <= steps; i++)
                    {
                        prog?.Report(new TaskProgress(i, steps, $"Processing step {i} of {steps}"));
                        await Task.Delay(delayPerStep, token);
                    }

                    return $"Completed {steps} steps successfully";
                },
                progress
            );

            Console.WriteLine($"Task enqueued with ID {handle.Id}, monitoring progress...");

            string result = await handle.Task;
            Console.WriteLine($"Task completed: {result}");
        }

        private static async Task MonitorQueueStatistics()
        {
            Console.WriteLine("");
            Console.WriteLine("=== Monitoring Queue Statistics ===");
            Console.WriteLine("Press any key to stop monitoring...");
            Console.WriteLine("");

            CancellationTokenSource cts = new CancellationTokenSource();

            // Start monitoring in background
            Task monitorTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    TaskQueueStatistics stats = _TaskQueue.GetStatistics();

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] " +
                        $"Enqueued: {stats.TotalEnqueued} | " +
                        $"Completed: {stats.TotalCompleted} | " +
                        $"Failed: {stats.TotalFailed} | " +
                        $"Canceled: {stats.TotalCanceled} | " +
                        $"Running: {stats.CurrentRunningCount} | " +
                        $"Queued: {stats.CurrentQueueDepth} | " +
                        $"Avg Exec: {stats.AverageExecutionTime.TotalMilliseconds:F0}ms");

                    try
                    {
                        await Task.Delay(2000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, cts.Token);

            // Wait for user to press a key
            Console.ReadKey(true);
            cts.Cancel();

            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            Console.WriteLine("");
            Console.WriteLine("Monitoring stopped.");
            Console.WriteLine("");
        }
    }
}