# TaskHandler Telemetry

TaskHandler (v2.2.0 and later) emits metrics and traces for every queue it runs, so an on-call engineer can tell from dashboards and traces alone **where the time went** (waiting in the queue, waiting for a concurrency slot, or running user code) and **what failed** (failure, timeout, cancellation, rejection, or drop, by error type).

TaskHandler is a **library**. It emits only through the .NET base class library:

| Signal | API | Name |
|---|---|---|
| Metrics | `System.Diagnostics.Metrics.Meter` | `TaskHandler` |
| Traces | `System.Diagnostics.ActivitySource` | `TaskHandler` |

Both carry the library version (for example `2.2.0`). TaskHandler takes **no** dependency on OpenTelemetry, Radiant, or any exporter, and never opens a connection. The host application decides whether and where to export. With no listener subscribed, spans are not created and measurements are discarded by the runtime, so the cost is effectively nothing.

All names below are constants on `TaskHandler.TaskHandlerTelemetryNames` and are a stable public contract within a major version.

## Table of Contents

- [Subscribing from a host](#subscribing-from-a-host)
- [Configuration](#configuration)
- [Metrics catalog](#metrics-catalog)
- [Spans catalog](#spans-catalog)
- [Attributes](#attributes)
- [Task lifecycle and stages](#task-lifecycle-and-stages)
- [Recommended PromQL](#recommended-promql)
- [Recommended alerts](#recommended-alerts)
- [Dashboard map](#dashboard-map)
- [Logs](#logs)
- [Design notes and limits](#design-notes-and-limits)

## Subscribing from a host

### Radiant

```csharp
using Radiant;

RadiantSettings settings = new RadiantSettings("my-service");
settings.Otlp.Endpoint = "http://127.0.0.1:4317";
settings.Prometheus.Enable = true;
settings.Sources.AddMeter("TaskHandler");          // TaskHandlerTelemetryNames.MeterName
settings.Sources.AddActivitySource("TaskHandler"); // TaskHandlerTelemetryNames.ActivitySourceName

using (RadiantHost host = RadiantHost.Start(settings))
{
    // run the application
}
```

### OpenTelemetry .NET SDK

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(TaskHandlerTelemetryNames.MeterName).AddPrometheusExporter())
    .WithTracing(t => t.AddSource(TaskHandlerTelemetryNames.ActivitySourceName).AddOtlpExporter());
```

### Raw BCL listeners (tests, custom collectors)

`MeterListener` and `ActivityListener` work directly. `src/Test.Shared/Telemetry/TelemetryCapture.cs` is a complete in-memory example.

### Histogram buckets

On net10.0 and the netstandard2.x builds, every duration histogram carries bucket advice tuned for seconds: `0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600`. The net8.0 runtime does not support instrument advice, so on net8.0 hosts configure an explicit-bucket view with those boundaries for the `taskhandler.*.duration` instruments. Otherwise the SDK default buckets (designed for milliseconds) apply.

## Configuration

| Setting | Where | Default | Effect |
|---|---|---|---|
| `Name` | `TaskQueue.Name`, `TaskQueueOptions.Name` | `"default"` | Value of the `taskhandler.queue.name` label on every queue metric and span. Must be low-cardinality: one fixed name per logical queue (for example `ingest`, `email`). Never use ids or user input. Queues sharing a name are aggregated in the gauges. |
| Metrics on/off | Host collector | Off until subscribed | Subscribe to meter `TaskHandler`. |
| Traces on/off | Host collector | Off until subscribed | Subscribe to activity source `TaskHandler`. |

TaskHandler has no telemetry endpoint, port, or credentials of its own. Exporter endpoints belong to the host (use `127.0.0.1`, not `localhost`, for loopback defaults).

## Metrics catalog

Prometheus names assume the standard OpenTelemetry Prometheus exporter, which converts dots to underscores and appends unit and `_total` suffixes.

### Throughput and outcomes

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.task.enqueued` | Counter | `{task}` | `taskhandler.queue.name` | `taskhandler_task_enqueued_total` | Tasks accepted into the queue. |
| `taskhandler.task.rejected` | Counter | `{task}` | `taskhandler.queue.name`, `error.type` | `taskhandler_task_rejected_total` | Enqueue attempts rejected. `error.type` is `queue_full` (bounded queue full on `AddTask`, or a QoSKit scheduler full under `Reject`/`DropNewest`), `queue_closed` (the queue was disposed), `unclassified` (a QoSKit scheduler could not classify the task), `canceled` (caller token canceled while waiting for space), or an exception type. |
| `taskhandler.task.completed` | Counter | `{task}` | `taskhandler.queue.name`, `taskhandler.outcome`, `error.type`* | `taskhandler_task_completed_total` | Tasks that reached a terminal state. Outcome is `success`, `failure`, `timeout`, `canceled`, or `dropped`. *`error.type` is present for `failure`, `timeout`, and `dropped` only (`dropped` carries `queue_closed` when the queue was disposed, or `queue_full` when a QoSKit `DropOldest` scheduler evicted the task). |
| `taskhandler.task.duration` | Histogram | `s` | `taskhandler.queue.name`, `taskhandler.outcome` | `taskhandler_task_duration_seconds_*` | End-to-end time from enqueue to terminal state. |
| `taskhandler.task.last_success` | Observable gauge | `s` | `taskhandler.queue.name` | `taskhandler_task_last_success_seconds` | Unix time of the most recent successful completion. Absent until the first success. |

### Per-stage (where the time went)

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.task.stage.duration` | Histogram | `s` | `taskhandler.queue.name`, `taskhandler.stage`, `taskhandler.outcome` | `taskhandler_task_stage_duration_seconds_*` | Time spent in each stage. Stage is `queued`, `slot_wait`, or `execute`. |
| `taskhandler.task.stage.events` | Counter | `{event}` | `taskhandler.queue.name`, `taskhandler.stage`, `taskhandler.outcome` | `taskhandler_task_stage_events_total` | One event per stage a task leaves. A `queued` or `slot_wait` event with outcome `dropped` means the queue was disposed while the task waited there. A task canceled with `Stop(guid)` before it started records `slot_wait` and `execute` events (`execute` with outcome `canceled` and near-zero duration) but its function never runs. |

### Queue and concurrency limiter

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.queue.depth` | Observable gauge | `{task}` | `taskhandler.queue.name` | `taskhandler_queue_depth` | Tasks accepted but not yet started: waiting in the queue, waiting for a concurrency slot, or held while the queue is stopped. Same value as `QueuedCount`. |
| `taskhandler.queue.capacity` | Observable gauge | `{task}` | `taskhandler.queue.name` | `taskhandler_queue_capacity` | Configured `MaxQueueSize`. Bounded queues only. |
| `taskhandler.queue.enqueue.duration` | Histogram | `s` | `taskhandler.queue.name`, `taskhandler.outcome` | `taskhandler_queue_enqueue_duration_seconds_*` | Time to write into the queue, including backpressure wait on a full bounded queue. Outcome `success` or `rejected`. |
| `taskhandler.concurrency.in_use` | Observable gauge | `{task}` | `taskhandler.queue.name` | `taskhandler_concurrency_in_use` | Concurrency slots in use (running tasks). |
| `taskhandler.concurrency.limit` | Observable gauge | `{task}` | `taskhandler.queue.name` | `taskhandler_concurrency_limit` | Configured `MaxConcurrentTasks`. |
| `taskhandler.queue.processing` | Observable gauge | `{state}` | `taskhandler.queue.name` | `taskhandler_queue_processing` | 1 while started and not disposed, else 0. |

The queue wait is `stage="queued"` and the limiter wait is `stage="slot_wait"`. Rejections are `taskhandler.task.rejected`.

### Lifecycle, cancellation, and errors

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.queue.lifecycle` | Counter | `{event}` | `taskhandler.queue.name`, `taskhandler.lifecycle.event` | `taskhandler_queue_lifecycle_total` | `start`, `stop`, `dispose`. |
| `taskhandler.task.cancellation_requests` | Counter | `{request}` | `taskhandler.queue.name`, `taskhandler.cancel.reason` | `taskhandler_task_cancellation_requests_total` | Cancellations issued by the queue: `stop_all` (`Stop()`), `stop_task` (`Stop(Guid)`, for a running or still-queued task), `dispose`. |
| `taskhandler.event_handler.errors` | Counter | `{error}` | `taskhandler.queue.name`, `taskhandler.event`, `error.type` | `taskhandler_event_handler_errors_total` | Exceptions thrown by user event handlers (`OnTaskAdded`, `OnTaskFinished`, ...). The queue suppresses them; this makes them visible. |
| `taskhandler.runner.errors` | Counter | `{error}` | `taskhandler.queue.name`, `error.type` | `taskhandler_runner_errors_total` | Unexpected exceptions that terminated the background task runner. Should always be zero. |

### TaskRunWithTimeout

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.run_with_timeout.operations` | Counter | `{operation}` | `taskhandler.outcome`, `error.type`* | `taskhandler_run_with_timeout_operations_total` | `TaskRunWithTimeout.Go` calls by outcome (`success`, `failure`, `canceled`, `timeout`). *`error.type` on non-success. |
| `taskhandler.run_with_timeout.duration` | Histogram | `s` | `taskhandler.outcome` | `taskhandler_run_with_timeout_duration_seconds_*` | Duration of each `Go` call. |

### Build and configuration

| Instrument | Type | Unit | Labels | Prometheus series | Description |
|---|---|---|---|---|---|
| `taskhandler.build.info` | Observable gauge | `{info}` | `taskhandler.version` | `taskhandler_build_info` | Always 1; carries the library version. |

Safe configuration is exposed through `taskhandler.concurrency.limit` and `taskhandler.queue.capacity`. Runtime metrics (GC, thread pool, CPU) are the host's responsibility: Radiant emits them, or add `OpenTelemetry.Instrumentation.Runtime` / the built-in `System.Runtime` meter on .NET 9+.

## Spans catalog

| Span name | Kind | Parent | Status | Key attributes |
|---|---|---|---|---|
| `taskhandler enqueue` | Producer | Caller's current span (for example the Watson request span) | `Ok`, or `Error` when rejected (with `exception` event when an exception caused it) | queue name, task id, task name, priority, `taskhandler.queue.depth_at_enqueue`, outcome, `error.type` |
| `taskhandler task` | Consumer | `taskhandler enqueue` (context captured at enqueue and carried across the background hand-off) | `Ok` (success), `Error` (failure, timeout, dropped), `Unset` (canceled) | queue name, task id, task name, priority, outcome, `error.type`. Start time is back-dated to the enqueue time, so its duration is the end-to-end time. |
| `stage:queued` | Internal | `taskhandler task` | `Ok` | Time from enqueue until the runner read the task. |
| `stage:slot_wait` | Internal | `taskhandler task` | `Ok`, or `Error` when dropped | Time waiting for a concurrency slot. |
| `stage:execute` | Internal | `taskhandler task` | Same rule as the task span; failures carry an `exception` event (`exception.type`, `exception.message`, `exception.stacktrace`) | `taskhandler.concurrency.limit_at_start`; `cancellation_requested` event (with `taskhandler.cancel.reason`) when the queue cancels the task. **`Activity.Current` inside the user function is this span**, so any spans, HTTP calls, or `ILogger` scopes the task creates nest under it. |
| `taskhandler run_with_timeout` | Internal | Caller's current span | `Ok`, `Error` (failure, timeout), `Unset` (canceled) | `taskhandler.timeout_ms`, outcome, `error.type`, `exception` event |

Canceled spans are left `Unset` deliberately: cancellation is usually intentional (`Stop`, `Dispose`, caller token), so it should not light up Tempo error filters. Filter on `taskhandler.outcome = canceled` to find them.

**Context propagation:** W3C trace context flows from the code that enqueues a task, through the queue, into the task's own code. The background runner deliberately detaches from whatever span was current when `Start()` was called, so a long-lived runner never stitches unrelated tasks into one trace. Propagation needs the `TaskHandler` activity source to be subscribed; without it, task code runs with no ambient span.

## Attributes

| Key | Metric label | Span | Values |
|---|---|---|---|
| `taskhandler.queue.name` | yes | yes | `TaskQueue.Name` (bounded by the developer) |
| `taskhandler.outcome` | yes | yes | `success`, `failure`, `timeout`, `canceled`, `dropped`, `rejected` |
| `taskhandler.stage` | yes | no | `queued`, `slot_wait`, `execute` |
| `taskhandler.cancel.reason` | yes | event | `stop_all`, `stop_task`, `dispose` |
| `taskhandler.lifecycle.event` | yes | no | `start`, `stop`, `dispose` |
| `taskhandler.event` | yes | no | Event handler property names, such as `OnTaskFinished` |
| `taskhandler.version` | build info only | no | Library version |
| `error.type` | yes | yes | Exception full type name, or `queue_full`, `queue_closed`, `unclassified`, `canceled` |
| `taskhandler.task.id` | **never** | yes | Task GUID |
| `taskhandler.task.name` | **never** | yes | User-supplied task name |
| `taskhandler.task.priority` | no | yes | Integer priority |
| `taskhandler.queue.depth_at_enqueue` | no | yes | Integer |
| `taskhandler.concurrency.limit_at_start` | no | yes | Integer |
| `taskhandler.timeout_ms` | no | yes | Integer |

Task ids, task names, metadata, and payloads never appear as metric labels. Metadata and task arguments are never recorded anywhere. Exception messages appear only on span `exception` events, following OpenTelemetry convention. If your exception messages can contain sensitive data, filter them in the collector.

## Task lifecycle and stages

```
AddTask / EnqueueAsync          TaskRunner                     continuation
   |  [taskhandler enqueue]        |                               |
   |--write--> queue --read------->| stage:queued ends             |
                                   |--wait for slot--> stage:slot_wait ends
                                   |--Task.Run--> stage:execute ---|--> taskhandler task ends
                                                                     outcome: success | failure | timeout | canceled
   rejected (queue_full/closed/canceled) ......... taskhandler.task.rejected
   Stop(): unstarted tasks are retained ......... no outcome until they run after Start()
   Dispose() before a task started .............. outcome dropped (from queued or slot_wait)
```

## Recommended PromQL

```promql
# Throughput by outcome
sum by (taskhandler_queue_name, taskhandler_outcome) (rate(taskhandler_task_completed_total[5m]))

# Error ratio (failure + timeout + dropped) per queue
sum by (taskhandler_queue_name) (rate(taskhandler_task_completed_total{taskhandler_outcome=~"failure|timeout|dropped"}[5m]))
  / sum by (taskhandler_queue_name) (rate(taskhandler_task_completed_total[5m]))

# p95 per stage: where the time goes
histogram_quantile(0.95, sum by (le, taskhandler_queue_name, taskhandler_stage) (rate(taskhandler_task_stage_duration_seconds_bucket[5m])))

# p95 end to end
histogram_quantile(0.95, sum by (le, taskhandler_queue_name) (rate(taskhandler_task_duration_seconds_bucket[5m])))

# Concurrency saturation (1.0 = every slot busy)
taskhandler_concurrency_in_use / taskhandler_concurrency_limit

# Bounded queue fill ratio
taskhandler_queue_depth / taskhandler_queue_capacity

# Top failure types
topk(5, sum by (taskhandler_queue_name, error_type) (rate(taskhandler_task_completed_total{taskhandler_outcome="failure"}[15m])))

# Seconds since last success
time() - taskhandler_task_last_success_seconds
```

## Recommended alerts

| Alert | Expression | For | Meaning |
|---|---|---|---|
| TaskHandlerHighErrorRatio | error ratio query above `> 0.05` | 10m | More than 5% of tasks fail, time out, or are dropped. |
| TaskHandlerQueueBacklog | `taskhandler_queue_depth > 1000` (tune per queue) | 10m | Work is arriving faster than it is processed. |
| TaskHandlerSaturated | `taskhandler_concurrency_in_use / taskhandler_concurrency_limit >= 1` | 15m | All slots busy for a sustained period; `slot_wait` will grow. |
| TaskHandlerSlowQueueWait | p95 of `stage="queued"` or `stage="slot_wait"` `> 30` | 10m | Tasks wait too long before running. |
| TaskHandlerRejections | `sum by (taskhandler_queue_name) (rate(taskhandler_task_rejected_total[5m])) > 0` | 5m | Backpressure is rejecting work. |
| TaskHandlerStalled | `time() - taskhandler_task_last_success_seconds > 900 and taskhandler_queue_depth > 0` | 5m | Work is queued but nothing has succeeded for 15 minutes. |
| TaskHandlerNotProcessing | `taskhandler_queue_processing == 0 and taskhandler_queue_depth > 0` | 5m | Tasks are queued but the queue is stopped. |
| TaskHandlerRunnerCrashed | `increase(taskhandler_runner_errors_total[5m]) > 0` | 0m | The background runner died unexpectedly. |
| TaskHandlerEventHandlerErrors | `increase(taskhandler_event_handler_errors_total[15m]) > 0` | 0m | Application event handlers are throwing (suppressed by the queue). |

## Dashboard map

TaskHandler is a library, so it ships no Grafana stack or dashboards. A host service that embeds it should add a **Task Queues** dashboard to its product folder, built from the queries above:

| Row | Panels |
|---|---|
| Overview | Processing state, throughput by outcome, error ratio, seconds since last success |
| Where the time went | p50/p95/p99 by stage (`queued`, `slot_wait`, `execute`), end-to-end p95 |
| Capacity | Depth vs capacity, in-use vs limit, enqueue backpressure p95, rejections by `error.type` |
| Failures | Completions by outcome and `error.type`, cancellations by reason, event handler errors, runner errors |
| Exemplars and traces | Tempo search `{ name = "taskhandler task" && status = error }` |

## Logs

TaskHandler has no logging dependency. Its existing `Logger` callback (`Action<string>`) is unchanged. A host that routes it into `ILogger` gets trace correlation for task code automatically, because `Activity.Current` inside a task is the `stage:execute` span. TaskHandler does not ship Loki configuration; that belongs to the host service.

## Design notes and limits

- **Best effort:** every recording path catches its own exceptions. Telemetry can never fail, delay, or alter a task.
- **Observable gauges** are backed by a registry of weak references to live queues. A disposed queue stops reporting, and a queue that is garbage-collected without `Dispose()` is pruned automatically.
- **Stages with a QoSKit scheduler.** The default FIFO queue reads the next task and then waits for a slot, so `stage:queued` and `stage:slot_wait` are distinct. With a scheduler, the runner waits for a free slot first and then asks the scheduler, so a later high-priority task can overtake waiting work. Wait time therefore lands in `stage:queued`, and `stage:slot_wait` is near zero. `taskhandler.queue.depth` and `taskhandler.queue.capacity` report the scheduler's waiting tasks and `MaxDepth`. QoSKit also emits its own scheduler telemetry under the meter and activity source `QoSKit`, which hosts can subscribe to separately.
- **`Stop()` does not drop tasks.** Tasks that have not started (including one already waiting for a slot) are retained and run after the next `Start()`, so they stay in `taskhandler.queue.depth`. Their `stage:queued` time includes the pause. The one task that had already reached `stage:slot_wait` keeps its `taskhandler task` and `stage:slot_wait` spans open while stopped, so its slot-wait duration includes the pause.
- **`dropped` means disposed or evicted.** `Dispose()` completes every unstarted task with outcome `dropped` and `error.type` `queue_closed`, from whichever stage it was in. A QoSKit scheduler with the `DropOldest` policy evicts its oldest waiting task as `dropped` with `error.type` `queue_full` (from the `queued` stage). A task that was never read from the queue still gets a `taskhandler task` span, back-dated to its enqueue time, with `Error` status.
- **`timeout` detection** classifies any task that ends with `TimeoutException` (including one thrown by user code) as `timeout`.
- **Percentiles** come from histogram buckets in Prometheus/Grafana. TaskHandler never computes quantiles in process.
