# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TaskHandler is a C# NuGet library for managing asynchronous task queues with concurrency control. It provides two main components:
- **TaskQueue**: A managed queue that executes async tasks with configurable concurrency limits
- **TaskRunWithTimeout**: A utility for running tasks with timeout constraints

## Build & Test Commands

### Build
```bash
cd src
dotnet build TaskHandler.sln
```

### Build for specific target framework
```bash
cd src/TaskHandler
dotnet build -f net8.0
```

### Run tests
```bash
cd src/Test
dotnet run
```

### Create NuGet package
The project is configured with `GeneratePackageOnBuild` set to true, so building in Release mode will automatically generate the NuGet package:
```bash
cd src/TaskHandler
dotnet build -c Release
```

## Architecture

**Current Version:** v2.0.0

The architecture has evolved significantly from v1.0.x:
- **v1.0.x**: Polling-based with 100ms iteration delay
- **v2.0.0**: Event-driven with Channels and Semaphores (10-100x performance improvement), includes statistics tracking and progress reporting

This documentation describes the v2.0.0 architecture.

### Core Components

**TaskQueue** (src/TaskHandler/TaskQueue.cs)
- Main class for managing task execution
- Implements `IDisposable` and `IAsyncDisposable`
- Uses `System.Threading.Channels` for event-driven task queueing
- Uses `ConcurrentDictionary<Guid, TaskDetails>` for running tasks
- Uses `SemaphoreSlim` for concurrency control (not polling)
- Background task runner (`TaskRunner`) uses continuations for instant task execution
- Enforces `MaxConcurrentTasks` limit (default: 32)
- Supports bounded queues via `MaxQueueSize` for backpressure
- Each task gets its own `CancellationTokenSource` for individual cancellation

**TaskDetails** (src/TaskHandler/TaskDetails.cs)
- Encapsulates task metadata: Guid, Name, Metadata dictionary, Priority
- Contains the task function: `Func<CancellationToken, Task>`
- Manages per-task cancellation via `TokenSource` and `Token`
- Tracks timing information for statistics

**TaskHandle<T>** (src/TaskHandler/TaskHandle.cs)
- Wrapper for tasks that return results
- Uses `TaskCompletionSource<T>` internally
- Allows awaiting task results via `handle.Task`
- Returned by `EnqueueAsync<T>()` methods

**TaskQueueOptions** (src/TaskHandler/TaskQueueOptions.cs)
- Configuration options for fluent API
- Used with `TaskQueue.Create()` factory method or constructor
- Encapsulates MaxConcurrentTasks, MaxQueueSize, Logger, and event handlers

**TaskPriority** (src/TaskHandler/TaskPriority.cs)
- Enum defining standard priority levels
- Values: Urgent (0), High (1), Normal (2), Low (3), Background (4)

**TaskInfo** (src/TaskHandler/TaskInfo.cs)
- Immutable record for read-only task information
- Returned by `GetRunningTasksInfo()` to prevent mutation of running task state
- Contains: Id, Name, Status, Priority, Metadata

**TaskQueueStatistics** (src/TaskHandler/TaskQueueStatistics.cs)
- Performance metrics and statistics tracking
- Tracks total enqueued, completed, failed, canceled tasks
- Calculates average execution time and wait time
- Tracks last task started and completed timestamps
- Accessed via `queue.GetStatistics()`

**TaskProgress** (src/TaskHandler/TaskProgress.cs)
- Progress reporting support via `IProgress<TaskProgress>`
- Properties: Current, Total, PercentComplete, Message
- Used with tasks that accept `IProgress<TaskProgress>` parameter

**TaskRunWithTimeout** (src/TaskHandler/TaskRunWithTimeout.cs)
- Static utility class for running tasks with timeout constraints
- Generic method: `Task<T> Go<T>(Task<T> task, int timeoutMs, CancellationTokenSource tokenSource)`
- Uses `Task.WhenAny` to race the user task against `Task.Delay`
- Cancels the task via provided `CancellationTokenSource` on timeout
- Throws `TimeoutException` when timeout is exceeded

### Task Lifecycle

1. **Adding**: `EnqueueAsync()` or `AddTask()` creates `TaskDetails` and writes to the Channel
2. **Queueing**: Task waits in the Channel until a semaphore slot becomes available
3. **Starting**: `TaskRunner` reads from Channel and acquires semaphore, then starts task execution
4. **Monitoring**: Task completion triggers a continuation that handles cleanup
5. **Completion**: Continuation removes task from `_RunningTasks`, fires appropriate event (Finished/Faulted/Canceled), and releases semaphore slot
6. **Events**: Fires events at each lifecycle stage (Added, Started, Finished, Faulted, Canceled)

### Critical Implementation Details

- **Event-Driven Execution**: Uses `System.Threading.Channels` for queueing and `SemaphoreSlim` for concurrency control. No polling - tasks start instantly when capacity is available.

- **Concurrency Control**: `SemaphoreSlim` enforces `MaxConcurrentTasks` limit. Each task acquires a semaphore slot before starting and releases it upon completion via continuation.

- **Task Completion Handling**: Continuations check `TaskStatus` enum to determine completion type. Terminal states (`RanToCompletion`, `Faulted`, `Canceled`) trigger appropriate event handlers.

- **Backpressure**: Bounded channels (when `MaxQueueSize > 0`) provide backpressure by blocking or waiting when queue is full.

- **Cancellation**: Calling `Stop()` with no arguments cancels ALL running tasks. Calling `Stop(Guid)` cancels a specific task. The task runner itself can be stopped via `_TaskRunnerTokenSource`.

- **Statistics Tracking**: Tracks enqueue/completion counts, timing metrics, and calculates rolling averages for performance monitoring.

## Target Frameworks

The library multi-targets:
- netstandard2.0
- netstandard2.1
- net6.0
- net8.0

When making changes, ensure compatibility across all target frameworks.

## Testing

**Test.Automated** (src/Test.Automated/Program.cs) provides a comprehensive automated test suite with 38 tests covering:
- Tests 1-20: Core functionality, concurrency, cancellation, error handling, race conditions
- Tests 21-30: TaskHandle<T> with results, options pattern, priority, timeout, GetRunningTasksInfo
- Tests 31-38: Statistics tracking, progress reporting

Run all tests:
```bash
cd src/Test.Automated
dotnet run
```

All tests run on all target frameworks (netstandard2.0, netstandard2.1, net6.0, net8.0).

**Test** (src/Test/Program.cs) provides an interactive console application demonstrating TaskQueue usage. It creates tasks with varying delays and allows starting/stopping via console commands.

**Test.RunWithTimeout** and **Test.RunWithTimeoutHttp** test timeout functionality for the TaskRunWithTimeout utility.

## Coding Standards

**These rules MUST be followed strictly when modifying or adding code to this repository.**

### File Structure and Namespaces

- Namespace declaration must be at the top of the file
- Using statements must be INSIDE the namespace block
- Microsoft and standard system library usings come first, in alphabetical order
- Other using statements follow, in alphabetical order
- One class or one enum per file (no nesting multiple classes/enums)

**Example:**
```csharp
namespace TaskHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public class MyClass
    {
        // ...
    }
}
```

### Documentation

- All public members, constructors, and public methods MUST have XML documentation
- NO documentation on private members or private methods
- Document default values, minimum values, maximum values where applicable
- Document exceptions using `/// <exception>` tags
- Document thread safety guarantees
- Document nullability expectations

### Naming Conventions

- Private class member variables start with underscore, then Pascal case: `_FooBar` (NOT `_fooBar`)
- Do NOT use `var` - always use the actual type name
- Use meaningful names with context

### Properties and Validation

- Public members with value constraints must use explicit getters/setters with backing variables
- Validate ranges and null values in property setters
- Avoid constant values for things developers may want to configure - use configurable public members with reasonable defaults

**Example:**
```csharp
private int _MaxConcurrentTasks = 32;

/// <summary>
/// Maximum number of concurrent tasks.
/// Default: 32. Minimum: 1.
/// </summary>
public int MaxConcurrentTasks
{
    get
    {
        return _MaxConcurrentTasks;
    }
    set
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTasks));
        _MaxConcurrentTasks = value;
    }
}
```

### Async/Await Patterns

- Use `.ConfigureAwait(false)` where appropriate
- Every async method should accept a `CancellationToken` parameter (unless class has CancellationToken/CancellationTokenSource member)
- Check cancellation at appropriate points in async methods
- When implementing methods returning `IEnumerable`, also create async variants with `CancellationToken`

### Exception Handling

- Use specific exception types (not generic `Exception`)
- Include meaningful error messages with context
- Consider custom exception types for domain-specific errors
- Document exceptions in XML comments
- Use exception filters when appropriate: `catch (SqlException ex) when (ex.Number == 2601)`

### Resource Management

- Implement `IDisposable`/`IAsyncDisposable` when holding unmanaged resources or disposable objects
- Use `using` statements or `using` declarations for IDisposable objects
- Follow full Dispose pattern with `protected virtual void Dispose(bool disposing)`
- Always call `base.Dispose()` in derived classes

### Nullability and Validation

- Use nullable reference types (ensure `<Nullable>enable</Nullable>` in project files)
- Validate input parameters with guard clauses at method start
- Use `ArgumentNullException.ThrowIfNull()` for .NET 6+ or manual null checks
- Document nullability in XML comments
- Proactively identify and eliminate null reference exception scenarios

### Concurrency and Thread Safety

- Document thread safety guarantees in XML comments
- Use `Interlocked` operations for simple atomic operations
- Prefer `ReaderWriterLockSlim` over `lock` for read-heavy scenarios

### LINQ Best Practices

- Prefer LINQ methods over manual loops when readability is not compromised
- Use `.Any()` instead of `.Count() > 0` for existence checks
- Be aware of multiple enumeration issues - consider `.ToList()` when needed
- Use `.FirstOrDefault()` with null checks rather than `.First()` when element might not exist

### Code Organization

- Regions for Public-Members, Private-Members, Constructors-and-Factories, Public-Methods, and Private-Methods are NOT required for small files under 500 lines
- For files over 500 lines, use the standard regions as seen in existing code

### Library-Specific Rules

- **NO `Console.WriteLine` statements in library code** - use the `Logger` callback instead
- Do not use tuples unless absolutely necessary
- If SQL statements are manually prepared, there is a good reason - do not change to ORM patterns without discussion
- Do not make assumptions about opaque class members/methods - ask for implementation details

### Compilation

- Before committing changes, compile the code and ensure it is free of errors and warnings
- Test across all target frameworks (netstandard2.0, netstandard2.1, net6.0, net8.0)
