namespace TaskHandler
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Task runner with timeout.
    /// </summary>
    public static class TaskRunWithTimeout
    {
        /// <summary>
        /// Method to invoke to send log messages.
        /// </summary>
        public static Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Message header to prepend to each emitted log message.
        /// </summary>
        public static string LogHeader { get; set; } = "[TaskRunWithTimeout] ";

        /// <summary>
        /// Run a task with a given timeout.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        /// <returns>Task.</returns>
        public static async Task<T> Go<T>(Task<T> task, int timeoutMs, CancellationTokenSource tokenSource)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (tokenSource == null) throw new ArgumentNullException(nameof(tokenSource));

            if (await Task.WhenAny(new Task[] { task, Task.Delay(timeoutMs) }) == task)
            {
                Log("user task completed within the timeout window");
                return task.Result;
            }
            else
            {
                if (!tokenSource.IsCancellationRequested)
                {
                    Log("cancellation has not yet been requested, requesting");
                    tokenSource.Cancel();
                }
                else
                {
                    Log("cancellation has already been requested, skipping");
                }

                Log("timeout task completed before user task (user task timed out)");
                throw new TimeoutException("The specified task timed out after " + timeoutMs + "ms.");
            }
        }

        private static void Log(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;

            StringBuilder sb = new StringBuilder();
            if (!String.IsNullOrEmpty(LogHeader))
            {
                if (LogHeader.EndsWith(" ")) sb.Append(LogHeader);
                else sb.Append(LogHeader + " ");
            }

            sb.Append(msg);
            Logger?.Invoke(sb.ToString());
        }
    }
}
