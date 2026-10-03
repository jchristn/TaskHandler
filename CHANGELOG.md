# Change Log

## v2.2.0 (Current)

**Observability:**
- `TaskQueue` and `TaskRunWithTimeout` emit metrics and traces through the BCL `Meter` and `ActivitySource` named `TaskHandler`, with no exporter or SDK dependency and near-zero cost when unobserved. All names are public constants on `TaskHandlerTelemetryNames`; see `TELEMETRY.md`
- Metrics: enqueued, rejected (by `error.type`), completed (by outcome and `error.type`), end-to-end and per-stage (`queued`, `slot_wait`, `execute`) duration histograms and stage counters, enqueue/backpressure duration, cancellation requests by reason, lifecycle events, event-handler errors, runner errors, `TaskRunWithTimeout` outcomes and duration, plus observable gauges for queue depth, capacity, concurrency in use and limit, processing state, last success, and build info
- Traces: `taskhandler enqueue` (producer), `taskhandler task` (consumer, parented to the enqueue span across the background hand-off), `stage:queued`, `stage:slot_wait`, `stage:execute` (current while the task function runs, so user spans nest under it), and `taskhandler run_with_timeout`, all with explicit status and exception events
- New `TaskQueue.Name` / `TaskQueueOptions.Name` (default `"default"`) labels each queue
- netstandard targets now reference `System.Diagnostics.DiagnosticSource` 10.0.11; library `LangVersion` raised from 8.0 to 9.0 (compile-time only)

**Fixes:**
- A result task (`EnqueueAsync<T>`) canceled before its function started, or dropped because the queue stopped while it waited for a concurrency slot, now completes its `TaskHandle<T>` as canceled instead of leaving `handle.Task` pending forever
- `EnqueueAsync` priority is now assigned before the task is written to the queue (previously set after, racing the runner)

**Testing:**
- Added an 18-case Telemetry suite (in-memory `MeterListener`/`ActivityListener`) and a regression case for dropped result handles; 136 cases total

## v2.1.1

- Fixed result-returning `EnqueueAsync<T>` overloads so a task exceeding its timeout completes the `TaskHandle<T>` with a `TimeoutException`; `TaskHandle<T>` completion made idempotent
- Updated `System.Threading.Channels` and `Microsoft.Bcl.AsyncInterfaces` to 10.0.11

## v2.1.0

**Target Framework Update:**
- Dropped .NET 6.0 target (out of support)
- Added .NET 10.0 target
- Now multi-targets netstandard2.0, netstandard2.1, net8.0, and net10.0
- All automated tests passing on all target frameworks

**Fixes:**
- Fixed `WaitForCompletionAsync()` throwing `NotSupportedException` on the default unbounded queue (the single-reader unbounded channel does not support `Reader.Count`); it now uses the tracked queue depth

**Testing:**
- Expanded the automated suite to 53 tests, adding positive and negative coverage for input validation, bounded-queue backpressure, async start/stop/dispose lifecycle, and `WaitForCompletionAsync`

## v2.0.x

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
