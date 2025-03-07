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
        private static TaskQueue _TaskQueue = new TaskQueue();
        private static int _NumTasks = 6;

        public static void Main(string[] args)
        {
            /*
            _TaskQueue.Logger = Console.WriteLine;
            _TaskQueue.OnTaskAdded += OnTaskAdded;
            _TaskQueue.OnTaskStarted += OnTaskStarted;
            _TaskQueue.OnTaskFinished += OnTaskFinished;
            _TaskQueue.OnTaskFaulted += OnTaskFaulted;
            _TaskQueue.OnTaskCanceled += OnTaskCanceled;
            _TaskQueue.OnProcessingStarted += OnProcessingStarted;
            _TaskQueue.OnProcessingStopped += OnProcessingStopped;
            */

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
                    case "queue":
                        ShowQueue();
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
            Console.WriteLine("  queue           Display the current queue of tasks waiting to execute");
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
            Console.WriteLine("Tasks in queue: " + _TaskQueue.QueuedTasks.Count);

            foreach (TaskDetails task in _TaskQueue.QueuedTasks)
            {
                Console.WriteLine(task.Guid.ToString() + ": " + task.Name);
            }

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
    }
}