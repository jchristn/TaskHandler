![alt tag](https://raw.githubusercontent.com/jchristn/TaskHandler/main/Assets/logo.png =250x250)

# TaskHandler

[![NuGet Version](https://img.shields.io/nuget/v/TaskHandler.svg?style=flat)](https://www.nuget.org/packages/TaskHandler/) [![NuGet](https://img.shields.io/nuget/dt/TaskHandler.svg)](https://www.nuget.org/packages/TaskHandler) 

A simple C# class library to help manage running a queue of tasks without relinquishing control.

## New in v1.0.x

- Initial release

## Feedback or Issues

Encounter a bug?  Think of a way this library could be even better?  Please file an issue here!

## Test App

Please refer to the ```Test``` project for a full example of how to exercise the class library.
 
## Example

```csharp
using TaskHandler;

TaskQueue queue = new TaskQueue();      // allow up to 32 concurrent tasks
TaskQueue queue = new TaskQueue(16);    // allow up to 16 concurrent tasks

queue.AddTask(
  Guid.NewGuid(),                       // unique identifier
  "Task 1",                             // name for the task
  new Dictionary<string, object>(),     // any metadata you like!
  delegate(CancellationToken token) {   // your task in form of Action<CancellationToken>
  	Console.WriteLine("Task 1 starting!");
  	Task.Delay(10000).Wait();
  	Console.WriteLine("Task 1 ending!");
  });

queue.AddTask(
  Guid.NewGuid(),                       // unique identifier
  "Task 2",                             // name for the task
  new Dictionary<string, object>(),     // any metadata you like!
  delegate(CancellationToken token) {   // your task in form of Action<CancellationToken>
  	Console.WriteLine("Task 2 starting!");
  	Task.Delay(5000).Wait();
  	Console.WriteLine("Task 2 ending!");
  });

queue.Start();
Console.WriteLine(queue.RunningCount);  // Integer, the number of running tasks
queue.Stop([guid]);                     // Cancel a specific task
queue.Stop();                           // Cancel all tasks
```

## For Control Freaks
```csharp
queue.Logger = Console.WriteLine;       // For debug messages
queue.OnTaskAdded += ...                // When a task is added
queue.OnTaskStarted += ...              // When a task starts
queue.OnTaskFinished += ...             // When a task finishes
queue.OnTaskFaulted += ...              // When a task faults
queue.OnTaskCanceled += ...             // When a task is canceled
queue.OnProcessingStarted += ...        // When the task queue is started
queue.OnProcessingStopped += ...        // When the task queue is stopped
```

## Version History

Please refer to CHANGELOG.md for version history.
