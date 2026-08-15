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
- net8.0
- net10.0

When making changes, ensure compatibility across all target frameworks.

## Testing

Testing is built on **[Touchstone](https://github.com/jchristn/touchstone)**, a runner-agnostic test
descriptor framework. Test cases are defined once and executed through multiple hosts.

**Test.Shared** (src/Test.Shared) is the single source of truth for the test corpus. It exposes
`TaskHandlerSuites.All`, a set of `TestSuiteDescriptor` objects containing 110 exhaustive positive and
negative test cases organized into suites:
- Construction & configuration (constructors, options, Create factory, validation)
- Property validation
- Enqueue & execution (AddTask, AddTaskAsync, EnqueueAsync, high-throughput, bounded queue)
- Concurrency control
- Cancellation (individual, bulk, dispose)
- Lifecycle (Start/Stop/StartAsync/StopAsync/Dispose/DisposeAsync, restart cycles, guard conditions)
- Events (all lifecycle/task events, handler-exception safety, metadata)
- TaskHandle&lt;T&gt; results and exception propagation
- Statistics and metrics
- Progress reporting
- Task priority
- TaskInfo snapshots
- TaskProgress and TaskDetails value objects
- TaskRunWithTimeout

The `Check` helper (src/Test.Shared/Check.cs) provides assertions; a failed assertion throws
`AssertionException`, which Touchstone reports as a failed test.

The corpus is executed through three hosts, all consuming `TaskHandlerSuites.All`:

- **Test.Automated** (src/Test.Automated) — Touchstone CLI runner. Colored tabular output and a
  CI-friendly exit code. Optionally pass a path argument to export JSON results.
  ```bash
  cd src/Test.Automated
  dotnet run                       # runs the full corpus
  dotnet run -- results.json       # also exports JSON
  ```
- **Test.Xunit** (src/Test.Xunit) — Touchstone xUnit adapter (theory-driven; one xUnit test per case).
  ```bash
  dotnet test src/Test.Xunit/Test.Xunit.csproj
  ```
- **Test.Nunit** (src/Test.Nunit) — Touchstone NUnit adapter (TestCaseSource; one NUnit test per case).
  ```bash
  dotnet test src/Test.Nunit/Test.Nunit.csproj
  ```

Test.Shared and the three hosts target net8.0 and net10.0 (the frameworks supported by Touchstone).
When adding or changing tests, edit **Test.Shared only** — all three runners pick up the change
automatically.

The following are interactive console applications (not part of the automated corpus) and are retained
for manual, hands-on use of the system:

- **Test** (src/Test/Program.cs) — interactive menu demonstrating TaskQueue usage (add/start/stop/monitor).
- **Test.RunWithTimeout** and **Test.RunWithTimeoutHttp** — interactive tools exercising the
  TaskRunWithTimeout utility (the HTTP variant issues real requests against a user-supplied URL).

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
- Test across all target frameworks (netstandard2.0, netstandard2.1, net8.0, net10.0)
