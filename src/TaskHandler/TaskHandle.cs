namespace TaskHandler
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Handle to an enqueued task with result.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public class TaskHandle<T>
    {
        /// <summary>
        /// Task identifier.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Task name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Task that will complete with the result.
        /// </summary>
        public Task<T> Task => _Tcs.Task;

        private readonly TaskCompletionSource<T> _Tcs = new TaskCompletionSource<T>();

        internal TaskHandle(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        internal void SetResult(T result) => _Tcs.SetResult(result);
        internal void SetException(Exception ex) => _Tcs.SetException(ex);
        internal void SetCanceled() => _Tcs.SetCanceled();
    }
}
