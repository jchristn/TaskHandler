<img src="https://raw.githubusercontent.com/jchristn/TaskHandler/main/Assets/logo.png" width="250" height="250">

# TaskHandler

[![NuGet Version](https://img.shields.io/nuget/v/TaskHandler.svg?style=flat)](https://www.nuget.org/packages/TaskHandler/) [![NuGet](https://img.shields.io/nuget/dt/TaskHandler.svg)](https://www.nuget.org/packages/TaskHandler)

TaskHandler is a simple, lightweight C# library for managing asynchronous task queues with precise concurrency control. It provides a clean API for queuing tasks, controlling how many run concurrently, and monitoring their lifecycle—all without relinquishing control of your application.

## Table of Contents

- [What is TaskHandler?](#what-is-taskhandler)
- [Key Benefits](#key-benefits)
- [Use Cases](#use-cases)
  - [API Rate Limiting](#api-rate-limiting)
  - [Batch Database Operations](#batch-database-operations)
  - [Parallel File Processing](#parallel-file-processing)
  - [Web Scraping with Throttling](#web-scraping-with-throttling)
- [Installation](#installation)
- [Quick Start](#quick-start)
  - [Basic Usage](#basic-usage)
  - [With Event Handlers](#with-event-handlers)
  - [Canceling Tasks](#canceling-tasks)
  - [Task with Timeout](#task-with-timeout)
- [API Reference](#api-reference)
  - [TaskQueue Class](#taskqueue-class)
  - [TaskDetails Class](#taskdetails-class)
  - [TaskRunWithTimeout Class](#taskrunwithtimeout-class)
  - [Using Async API](#using-async-api)
  - [Backpressure and Queue Limits](#backpressure-and-queue-limits)
- [Advanced Topics](#advanced-topics)
  - [Understanding Task Lifecycle](#understanding-task-lifecycle)
  - [Concurrency Control Details](#concurrency-control-details)
  - [Exception Handling Best Practices](#exception-handling-best-practices)
    - [Overview of Exception Handling Mechanisms](#overview-of-exception-handling-mechanisms)
    - [When to Use Each Approach](#when-to-use-each-approach)
    - [1. Global Error Handling with OnTaskFaulted](#1-global-error-handling-with-ontaskfaulted)
    - [2. Per-Task Error Handling with TaskHandle\<T\>](#2-per-task-error-handling-with-taskhandlet)
    - [3. Using Both Mechanisms Together](#3-using-both-mechanisms-together-recommended-for-production)
    - [4. Defensive Task Implementation](#4-defensive-task-implementation)
    - [5. Structured Logging with Full Exception Details](#5-structured-logging-with-full-exception-details)
    - [6. Statistics-Based Failure Monitoring](#6-statistics-based-failure-monitoring)
    - [7. Retry Logic Pattern](#7-retry-logic-pattern)
    - [8. Circuit Breaker Pattern](#8-circuit-breaker-pattern)
    - [9. Event Handler Exception Safety](#9-event-handler-exception-safety)
    - [10. Handling Multiple Exception Types](#10-handling-multiple-exception-types)
    - [Summary of Best Practices](#summary-of-best-practices)
  - [Metadata Usage](#metadata-usage)
  - [Multiple Start/Stop Cycles](#multiple-startstop-cycles)
  - [Graceful Shutdown](#graceful-shutdown)
- [Framework Support](#framework-support)
- [Testing](#testing)
- [Advanced Features](#advanced-features)
  - [TaskHandle\<T\> - Tasks with Results](#taskhandlet---tasks-with-results)
  - [Options Pattern - Fluent Configuration](#options-pattern---fluent-configuration)
  - [Per-Task Timeout Support](#per-task-timeout-support)
  - [Task Priority](#task-priority)
  - [GetRunningTasksInfo() - Immutable Task Information](#getrunningtasksinfo---immutable-task-information)
  - [Combined Example - Advanced Features](#combined-example---advanced-features)
- [Statistics and Progress Reporting](#statistics-and-progress-reporting)
  - [Statistics and Metrics](#statistics-and-metrics)
  - [Progress Reporting](#progress-reporting)
  - [Combined Example - Statistics + Progress](#combined-example---statistics--progress)
- [Feedback and Issues](#feedback-and-issues)
- [Contributing](#contributing)
- [License](#license)

## What is TaskHandler?

TaskHandler helps you manage concurrent asynchronous operations in scenarios where you need to:
- **Control concurrency**: Limit how many tasks run simultaneously to prevent resource exhaustion
- **Queue tasks**: Add tasks to a queue that automatically processes them as capacity becomes available
- **Monitor execution**: Track task lifecycle through events (added, started, completed, faulted, canceled)
- **Maintain control**: Unlike fire-and-forget approaches, TaskHandler gives you full visibility and control

## Key Benefits

### 🎯 **Precise Concurrency Control**
Set a maximum number of concurrent tasks (e.g., 10 simultaneous operations) and TaskHandler ensures this limit is never exceeded. Perfect for:
- Rate-limiting API calls
- Managing database connection pools
- Controlling parallel file I/O operations
- Throttling resource-intensive operations

### 🔄 **Automatic Queue Management**
Tasks are automatically dequeued and executed as soon as capacity becomes available. No manual thread management or complex coordination code required.

### 📊 **Complete Visibility**
Monitor every stage of task execution with built-in events:
- `OnTaskAdded`: When a task enters the queue
- `OnTaskStarted`: When a task begins execution
- `OnTaskFinished`: When a task completes successfully
- `OnTaskFaulted`: When a task throws an exception
- `OnTaskCanceled`: When a task is canceled

### 🛡️ **Robust Error Handling**
- Tasks that fault don't crash your application
- Exceptions in event handlers are caught and logged
- Safe disposal ensures clean shutdown
- State guards prevent invalid operations

### ⏱️ **Timeout Support**
Built-in `TaskRunWithTimeout` utility for running any task with a timeout, automatically canceling tasks that exceed their time limit.

## Use Cases

### API Rate Limiting
```csharp
using System;
using TaskHandler;

// Limit to 5 concurrent API calls to respect rate limits
using (TaskQueue apiQueue = new TaskQueue(5))
{
    apiQueue.Start();

    foreach (var item in itemsToProcess)
    {
        apiQueue.AddTask(
            Guid.NewGuid(),
            $"Process-{item.Id}",
            null,
            async (token) => await ProcessApiCall(item, token)
        );
    }
}
```

### Batch Database Operations
```csharp
using System;
using System.Collections.Generic;
using TaskHandler;

// Process 1000 records with only 10 concurrent database connections
using (TaskQueue dbQueue = new TaskQueue(10))
{
    dbQueue.Logger = Console.WriteLine;
    dbQueue.Start();

    foreach (var record in records)
    {
        dbQueue.AddTask(
            Guid.NewGuid(),
            $"Save-{record.Id}",
            new Dictionary<string, object> { { "RecordId", record.Id } },
            async (token) => await SaveToDatabase(record, token)
        );
    }
}
```

### Parallel File Processing
```csharp
using System;
using System.IO;
using TaskHandler;

// Process files with 8 concurrent operations
using (TaskQueue fileQueue = new TaskQueue(8))
{
    fileQueue.OnTaskFinished += (sender, task) =>
    {
        Console.WriteLine($"Completed: {task.Name}");
    };
    fileQueue.Start();

    foreach (var file in filesToProcess)
    {
        fileQueue.AddTask(
            Guid.NewGuid(),
            $"Process-{Path.GetFileName(file)}",
            null,
            async (token) => await ProcessFile(file, token)
        );
    }
}
```

### Web Scraping with Throttling
```csharp
using System;
using TaskHandler;

// Scrape websites with controlled concurrency to be respectful
using (TaskQueue scrapeQueue = new TaskQueue(3))
{
    scrapeQueue.Start();

    foreach (var url in urlsToScrape)
    {
        scrapeQueue.AddTask(
            Guid.NewGuid(),
            $"Scrape-{url}",
            null,
            async (token) => await ScrapeWebsite(url, token)
        );
    }
}
```

## Installation

Install via NuGet:
```bash
dotnet add package TaskHandler
```

Or using the Package Manager Console:
```powershell
Install-Package TaskHandler
```

## Quick Start

### Basic Usage

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaskHandler;

// Create a queue allowing 16 concurrent tasks (default is 32)
using (TaskQueue queue = new TaskQueue(16))
{
    // Add tasks to the queue
    for (int i = 0; i < 100; i++)
    {
        int taskNum = i;
        queue.AddTask(
            Guid.NewGuid(),
            $"Task-{taskNum}",
            new Dictionary<string, object> { { "TaskNumber", taskNum } },
            async (CancellationToken token) =>
            {
                Console.WriteLine($"Task {taskNum} starting");
                await Task.Delay(1000, token); // Simulate work
                Console.WriteLine($"Task {taskNum} completed");
            }
        );
    }

    // Start processing the queue
    queue.Start();

    // Check status
    Console.WriteLine($"Running: {queue.RunningCount}");
    Console.WriteLine($"Queued: {queue.QueuedCount}");

    // Wait for user input, then stop
    Console.ReadLine();
    queue.Stop();
}
```

### With Event Handlers

```csharp
using System;
using TaskHandler;

using (TaskQueue queue = new TaskQueue(10))
{
    // Optional: Enable logging
    queue.Logger = Console.WriteLine;

    // Track task lifecycle
    queue.OnTaskAdded += (sender, task) =>
    {
        Console.WriteLine($"[ADDED] {task.Name}");
    };

    queue.OnTaskStarted += (sender, task) =>
    {
        Console.WriteLine($"[STARTED] {task.Name} at {DateTime.Now}");
    };

    queue.OnTaskFinished += (sender, task) =>
    {
        Console.WriteLine($"[FINISHED] {task.Name}");
    };

    queue.OnTaskFaulted += (sender, task) =>
    {
        Console.WriteLine($"[FAULTED] {task.Name}");
        if (task.Task.Exception != null)
        {
            Console.WriteLine($"  Error: {task.Task.Exception.InnerException?.Message}");
        }
    };

    queue.OnTaskCanceled += (sender, task) =>
    {
        Console.WriteLine($"[CANCELED] {task.Name}");
    };

    queue.OnProcessingStarted += (sender, e) =>
    {
        Console.WriteLine("[QUEUE] Processing started");
    };

    queue.OnProcessingStopped += (sender, e) =>
    {
        Console.WriteLine("[QUEUE] Processing stopped");
    };

    // Start processing and add tasks...
}
```

### Canceling Tasks

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

using (TaskQueue queue = new TaskQueue())
{
    // Cancel a specific task by GUID
    Guid taskId = Guid.NewGuid();
    queue.AddTask(taskId, "LongRunningTask", null, async (token) =>
    {
        await Task.Delay(10000, token); // Will be canceled before completion
    });

    queue.Start();
    await Task.Delay(2000);

    // Cancel the specific task
    queue.Stop(taskId);

    // Or cancel all running tasks
    queue.Stop();
}
```

### Task with Timeout

`TaskRunWithTimeout.Go<T>` accepts any `Task<T>` regardless of the function signature that created it:

```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaskHandler;

// Example 1: Simple Task<string>
using (CancellationTokenSource cts = new CancellationTokenSource())
{
    Func<CancellationToken, Task<string>> quickTask = async (token) =>
    {
        await Task.Delay(500, token);
        return "Success";
    };

    string result = await TaskRunWithTimeout.Go(quickTask(cts.Token), 2000, cts);
    Console.WriteLine(result); // "Success"
}

// Example 2: Multiple input parameters, different output type
using (CancellationTokenSource cts = new CancellationTokenSource())
{
    Func<string, int, CancellationToken, Task<double>> complexTask = async (name, count, token) =>
    {
        await Task.Delay(100, token);
        double result = count * 3.14;
        Console.WriteLine($"Processing {name}");
        return result;
    };

    double value = await TaskRunWithTimeout.Go(complexTask("test", 10, cts.Token), 1000, cts);
    Console.WriteLine(value); // 31.4
}

// Example 3: HTTP request with complex signature
using (CancellationTokenSource cts = new CancellationTokenSource())
{
    Func<HttpClient, string, int, CancellationToken, Task<string>> fetchTask = async (client, url, retries, token) =>
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return await client.GetStringAsync(url);
            }
            catch when (i < retries - 1)
            {
                await Task.Delay(100, token);
            }
        }
        throw new Exception("All retries failed");
    };

    using (HttpClient client = new HttpClient())
    {
        string html = await TaskRunWithTimeout.Go(
            fetchTask(client, "https://example.com", 3, cts.Token),
            5000,
            cts
        );
    }
}

// Example 4: Custom class return type
public class ProcessResult
{
    public int ItemsProcessed { get; set; }
    public TimeSpan Duration { get; set; }
}

using (CancellationTokenSource cts = new CancellationTokenSource())
{
    Func<List<string>, bool, CancellationToken, Task<ProcessResult>> processTask = async (items, parallel, token) =>
    {
        DateTime start = DateTime.UtcNow;
        // Process items...
        await Task.Delay(200, token);
        return new ProcessResult
        {
            ItemsProcessed = items.Count,
            Duration = DateTime.UtcNow - start
        };
    };

    ProcessResult result = await TaskRunWithTimeout.Go(
        processTask(myItems, true, cts.Token),
        3000,
        cts
    );
    Console.WriteLine($"Processed {result.ItemsProcessed} items in {result.Duration.TotalMilliseconds}ms");
}

// Example 5: Task that exceeds timeout
using (CancellationTokenSource cts = new CancellationTokenSource())
{
    Func<CancellationToken, Task<string>> slowTask = async (token) =>
    {
        await Task.Delay(5000, token);
        return "Should not reach here";
    };

    try
    {
        string result = await TaskRunWithTimeout.Go(slowTask(cts.Token), 1000, cts);
    }
    catch (TimeoutException ex)
    {
        Console.WriteLine($"Task timed out: {ex.Message}");
    }
}
```

## API Reference

### TaskQueue Class

#### Constructors
```csharp
TaskQueue(int maxConcurrentTasks = 32, int maxQueueSize = -1)
```
Creates a new task queue with the specified maximum concurrent task limit and optional queue size limit.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `MaxConcurrentTasks` | `int` | Maximum number of tasks that can run concurrently. Minimum: 1, Default: 32 |
| `MaxQueueSize` | `int` | Maximum queue size. -1 for unbounded (default). Prevents memory exhaustion when tasks arrive faster than they can be processed |
| `RunningCount` | `int` | Number of currently running tasks (read-only) |
| `QueuedCount` | `int` | Number of tasks waiting in the queue (read-only) |
| `RunningTasks` | `ConcurrentDictionary<Guid, TaskDetails>` | Dictionary of currently running tasks (read-only) |
| `IsRunning` | `bool` | Whether the task runner is currently active (read-only) |
| `Logger` | `Action<string>` | Optional callback for log messages |

#### Methods

| Method | Description |
|--------|-------------|
| `AddTask(Guid guid, string name, Dictionary<string, object> metadata, Func<CancellationToken, Task> func)` | Adds a task to the queue synchronously. Returns `TaskDetails`. May throw if bounded queue is full |
| `AddTaskAsync(Guid guid, string name, Dictionary<string, object> metadata, Func<CancellationToken, Task> func, CancellationToken cancellationToken)` | Adds a task to the queue asynchronously. Waits if bounded queue is full. Returns `Task<TaskDetails>` |
| `Start()` | Starts processing tasks from the queue |
| `StartAsync(CancellationToken cancellationToken)` | Starts processing tasks asynchronously. Returns `Task` |
| `Stop()` | Stops processing and cancels all running tasks |
| `Stop(Guid guid)` | Cancels a specific task by its GUID |
| `StopAsync(bool waitForCompletion, CancellationToken cancellationToken)` | Stops processing asynchronously. Optionally waits for running tasks to complete |
| `WaitForCompletionAsync(CancellationToken cancellationToken)` | Waits until the queue is empty and all tasks have finished |
| `Dispose()` | Stops the queue and releases resources |
| `DisposeAsync()` | Stops the queue asynchronously and releases resources. Returns `ValueTask` |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `OnTaskAdded` | `EventHandler<TaskDetails>` | Fired when a task is added to the queue |
| `OnTaskStarted` | `EventHandler<TaskDetails>` | Fired when a task begins execution |
| `OnTaskFinished` | `EventHandler<TaskDetails>` | Fired when a task completes successfully |
| `OnTaskFaulted` | `EventHandler<TaskDetails>` | Fired when a task throws an exception |
| `OnTaskCanceled` | `EventHandler<TaskDetails>` | Fired when a task is canceled |
| `OnProcessingStarted` | `EventHandler` | Fired when the queue starts processing |
| `OnProcessingStopped` | `EventHandler` | Fired when the queue stops processing |

### TaskDetails Class

Represents metadata and state for a queued or running task.

| Property | Type | Description |
|----------|------|-------------|
| `Guid` | `Guid` | Unique identifier for the task |
| `Name` | `string` | User-supplied name for the task |
| `Metadata` | `Dictionary<string, object>` | User-supplied metadata |
| `Function` | `Func<CancellationToken, Task>` | The async function to execute |
| `Task` | `Task` | The running Task instance (null until started) |
| `TokenSource` | `CancellationTokenSource` | Cancellation token source for this task |
| `Token` | `CancellationToken` | Cancellation token for this task |

### TaskRunWithTimeout Class

Static utility for running tasks with timeout constraints.

#### Properties
| Property | Type | Description |
|----------|------|-------------|
| `Logger` | `Action<string>` | Optional callback for log messages |
| `LogHeader` | `string` | Prefix for log messages. Default: "[TaskRunWithTimeout] " |

#### Methods
```csharp
Task<T> Go<T>(Task<T> task, int timeoutMs, CancellationTokenSource tokenSource)
```
Executes a task with the specified timeout in milliseconds. Throws `TimeoutException` if the task exceeds the timeout.

### Using Async API

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

// Create queue with backpressure (max 100 queued tasks)
using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 10, maxQueueSize: 100))
{
    // Start asynchronously
    await queue.StartAsync();

    // Add tasks asynchronously (waits if queue is full)
    for (int i = 0; i < 1000; i++)
    {
        await queue.AddTaskAsync(
            Guid.NewGuid(),
            $"Task-{i}",
            null,
            async (token) =>
            {
                await ProcessItemAsync(i, token);
            }
        );
    }

    // Wait for all tasks to complete
    await queue.WaitForCompletionAsync();

    // Stop and cleanup
    await queue.StopAsync(waitForCompletion: true);
}
```

### Backpressure and Queue Limits

**Problem**: If tasks arrive faster than they can be processed, unbounded queues can cause out-of-memory errors.

**Solution**: Use `MaxQueueSize` to limit queue depth:

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

// Bounded queue - prevents memory exhaustion
using (TaskQueue queue = new TaskQueue(
    maxConcurrentTasks: 5,
    maxQueueSize: 50  // Max 50 queued tasks
))
{
    queue.Start();

    // This will wait (async) or throw (sync) if queue is full
    try
    {
        queue.AddTask(guid, name, null, taskFunc);  // Throws if full
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine("Queue is full, backing off...");
    }

    // OR use async version which waits instead of throwing
    await queue.AddTaskAsync(guid, name, null, taskFunc);  // Waits if full
}
```

## Advanced Topics

### Understanding Task Lifecycle

1. **Added**: Task is enqueued via `AddTask()`
2. **Started**: Task is dequeued and begins execution (when `RunningCount < MaxConcurrentTasks`)
3. **Terminal State**: Task reaches one of three end states:
   - **Finished**: Completed successfully
   - **Faulted**: Threw an exception
   - **Canceled**: Was canceled via cancellation token

### Concurrency Control Details

TaskHandler uses an **event-driven architecture** with `System.Threading.Channels`:
1. Tasks are immediately available when added to the channel
2. A semaphore (`SemaphoreSlim`) enforces the `MaxConcurrentTasks` limit
3. Task completions trigger continuations that release semaphore slots
4. New tasks start instantly when slots become available (sub-millisecond latency)
5. Zero CPU usage when idle

**Benefits of event-driven architecture**:
- **Latency**: <1ms (vs 50ms average in older polling-based implementations)
- **Throughput**: 10-100x higher for short tasks
- **CPU**: 0% idle
- **Responsiveness**: Instant task execution

### Exception Handling Best Practices

TaskHandler provides multiple layers of exception handling to ensure robust error management. Understanding when and how to use each mechanism is crucial for building reliable applications.

#### Overview of Exception Handling Mechanisms

1. **OnTaskFaulted Event** - Global error handler for all task exceptions
2. **TaskHandle<T> Exception Propagation** - Per-task exception handling via try-catch
3. **Timeout Exceptions** - Automatic TimeoutException for tasks exceeding time limits
4. **Statistics Monitoring** - Track failure rates and metrics
5. **Safe Event Handlers** - Event handler exceptions don't crash the queue

#### When to Use Each Approach

| Approach | Use When | Example Scenarios |
|----------|----------|-------------------|
| **OnTaskFaulted** | You need centralized error handling for all tasks | Logging all failures, alerting, global retry logic |
| **TaskHandle<T> try-catch** | You need to handle exceptions for specific tasks | Critical operations, per-task error recovery, user notifications |
| **Both Together** | You need both global monitoring AND per-task handling | Production systems with centralized logging + specific error handling |
| **Statistics** | You need to monitor overall health and failure rates | Performance monitoring, SLA tracking, alerting thresholds |

#### 1. Global Error Handling with OnTaskFaulted

Use `OnTaskFaulted` for centralized error handling across all tasks:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();

// Global error handler - fires for ALL task exceptions
queue.OnTaskFaulted += (sender, task) =>
{
    // Access the exception (wrapped in AggregateException)
    if (task.Task?.Exception != null)
    {
        // Task exceptions are wrapped, so use InnerException or InnerExceptions
        Exception innerEx = task.Task.Exception.InnerException;

        Console.WriteLine($"[ERROR] Task '{task.Name}' (ID: {task.Guid}) failed");
        Console.WriteLine($"  Exception Type: {innerEx?.GetType().Name}");
        Console.WriteLine($"  Message: {innerEx?.Message}");
        Console.WriteLine($"  Stack Trace: {innerEx?.StackTrace}");

        // Access metadata for context
        if (task.Metadata != null && task.Metadata.ContainsKey("UserId"))
        {
            Console.WriteLine($"  User ID: {task.Metadata["UserId"]}");
        }
    }
};

queue.Start();

// All tasks will trigger OnTaskFaulted on exception
await queue.EnqueueAsync(
    "DatabaseOperation",
    async (token) =>
    {
        throw new InvalidOperationException("Database connection failed");
    }
);
```

**Important**: Task exceptions are wrapped in `AggregateException`, so access the actual exception via:
- `task.Task.Exception.InnerException` - for single exceptions
- `task.Task.Exception.InnerExceptions` - for multiple exceptions (rare in TaskQueue)

#### 2. Per-Task Error Handling with TaskHandle<T>

Use `TaskHandle<T>` with try-catch when you need to handle exceptions for specific tasks:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

try
{
    // Enqueue task that returns a result
    TaskHandle<string> handle = await queue.EnqueueAsync(
        "ApiCall",
        async (token) =>
        {
            // Simulate API call that might fail
            if (new Random().Next(2) == 0)
            {
                throw new HttpRequestException("API request failed");
            }
            return "Success";
        }
    );

    // Await the result - exception thrown here if task faulted
    string result = await handle.Task;
    Console.WriteLine($"Task succeeded: {result}");
}
catch (HttpRequestException ex)
{
    // Handle specific exception type
    Console.WriteLine($"API call failed: {ex.Message}");
    // Implement retry logic, fallback, or user notification
}
catch (OperationCanceledException)
{
    // Task was canceled
    Console.WriteLine("Task was canceled");
}
catch (Exception ex)
{
    // Catch-all for unexpected exceptions
    Console.WriteLine($"Unexpected error: {ex.Message}");
}
```

**Note**: Both `OnTaskFaulted` AND the exception propagation occur when using `TaskHandle<T>`.

#### 3. Using Both Mechanisms Together (Recommended for Production)

Combine global and per-task error handling for robust error management:

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

// Global error handler for logging and monitoring
TaskQueue queue = new TaskQueue();

queue.OnTaskFaulted += (sender, task) =>
{
    // Centralized logging for ALL failures
    Logger.LogError(
        task.Task?.Exception?.InnerException,
        "Task {TaskName} (ID: {TaskId}) faulted",
        task.Name,
        task.Guid
    );

    // Send to monitoring system
    MonitoringSystem.RecordFailure(task.Name, task.Task?.Exception?.InnerException);
};

queue.Start();

// Per-task handling for specific error recovery
for (int i = 0; i < 100; i++)
{
    try
    {
        TaskHandle<string> handle = await queue.EnqueueAsync(
            $"ProcessItem-{i}",
            async (token) => await ProcessCriticalItem(i, token)
        );

        string result = await handle.Task;
        Console.WriteLine($"Item {i} processed: {result}");
    }
    catch (ValidationException ex)
    {
        // Handle validation errors - don't retry
        Console.WriteLine($"Item {i} validation failed: {ex.Message}");
    }
    catch (TransientException ex)
    {
        // Handle transient errors - implement retry
        Console.WriteLine($"Item {i} failed (transient), retrying...");
        await RetryItem(i);
    }
    catch (Exception ex)
    {
        // Unexpected exception - log and continue
        Console.WriteLine($"Item {i} failed unexpectedly: {ex.Message}");
    }
}

await queue.WaitForCompletionAsync();
```

#### 4. Defensive Task Implementation

Implement error handling WITHIN your tasks for fine-grained control:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

await queue.EnqueueAsync(
    "RobustTask",
    async (token) =>
    {
        try
        {
            // Risky operation
            await RiskyDatabaseOperation(token);
        }
        catch (SqlException ex) when (ex.Number == 2601) // Duplicate key
        {
            // Handle specific SQL error - don't propagate
            Logger.LogWarning("Duplicate key detected, skipping");
            return; // Task completes successfully
        }
        catch (SqlException ex)
        {
            // Log SQL error with context
            Logger.LogError(ex, "Database operation failed");
            throw; // Re-throw to trigger OnTaskFaulted
        }
        catch (Exception ex)
        {
            // Log unexpected error
            Logger.LogError(ex, "Unexpected error in task");
            throw; // Re-throw to trigger OnTaskFaulted
        }
    }
);
```

**Best Practice**: Use try-catch within tasks when you need to:
- Handle specific errors without failing the task
- Add contextual logging before re-throwing
- Clean up resources before propagating exceptions
- Convert exceptions to domain-specific exceptions

#### 5. Structured Logging with Full Exception Details

Implement comprehensive logging in your error handlers:

```csharp
using Microsoft.Extensions.Logging;
using TaskHandler;

ILogger<MyService> logger = /* ... */;
TaskQueue queue = new TaskQueue();

queue.OnTaskFaulted += (sender, task) =>
{
    if (task.Task?.Exception != null)
    {
        Exception ex = task.Task.Exception.InnerException ?? task.Task.Exception;

        // Structured logging with all context
        logger.LogError(
            ex,
            "Task execution failed. " +
            "TaskName: {TaskName}, " +
            "TaskId: {TaskId}, " +
            "ExceptionType: {ExceptionType}, " +
            "Message: {Message}, " +
            "Metadata: {@Metadata}",
            task.Name,
            task.Guid,
            ex.GetType().FullName,
            ex.Message,
            task.Metadata
        );

        // Include stack trace for critical errors
        if (ex is CriticalException)
        {
            logger.LogCritical("Stack Trace: {StackTrace}", ex.StackTrace);
        }
    }
};
```

#### 6. Statistics-Based Failure Monitoring

Monitor failure rates and queue health using statistics:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

// Enqueue tasks...
for (int i = 0; i < 1000; i++)
{
    await queue.EnqueueAsync($"Task-{i}", async (token) => await ProcessItem(i, token));
}

// Monitor periodically
while (queue.RunningCount > 0 || queue.QueuedCount > 0)
{
    TaskQueueStatistics stats = queue.GetStatistics();

    // Calculate failure rate
    double failureRate = stats.TotalEnqueued > 0
        ? (double)stats.TotalFailed / stats.TotalEnqueued
        : 0;

    // Alert on high failure rate
    if (failureRate > 0.10) // More than 10% failures
    {
        Logger.LogWarning(
            "High failure rate detected: {FailureRate:P} ({Failed}/{Total})",
            failureRate,
            stats.TotalFailed,
            stats.TotalEnqueued
        );

        // Take corrective action
        AlertingSystem.SendAlert($"TaskQueue failure rate: {failureRate:P}");
    }

    // Monitor for complete failure
    if (stats.TotalFailed > 0 && stats.TotalCompleted == 0)
    {
        Logger.LogCritical("All tasks are failing - possible systemic issue");
    }

    await Task.Delay(5000);
}

// Final statistics
TaskQueueStatistics finalStats = queue.GetStatistics();
Console.WriteLine($"Completed: {finalStats.TotalCompleted}");
Console.WriteLine($"Failed: {finalStats.TotalFailed}");
Console.WriteLine($"Canceled: {finalStats.TotalCanceled}");
Console.WriteLine($"Success Rate: {(double)finalStats.TotalCompleted / finalStats.TotalEnqueued:P}");
```

#### 7. Retry Logic Pattern

Implement retry logic for transient failures:

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

public async Task<T> EnqueueWithRetry<T>(
    TaskQueue queue,
    string taskName,
    Func<CancellationToken, Task<T>> taskFunc,
    int maxRetries = 3,
    TimeSpan? retryDelay = null)
{
    retryDelay ??= TimeSpan.FromSeconds(2);

    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        try
        {
            TaskHandle<T> handle = await queue.EnqueueAsync(taskName, taskFunc);
            T result = await handle.Task;
            return result; // Success
        }
        catch (Exception ex) when (IsTransientException(ex) && attempt < maxRetries)
        {
            Logger.LogWarning(
                "Task {TaskName} failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                taskName,
                attempt + 1,
                maxRetries + 1,
                ex.Message
            );

            await Task.Delay(retryDelay.Value);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Task {TaskName} failed permanently", taskName);
            throw; // Non-transient or max retries exceeded
        }
    }

    throw new InvalidOperationException("Retry logic failed");
}

private bool IsTransientException(Exception ex)
{
    return ex is HttpRequestException ||
           ex is TimeoutException ||
           (ex is SqlException sqlEx && new[] { 1205, -2 }.Contains(sqlEx.Number));
}

// Usage
try
{
    string result = await EnqueueWithRetry(
        queue,
        "ApiCall",
        async (token) => await CallExternalApi(token),
        maxRetries: 3,
        retryDelay: TimeSpan.FromSeconds(5)
    );
}
catch (Exception ex)
{
    Console.WriteLine($"All retries exhausted: {ex.Message}");
}
```

#### 8. Circuit Breaker Pattern

Implement circuit breaker to prevent cascading failures:

```csharp
using System;
using System.Threading.Tasks;
using TaskHandler;

public class CircuitBreaker
{
    private int _failureCount = 0;
    private DateTime? _openedAt = null;
    private readonly int _threshold;
    private readonly TimeSpan _timeout;

    public CircuitBreaker(int threshold = 5, TimeSpan? timeout = null)
    {
        _threshold = threshold;
        _timeout = timeout ?? TimeSpan.FromMinutes(1);
    }

    public bool IsOpen
    {
        get
        {
            if (_openedAt.HasValue && DateTime.UtcNow - _openedAt.Value > _timeout)
            {
                // Reset after timeout
                _failureCount = 0;
                _openedAt = null;
                return false;
            }
            return _failureCount >= _threshold;
        }
    }

    public void RecordFailure()
    {
        _failureCount++;
        if (_failureCount >= _threshold && !_openedAt.HasValue)
        {
            _openedAt = DateTime.UtcNow;
        }
    }

    public void RecordSuccess() => _failureCount = 0;
}

// Usage
CircuitBreaker circuitBreaker = new CircuitBreaker(threshold: 5, timeout: TimeSpan.FromMinutes(1));
TaskQueue queue = new TaskQueue();

queue.OnTaskFaulted += (sender, task) =>
{
    circuitBreaker.RecordFailure();
    Logger.LogWarning($"Circuit breaker failure count: {circuitBreaker._failureCount}");
};

queue.OnTaskFinished += (sender, task) =>
{
    circuitBreaker.RecordSuccess();
};

queue.Start();

// Check circuit breaker before enqueueing
for (int i = 0; i < 100; i++)
{
    if (circuitBreaker.IsOpen)
    {
        Logger.LogWarning("Circuit breaker is OPEN - skipping task");
        await Task.Delay(TimeSpan.FromSeconds(10)); // Back off
        continue;
    }

    await queue.EnqueueAsync($"Task-{i}", async (token) => await ProcessItem(i, token));
}
```

#### 9. Event Handler Exception Safety

TaskHandler automatically catches exceptions in YOUR event handlers to prevent queue crashes:

```csharp
TaskQueue queue = new TaskQueue();

// Logger captures event handler exceptions
queue.Logger = Console.WriteLine;

// This buggy event handler won't crash the queue
queue.OnTaskAdded += (sender, task) =>
{
    throw new Exception("Bug in event handler"); // Caught and logged
};

queue.OnTaskFaulted += (sender, task) =>
{
    int x = 0;
    int result = 10 / x; // Division by zero - caught and logged
};

queue.Start();

// Queue continues to function despite event handler bugs
await queue.EnqueueAsync("Task1", async (token) => await Task.Delay(100, token));
```

**Output** (via Logger):
```
[TaskHandler] exception in event handler: System.Exception: Bug in event handler
[TaskHandler] exception in event handler: System.DivideByZeroException: Attempted to divide by zero
```

#### 10. Handling Multiple Exception Types

Process different exception types with specific handling:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();

queue.OnTaskFaulted += (sender, task) =>
{
    Exception ex = task.Task?.Exception?.InnerException;

    switch (ex)
    {
        case TimeoutException timeoutEx:
            Logger.LogWarning("Task {TaskName} timed out after {Timeout}", task.Name, timeoutEx.Message);
            // Maybe retry with longer timeout
            break;

        case HttpRequestException httpEx:
            Logger.LogWarning("HTTP request failed for {TaskName}: {Message}", task.Name, httpEx.Message);
            // Maybe retry or use fallback
            break;

        case SqlException sqlEx:
            Logger.LogError(sqlEx, "Database error in {TaskName}", task.Name);
            // Maybe alert DBA
            break;

        case ValidationException validationEx:
            Logger.LogInformation("Validation failed for {TaskName}: {Message}", task.Name, validationEx.Message);
            // Don't alert - expected validation failure
            break;

        default:
            Logger.LogError(ex, "Unexpected error in {TaskName}", task.Name);
            // Alert for investigation
            break;
    }
};
```

#### Summary of Best Practices

1. ✅ **Always subscribe to OnTaskFaulted** for centralized error logging and monitoring
2. ✅ **Use TaskHandle<T> with try-catch** for tasks requiring specific error handling
3. ✅ **Implement defensive error handling** within tasks when appropriate
4. ✅ **Monitor statistics** to track failure rates and queue health
5. ✅ **Use structured logging** with full exception context and metadata
6. ✅ **Implement retry logic** for transient failures
7. ✅ **Consider circuit breakers** for external dependencies
8. ✅ **Set the Logger property** to capture event handler exceptions
9. ✅ **Access InnerException** when handling exceptions in events (due to AggregateException wrapping)
10. ✅ **Test your error handling** to ensure exceptions don't crash your application

### Metadata Usage

Use the metadata dictionary to attach custom data to tasks:

```csharp
var metadata = new Dictionary<string, object>
{
    { "UserId", 12345 },
    { "Priority", "High" },
    { "RetryCount", 0 },
    { "StartTime", DateTime.UtcNow }
};

queue.AddTask(Guid.NewGuid(), "UserOperation", metadata, async (token) =>
{
    // Task implementation
});

queue.OnTaskFinished += (sender, task) =>
{
    var userId = task.Metadata["UserId"];
    var startTime = (DateTime)task.Metadata["StartTime"];
    var duration = DateTime.UtcNow - startTime;
    Console.WriteLine($"User {userId} task completed in {duration.TotalMilliseconds}ms");
};
```

### Multiple Start/Stop Cycles

TaskHandler supports starting and stopping the queue multiple times:

```csharp
TaskQueue queue = new TaskQueue(10);

// First batch
queue.AddTask(Guid.NewGuid(), "Task1", null, async (token) => await DoWork(token));
queue.Start();
await Task.Delay(5000);
queue.Stop();

// Second batch (same queue instance)
queue.AddTask(Guid.NewGuid(), "Task2", null, async (token) => await DoWork(token));
queue.Start();
await Task.Delay(5000);
queue.Stop();

queue.Dispose();
```

### Graceful Shutdown

```csharp
using System.Threading.Tasks;
using TaskHandler;

// Recommended approach: Use async methods
using (TaskQueue queue = new TaskQueue())
{
    // Add and process tasks...

    // Wait for all tasks to complete
    await queue.WaitForCompletionAsync();
    await queue.StopAsync(waitForCompletion: true);
}
// DisposeAsync called automatically by using statement
```

## Framework Support

TaskHandler targets multiple .NET frameworks:
- .NET Standard 2.0
- .NET Standard 2.1
- .NET 6.0
- .NET 8.0

This ensures compatibility with:
- .NET Core 2.0+
- .NET Framework 4.6.1+
- .NET 5.0+
- .NET 6.0+
- .NET 8.0+
- Xamarin
- Unity (2021.2+)

## Testing

The repository includes comprehensive automated tests in the `Test.Automated` project:

```bash
cd src/Test.Automated
dotnet run
```

The test suite includes 38 comprehensive tests covering:

**Core Functionality Tests (Tests 1-20):**
- Basic task enqueueing and execution
- Concurrency limit enforcement
- Task cancellation (individual and batch)
- State management and disposal
- Error handling in event handlers
- Multiple start/stop cycles
- Race condition safety
- Timeout functionality
- High throughput scenarios
- Queue statistics accuracy

**Advanced Features Tests (Tests 21-30):**
- TaskHandle<T> with result retrieval
- TaskHandle<T> with exception handling
- TaskHandle<T> with cancellation
- TaskQueueOptions pattern
- GetRunningTasksInfo() method
- EnqueueAsync() with timeout (success case)
- EnqueueAsync() with timeout (timeout case)
- Task priority property
- TaskQueue.Create() factory method
- Multiple concurrent tasks with results

**Statistics and Progress Tests (Tests 31-38):**
- Statistics tracking and accuracy
- Progress reporting functionality

All tests include clear PASS/FAIL indicators and detailed error messages.

## Advanced Features

TaskHandler v2.0.0 includes powerful features for advanced task management:

### TaskHandle<T> - Tasks with Results

Enqueue tasks that return results and await them:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using TaskHandler;

using (TaskQueue queue = new TaskQueue())
{
    queue.Start();

    // Enqueue a task that returns a value
    TaskHandle<string> handle = await queue.EnqueueAsync(
        "FetchData",
        async (CancellationToken token) =>
        {
            await Task.Delay(1000, token);
            return "Data retrieved successfully";
        }
    );

    // Await the result
    string result = await handle.Task;
    Console.WriteLine(result); // "Data retrieved successfully"
}
```

**Multiple concurrent results:**
```csharp
var handles = new List<TaskHandle<int>>();

for (int i = 0; i < 100; i++)
{
    int value = i;
    var handle = await queue.EnqueueAsync(
        $"Calculate-{i}",
        async (token) =>
        {
            await Task.Delay(100, token);
            return value * 2;
        }
    );
    handles.Add(handle);
}

// Wait for all results
int[] results = await Task.WhenAll(handles.Select(h => h.Task));
```

### Options Pattern - Fluent Configuration

Configure TaskQueue using the options pattern or factory method:

```csharp
using TaskHandler;

// Using TaskQueueOptions constructor
var queue = new TaskQueue(new TaskQueueOptions
{
    MaxConcurrentTasks = 10,
    MaxQueueSize = 100,
    Logger = Console.WriteLine,
    OnTaskFinished = (sender, task) =>
    {
        Console.WriteLine($"Task {task.Name} completed");
    },
    OnTaskFaulted = (sender, task) =>
    {
        Console.WriteLine($"Task {task.Name} failed: {task.Task?.Exception?.Message}");
    }
});

// Or using factory method
var queue2 = TaskQueue.Create(options =>
{
    options.MaxConcurrentTasks = 20;
    options.MaxQueueSize = 200;
    options.Logger = msg => Debug.WriteLine(msg);
    options.OnTaskStarted = (sender, task) =>
    {
        Console.WriteLine($"Started: {task.Name}");
    };
});

queue.Start();
```

### Per-Task Timeout Support

Set timeouts for individual tasks:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

// Task with 5-second timeout
Guid taskId = await queue.EnqueueAsync(
    "LongOperation",
    async (token) =>
    {
        await Task.Delay(10000, token); // This will timeout
    },
    timeout: TimeSpan.FromSeconds(5)
);

// Task will fault with TimeoutException after 5 seconds
```

**Timeout with result handling:**
```csharp
try
{
    TaskHandle<string> handle = await queue.EnqueueAsync(
        "ApiCall",
        async (token) =>
        {
            await Task.Delay(10000, token);
            return "Data";
        },
        timeout: TimeSpan.FromSeconds(3)
    );

    string result = await handle.Task;
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Task timed out: {ex.Message}");
}
```

### Task Priority

Assign priorities to tasks (lower number = higher priority):

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();

// Add high-priority task
await queue.EnqueueAsync(
    "UrgentTask",
    async (token) => await ProcessUrgentData(token),
    priority: (int)TaskPriority.Urgent  // Priority: 0
);

// Add normal-priority task
await queue.EnqueueAsync(
    "NormalTask",
    async (token) => await ProcessNormalData(token),
    priority: (int)TaskPriority.Normal  // Priority: 2
);

// Add background task
await queue.EnqueueAsync(
    "BackgroundTask",
    async (token) => await CleanupData(token),
    priority: (int)TaskPriority.Background  // Priority: 4
);

queue.Start();
```

**Priority enum values:**
- `TaskPriority.Urgent` = 0 (highest)
- `TaskPriority.High` = 1
- `TaskPriority.Normal` = 2 (default)
- `TaskPriority.Low` = 3
- `TaskPriority.Background` = 4 (lowest)

### GetRunningTasksInfo() - Immutable Task Information

Safely inspect running tasks without accessing mutable state:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

// Add some tasks
await queue.EnqueueAsync("Task1", async (token) => await Task.Delay(5000, token));
await queue.EnqueueAsync("Task2", async (token) => await Task.Delay(5000, token));

await Task.Delay(100); // Let tasks start

// Get read-only snapshot of running tasks
IReadOnlyCollection<TaskInfo> runningTasks = queue.GetRunningTasksInfo();

foreach (var task in runningTasks)
{
    Console.WriteLine($"Task: {task.Name}");
    Console.WriteLine($"  ID: {task.Id}");
    Console.WriteLine($"  Status: {task.Status}");
    Console.WriteLine($"  Priority: {task.Priority}");
    Console.WriteLine($"  Metadata count: {task.Metadata.Count}");
}
```

### Combined Example - Advanced Features

```csharp
using TaskHandler;

// Create queue with options pattern
var queue = TaskQueue.Create(options =>
{
    options.MaxConcurrentTasks = 5;
    options.MaxQueueSize = 50;
    options.Logger = Console.WriteLine;
    options.OnTaskFaulted = (sender, task) =>
    {
        if (task.Task?.Exception != null)
        {
            Console.WriteLine($"Task {task.Name} faulted: {task.Task.Exception.Message}");
        }
    };
});

await queue.StartAsync();

// Enqueue task with result, priority, and timeout
TaskHandle<string> handle = await queue.EnqueueAsync(
    "DataFetch",
    async (token) =>
    {
        await Task.Delay(2000, token);
        return "Important data";
    },
    priority: (int)TaskPriority.High,
    timeout: TimeSpan.FromSeconds(5)
);

// Monitor running tasks
IReadOnlyCollection<TaskInfo> running = queue.GetRunningTasksInfo();
Console.WriteLine($"Currently running: {running.Count} tasks");

// Await result
try
{
    string result = await handle.Task;
    Console.WriteLine($"Result: {result}");
}
catch (TimeoutException)
{
    Console.WriteLine("Task timed out");
}

// Graceful shutdown
await queue.WaitForCompletionAsync();
await queue.DisposeAsync();
```

## Statistics and Progress Reporting

TaskHandler v2.1.0 includes features for statistics tracking and progress reporting:

### Statistics and Metrics

Track queue performance and task metrics with `GetStatistics()`:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

// Enqueue and process tasks
for (int i = 0; i < 100; i++)
{
    await queue.EnqueueAsync(
        $"Task{i}",
        async (token) => await ProcessItem(i, token)
    );
}

await queue.WaitForCompletionAsync();

// Get statistics
TaskQueueStatistics stats = queue.GetStatistics();

Console.WriteLine($"Total Enqueued: {stats.TotalEnqueued}");
Console.WriteLine($"Total Completed: {stats.TotalCompleted}");
Console.WriteLine($"Total Failed: {stats.TotalFailed}");
Console.WriteLine($"Total Canceled: {stats.TotalCanceled}");
Console.WriteLine($"Current Queue Depth: {stats.CurrentQueueDepth}");
Console.WriteLine($"Currently Running: {stats.CurrentRunningCount}");
Console.WriteLine($"Average Execution Time: {stats.AverageExecutionTime.TotalMilliseconds:F2}ms");
Console.WriteLine($"Average Wait Time: {stats.AverageWaitTime.TotalMilliseconds:F2}ms");
Console.WriteLine($"Last Task Started: {stats.LastTaskStarted}");
Console.WriteLine($"Last Task Completed: {stats.LastTaskCompleted}");

await queue.DisposeAsync();
```

**Available Statistics:**

| Statistic | Type | Description |
|-----------|------|-------------|
| `TotalEnqueued` | `long` | Total number of tasks enqueued since queue creation |
| `TotalCompleted` | `long` | Total number of tasks completed successfully |
| `TotalFailed` | `long` | Total number of tasks that failed (faulted) |
| `TotalCanceled` | `long` | Total number of tasks that were canceled |
| `CurrentQueueDepth` | `int` | Current number of tasks waiting in the queue |
| `CurrentRunningCount` | `int` | Current number of tasks actively running |
| `AverageExecutionTime` | `TimeSpan` | Average execution time across completed tasks |
| `AverageWaitTime` | `TimeSpan` | Average time tasks spent waiting in queue before execution |
| `LastTaskStarted` | `DateTime?` | Timestamp when the most recent task started (null if none started) |
| `LastTaskCompleted` | `DateTime?` | Timestamp when the most recent task completed (null if none completed) |

**Example - Monitoring Performance:**

```csharp
TaskQueue queue = new TaskQueue(maxConcurrentTasks: 10);
queue.Start();

// Enqueue tasks
for (int i = 0; i < 1000; i++)
{
    await queue.EnqueueAsync($"Task{i}", async (token) => await ProcessTask(i, token));
}

// Monitor progress periodically
while (queue.RunningCount > 0 || queue.QueuedCount > 0)
{
    TaskQueueStatistics stats = queue.GetStatistics();
    double completionRate = (double)stats.TotalCompleted / stats.TotalEnqueued * 100;

    Console.WriteLine($"Progress: {completionRate:F1}% ({stats.TotalCompleted}/{stats.TotalEnqueued})");
    Console.WriteLine($"Running: {stats.CurrentRunningCount}, Queued: {stats.CurrentQueueDepth}");
    Console.WriteLine($"Avg Execution: {stats.AverageExecutionTime.TotalMilliseconds:F0}ms");

    await Task.Delay(1000);
}

await queue.DisposeAsync();
```

### Progress Reporting

Report progress from within tasks using `IProgress<TaskProgress>`:

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();
queue.Start();

// Create progress reporter
Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
{
    Console.WriteLine($"Progress: {p.Current}/{p.Total} ({p.PercentComplete:F1}%) - {p.Message}");
});

// Enqueue task with progress reporting
TaskHandle<string> handle = await queue.EnqueueAsync(
    "DataProcessing",
    async (CancellationToken token, IProgress<TaskProgress> prog) =>
    {
        int totalItems = 100;

        for (int i = 0; i <= totalItems; i++)
        {
            // Report progress
            prog?.Report(new TaskProgress(i, totalItems, $"Processing item {i}"));

            // Do work
            await ProcessItem(i, token);
        }

        return "Processing complete";
    },
    progress
);

// Await result
string result = await handle.Task;
Console.WriteLine(result);

await queue.DisposeAsync();
```

**Progress without result:**

```csharp
Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
{
    Console.WriteLine($"{p.PercentComplete:F0}% complete - {p.Message}");
});

Guid taskId = await queue.EnqueueAsync(
    "FileProcessing",
    async (CancellationToken token, IProgress<TaskProgress> prog) =>
    {
        int totalFiles = 50;

        for (int i = 0; i < totalFiles; i++)
        {
            prog?.Report(new TaskProgress(i + 1, totalFiles, $"Processing file {i + 1}"));
            await ProcessFile(files[i], token);
        }
    },
    progress
);
```

**TaskProgress Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Current` | `int` | Current progress value |
| `Total` | `int` | Total expected value for completion |
| `PercentComplete` | `double` | Percentage of completion (0-100) |
| `Message` | `string` | Optional message describing current progress state |

**Example - Download with Progress:**

```csharp
var queue = TaskQueue.Create(options =>
{
    options.MaxConcurrentTasks = 3;
    options.Logger = Console.WriteLine;
});

await queue.StartAsync();

// Download multiple files with progress
List<TaskHandle<bool>> downloads = new List<TaskHandle<bool>>();

foreach (string url in urlsToDownload)
{
    Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
    {
        Console.WriteLine($"{url}: {p.PercentComplete:F0}% ({p.Message})");
    });

    TaskHandle<bool> handle = await queue.EnqueueAsync(
        $"Download-{url}",
        async (CancellationToken token, IProgress<TaskProgress> prog) =>
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                long? totalBytes = response.Content.Headers.ContentLength;

                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = File.Create($"downloads/{Path.GetFileName(url)}"))
                {
                    byte[] buffer = new byte[8192];
                    long downloadedBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                        downloadedBytes += bytesRead;

                        if (totalBytes.HasValue)
                        {
                            int percentage = (int)((double)downloadedBytes / totalBytes.Value * 100);
                            prog?.Report(new TaskProgress(percentage, 100, $"{downloadedBytes}/{totalBytes.Value} bytes"));
                        }
                    }
                }

                return true;
            }
        },
        progress,
        priority: (int)TaskPriority.Normal,
        timeout: TimeSpan.FromMinutes(5)
    );

    downloads.Add(handle);
}

// Wait for all downloads
bool[] results = await Task.WhenAll(downloads.Select(h => h.Task));
Console.WriteLine($"Downloaded {results.Count(r => r)}/{results.Length} files successfully");

await queue.DisposeAsync();
```

### Combined Example - Statistics + Progress

```csharp
using TaskHandler;

// Create queue with options
var queue = TaskQueue.Create(options =>
{
    options.MaxConcurrentTasks = 5;
    options.MaxQueueSize = 100;
    options.Logger = msg => Debug.WriteLine(msg);
});

await queue.StartAsync();

// Process items with progress tracking
int totalItems = 1000;
List<TaskHandle<int>> handles = new List<TaskHandle<int>>();

for (int i = 0; i < totalItems; i++)
{
    int itemId = i;

    Progress<TaskProgress> progress = new Progress<TaskProgress>(p =>
    {
        // Could update UI here
    });

    TaskHandle<int> handle = await queue.EnqueueAsync(
        $"Item-{itemId}",
        async (CancellationToken token, IProgress<TaskProgress> prog) =>
        {
            // Simulate multi-step processing
            for (int step = 0; step < 10; step++)
            {
                prog?.Report(new TaskProgress(step + 1, 10, $"Step {step + 1}"));
                await Task.Delay(10, token);
            }
            return itemId;
        },
        progress,
        priority: itemId < 100 ? (int)TaskPriority.High : (int)TaskPriority.Normal
    );

    handles.Add(handle);
}

// Monitor overall progress
while (queue.RunningCount > 0 || queue.QueuedCount > 0)
{
    TaskQueueStatistics stats = queue.GetStatistics();

    Console.WriteLine($"\n=== Queue Statistics ===");
    Console.WriteLine($"Progress: {stats.TotalCompleted}/{stats.TotalEnqueued} ({(double)stats.TotalCompleted / stats.TotalEnqueued * 100:F1}%)");
    Console.WriteLine($"Running: {stats.CurrentRunningCount}, Queued: {stats.CurrentQueueDepth}");
    Console.WriteLine($"Failed: {stats.TotalFailed}, Canceled: {stats.TotalCanceled}");
    Console.WriteLine($"Avg Execution: {stats.AverageExecutionTime.TotalMilliseconds:F0}ms");
    Console.WriteLine($"Avg Wait: {stats.AverageWaitTime.TotalMilliseconds:F0}ms");

    await Task.Delay(2000);
}

// Wait for all results
int[] results = await Task.WhenAll(handles.Select(h => h.Task));

// Final statistics
TaskQueueStatistics finalStats = queue.GetStatistics();
Console.WriteLine($"\n=== Final Statistics ===");
Console.WriteLine(finalStats.ToString());

await queue.DisposeAsync();
```

## Feedback and Issues

Found a bug? Have a feature request? Please file an issue at:
https://github.com/jchristn/TaskHandler/issues

## Contributing

Contributions are welcome! Please see `IMPROVEMENTS.md` for planned enhancements and `CLAUDE.md` for coding standards.

## License

This project is licensed under the MIT License.
