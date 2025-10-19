# Change Log

## v2.0.x (Current)

**Major Version with Breaking Changes:**

**Event-Driven Architecture:**
- Event-Driven Architecture: Replaced polling with System.Threading.Channels for instant task execution
- Zero idle CPU usage
- 10-100x lower latency (sub-millisecond vs 50ms average)
- Instant task execution when capacity is available
- Async-First API: Full support for IAsyncDisposable, StartAsync, StopAsync, and WaitForCompletionAsync
- Backpressure Support: MaxQueueSize parameter prevents memory exhaustion

**Modern API Features:**
- TaskHandle<T>: Enqueue tasks that return results with EnqueueAsync<T>()
- Options Pattern: Configure queue with TaskQueueOptions or TaskQueue.Create() factory method
- Per-Task Timeout: Set individual task timeouts via timeout parameter in EnqueueAsync()
- Task Priority: Assign priorities to tasks using TaskPriority enum and priority parameter
- Immutable TaskInfo: Get read-only task information with GetRunningTasksInfo()
- Enhanced API: New EnqueueAsync() methods with priority and timeout support

**Statistics and Progress Reporting:**
- Statistics and Metrics: TaskQueueStatistics class with comprehensive performance tracking
- GetStatistics() method returns detailed metrics
- Tracks total enqueued, completed, failed, and canceled tasks
- Calculates average execution time and wait time
- Records timestamps for last task started and completed
- Maintains rolling average over last 1000 tasks
- Progress Reporting: Full IProgress<TaskProgress> support
- EnqueueAsync() overloads accepting IProgress<TaskProgress> parameter
- TaskProgress class with Current, Total, PercentComplete, and Message properties
- Works with both result-returning and non-result tasks
- Thread-safe progress reporting

**Breaking Changes:**
- REMOVED: IterationDelayMs property (no longer needed with event-driven architecture)
- REMOVED: QueuedTasks property; use QueuedCount instead
- Added AddTaskAsync method for async enqueueing when using bounded queues
- Cleaner, async-first API surface
- All 38 automated tests passing on netstandard2.0, netstandard2.1, net6.0, and net8.0

## v1.0.x

**Legacy polling-based architecture:**
- Initial releases with core functionality (v1.0.0 - v1.0.8)
- Minor bug fixes and improvements (v1.0.9)
- Critical bug fixes (v1.0.10):
  - Fixed race condition in _RunningTasks dictionary updates using Interlocked.Exchange
  - Fixed resource leak: _TaskRunnerTokenSource now properly disposed
  - Added state guards to prevent invalid operations (multiple starts, use after disposal)
  - Protected event handlers from user exceptions using SafeInvokeEvent
  - Added ConfigureAwait(false) throughout for better async performance
  - Support for multiple start/stop cycles
  - Comprehensive test suite with 20 automated tests
