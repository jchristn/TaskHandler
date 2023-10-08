using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        /// <returns>Task.</returns>
        public static async Task<T> Go<T>(Task<T> task, int timeoutMs)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
             
            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            {
                return task.Result;
            }
            else
            {
                throw new TimeoutException("The specified task timed out after " + timeoutMs + "ms.");
            }
        }
    }
}
