using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskHandler
{
    /// <summary>
    /// Task runner with timeout.
    /// </summary>
    public static class TaskRunWithTimeout
    {
        /// <summary>
        /// Run a task with a given timeout.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task<T> Go<T>(Task<T> task, int timeoutMs, CancellationToken token = default)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            CancellationTokenSource tokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            {
                return task.Result;
            }
            else
            {
                if (!token.IsCancellationRequested)
                {
                    tokenSource.Cancel();
                }

                throw new TimeoutException("The specified task timed out after " + timeoutMs + "ms.");
            }
        }
    }
}
