namespace TaskHandler
{
    using System;
    using System.Diagnostics;
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
        /// Default: null.
        /// </summary>
        public static Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Message header to prepend to each emitted log message.
        /// Default: "[TaskRunWithTimeout] ".
        /// </summary>
        public static string LogHeader { get; set; } = "[TaskRunWithTimeout] ";

        /// <summary>
        /// Run a task with a given timeout.
        /// Emits the "taskhandler run_with_timeout" span and the taskhandler.run_with_timeout.* metrics
        /// (outcome success, failure, canceled, or timeout).
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task.</param>
        /// <param name="timeoutMs">Timeout in milliseconds. Minimum: 1.</param>
        /// <param name="tokenSource">Cancellation token source, canceled when the timeout elapses.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when timeoutMs is less than 1.</exception>
        /// <exception cref="ArgumentNullException">Thrown when tokenSource is null.</exception>
        /// <exception cref="TimeoutException">Thrown when the task does not complete within the timeout.</exception>
        public static async Task<T> Go<T>(Task<T> task, int timeoutMs, CancellationTokenSource tokenSource)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (tokenSource == null) throw new ArgumentNullException(nameof(tokenSource));

            DateTime started = DateTime.UtcNow;
            Activity activity = TaskHandlerTelemetry.StartRunWithTimeout(timeoutMs);
            string outcome = TaskHandlerTelemetryNames.OutcomeFailure;
            Exception error = null;

            try
            {
                if (await Task.WhenAny(new Task[] { task, Task.Delay(timeoutMs) }).ConfigureAwait(false) == task)
                {
                    Log("user task completed within the timeout window");
                    if (task.IsCanceled) outcome = TaskHandlerTelemetryNames.OutcomeCanceled;
                    T result = task.Result;
                    outcome = TaskHandlerTelemetryNames.OutcomeSuccess;
                    return result;
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
                    outcome = TaskHandlerTelemetryNames.OutcomeTimeout;
                    throw new TimeoutException("The specified task timed out after " + timeoutMs + "ms.");
                }
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                TaskHandlerTelemetry.RunWithTimeoutCompleted(activity, started, outcome, error);
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
