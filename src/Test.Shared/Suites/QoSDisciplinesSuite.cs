namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using QoSKit;
    using TaskHandler;
    using Test.Shared.Scheduling;
    using Touchstone.Core;

    /// <summary>
    /// Behavioral tests proving that QoSKit disciplines beyond strict priority actually shape which TaskQueue task
    /// starts next: weighted fairness ratios, priority aging, class-based fairness, weighted round robin, low-latency
    /// queuing with and without policing, priority across several concurrency slots, and slot accounting when the
    /// runner waits on a policed scheduler.
    /// Ratio tests load the scheduler while the queue is stopped and run one task at a time, so the order reflects the
    /// discipline rather than arrival timing.
    /// </summary>
    public static class QoSDisciplinesSuite
    {
        private const string Id = "QoSDisciplines";

        /// <summary>
        /// Build the QoS disciplines test suite.
        /// </summary>
        /// <returns>Test suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TaskHandlerSuites.Case(Id, "WeightedFairBeatsFifo", "Equal-weight fair queuing alternates tenants that FIFO would serve one after the other", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Wfq(new WeightedFlow("a", 1), new WeightedFlow("b", 1)), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 10; i++) AddKindTask(queue, log, "a", "a" + i);
                        for (int i = 0; i < 10; i++) AddKindTask(queue, log, "b", "b" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(20), "all ran");
                        int aFirst = log.CountInFirst("a", 10);
                        Check.InRange(aFirst, 4, 6, $"tenant a share of the first 10 starts (FIFO would give 10; order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "WeightedFairRatio", "A 3:1 weighted fair scheduler gives the heavier tenant about three starts per lighter-tenant start", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Wfq(new WeightedFlow("gold", 3), new WeightedFlow("bronze", 1)), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 30; i++)
                        {
                            AddKindTask(queue, log, "bronze", "bronze" + i);
                            AddKindTask(queue, log, "gold", "gold" + i);
                        }

                        queue.Start();
                        Check.True(await log.WaitForAsync(60), "all ran");
                        int goldFirst = log.CountInFirst("gold", 20);
                        Check.InRange(goldFirst, 13, 17, $"gold share of the first 20 starts, expected about 15 (order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "AgingDeterministic", "With aging, a background task that has waited past the threshold starts before newer urgent tasks; without aging it starts last", async ct =>
                {
                    Check.Equal(0, await BackgroundIndexWithManualClock(agingMs: 200), "aged background task starts first");
                    Check.Equal(10, await BackgroundIndexWithManualClock(agingMs: 0), "without aging the background task starts last");
                }),

                TaskHandlerSuites.Case(Id, "AgingPreventsStarvation", "Under a continuous stream of urgent work arriving faster than it drains, aging lets a waiting background task start long before the stream ends", async ct =>
                {
                    StartLog log = new StartLog();
                    PriorityQoSQueue<TaskDetails> scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority).WithAging(100);
                    using (TaskQueue queue = new TaskQueue(scheduler, maxConcurrentTasks: 1))
                    {
                        // Seed an urgent backlog, then add the background task, then keep urgent work arriving (every
                        // 10 ms) faster than it drains (20 ms each). The urgent band is never empty, so without aging the
                        // background task starts last (position 33).
                        queue.Start();
                        for (int i = 0; i < 3; i++) await EnqueueLogged(queue, log, "seed" + i, (int)TaskPriority.Urgent, 20);
                        await EnqueueLogged(queue, log, "background", (int)TaskPriority.Background, 20);
                        for (int i = 0; i < 30; i++)
                        {
                            await Task.Delay(10);
                            await EnqueueLogged(queue, log, "urgent" + i, (int)TaskPriority.Urgent, 20);
                        }

                        Check.True(await log.WaitForAsync(34, 20000), "all ran");
                        int index = log.Names().IndexOf("background");
                        Check.InRange(index, 2, 20, $"background start position (33 without aging; order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "ClassBasedFairWithDefault", "Class-based fair queuing honors class weights and serves unmatched tasks through class-default", async ct =>
                {
                    StartLog log = new StartLog();
                    ClassBasedWeightedFairQoSQueue<TaskDetails> scheduler = new ClassBasedWeightedFairQoSQueue<TaskDetails>(new[]
                    {
                        new TrafficClass<TaskDetails>("interactive", t => Kind(t) == "interactive", weight: 4),
                        new TrafficClass<TaskDetails>("batch", t => Kind(t) == "batch", weight: 1)
                    });
                    using (TaskQueue queue = new TaskQueue(scheduler, maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 20; i++) AddKindTask(queue, log, "batch", "batch" + i);
                        for (int i = 0; i < 20; i++) AddKindTask(queue, log, "interactive", "interactive" + i);
                        for (int i = 0; i < 4; i++) AddKindTask(queue, log, "other", "other" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(44), $"all ran, including unmatched tasks (order {log})");
                        Check.True(log.CountInFirst("interactive", 10) >= 6, $"interactive dominates the first 10 starts (order {log})");
                        Check.True(log.CountInFirst("other", 44) == 4, "class-default tasks all ran");
                    }
                }),

                TaskHandlerSuites.Case(Id, "WeightedRoundRobinRatio", "Weighted round robin in classifier mode serves sub-queues in proportion to weight", async ct =>
                {
                    StartLog log = new StartLog();
                    WeightedRoundRobinQoSQueue<TaskDetails> scheduler = new WeightedRoundRobinQoSQueue<TaskDetails>(
                        new[] { new WeightedSubQueue("a", 2), new WeightedSubQueue("b", 1) },
                        subQueueSelector: t => Kind(t));
                    using (TaskQueue queue = new TaskQueue(scheduler, maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 20; i++) AddKindTask(queue, log, "b", "b" + i);
                        for (int i = 0; i < 20; i++) AddKindTask(queue, log, "a", "a" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(40), "all ran");
                        Check.InRange(log.CountInFirst("a", 12), 7, 9, $"sub-queue a share of the first 12 starts, expected 8 (order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "WeightedRoundRobinUnknownKeyRejected", "A round-robin classifier that throws on unknown keys surfaces as a rejected add", async ct =>
                {
                    WeightedRoundRobinQoSQueue<TaskDetails> scheduler = new WeightedRoundRobinQoSQueue<TaskDetails>(
                        new[] { new WeightedSubQueue("a", 1) },
                        subQueueSelector: t => Kind(t),
                        unknownKeyPolicy: UnknownKeyPolicy.Throw);
                    using (TaskQueue queue = new TaskQueue(scheduler))
                    {
                        Check.Throws<InvalidOperationException>(() => queue.AddTask(Guid.NewGuid(), "x", Meta("zzz"), token => Task.CompletedTask), "unknown sub-queue");
                        Check.Equal(0, queue.QueuedCount, "nothing queued");
                        queue.AddTask(Guid.NewGuid(), "y", Meta("a"), token => Task.CompletedTask);
                        Check.Equal(1, queue.QueuedCount, "known sub-queue accepted");
                    }
                    await Task.CompletedTask;
                }),

                TaskHandlerSuites.Case(Id, "LowLatencyUnpolicedPriorityFirst", "An unpoliced low-latency priority class is served before every fair class", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Llq(rateLimit: null), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 5; i++) AddKindTask(queue, log, "fair", "fair" + i);
                        for (int i = 0; i < 5; i++) AddKindTask(queue, log, "voice", "voice" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(10), "all ran");
                        Check.Equal(5, log.CountInFirst("voice", 5), $"voice tasks start first (order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "LowLatencyPolicedDoesNotStarveFair", "A policed priority class is rate limited, so fair tasks run between priority tasks and all of them finish", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Llq(new TokenBucket(10, 1)), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 6; i++) AddKindTask(queue, log, "voice", "voice" + i);
                        for (int i = 0; i < 6; i++) AddKindTask(queue, log, "fair", "fair" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(12, 15000), $"all ran, including throttled priority tasks (order {log})");

                        List<double> voice = log.TimesOf("voice");
                        List<double> fair = log.TimesOf("fair");
                        Check.True(fair.Max() < voice.Max(), $"fair tasks are not held behind the whole priority backlog (order {log})");
                        Check.True(voice.Max() - voice.Min() >= 300, $"priority starts are spread out by the 10/s policer ({voice.Max() - voice.Min():F0} ms for 6 starts)");
                    }
                }),

                TaskHandlerSuites.Case(Id, "LowLatencyThrottledOnlyWakes", "When only throttled priority tasks remain, the runner waits for the policer to refill and keeps them flowing", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Llq(new TokenBucket(10, 1)), maxConcurrentTasks: 1))
                    {
                        for (int i = 0; i < 4; i++) AddKindTask(queue, log, "voice", "voice" + i);
                        queue.Start();
                        Check.True(await log.WaitForAsync(4, 10000), $"all throttled tasks ran (order {log})");
                        List<double> times = log.TimesOf("voice");
                        for (int i = 1; i < times.Count; i++)
                        {
                            Check.True(times[i] - times[i - 1] >= 50, $"start {i} respects the policer ({times[i] - times[i - 1]:F0} ms after the previous)");
                        }
                    }
                }),

                TaskHandlerSuites.Case(Id, "LowLatencyStopWhileWaitingReleasesSlot", "Stopping while the runner waits on a policed scheduler releases its slot; after restart the throttled task runs and the limit still holds", async ct =>
                {
                    StartLog log = new StartLog();
                    using (TaskQueue queue = new TaskQueue(Llq(new TokenBucket(2, 1)), maxConcurrentTasks: 1))
                    {
                        AddKindTask(queue, log, "voice", "voice0");
                        AddKindTask(queue, log, "voice", "voice1");
                        queue.Start();
                        Check.True(await log.WaitForAsync(1), "first priority task ran");
                        await Task.Delay(100);
                        Check.Equal(1, log.Count, "second is throttled");

                        queue.Stop();
                        Check.Equal(1, queue.QueuedCount, "throttled task retained");
                        queue.Start();
                        Check.True(await log.WaitForAsync(2, 5000), "throttled task runs after restart");

                        TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        for (int i = 0; i < 2; i++)
                        {
                            queue.AddTask(Guid.NewGuid(), "gated" + i, Meta("fair"), async token => await gate.Task);
                        }
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "one gated task running");
                        await Task.Delay(150);
                        Check.Equal(1, queue.RunningCount, "no slot was leaked or double-released: limit 1 still holds");
                        gate.SetResult(true);
                        await queue.WaitForCompletionAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "LowLatencyDisposeWhileWaiting", "Disposing while the runner waits on a policed scheduler settles the throttled task as canceled promptly", async ct =>
                {
                    StartLog log = new StartLog();
                    Guid throttledId = Guid.NewGuid();
                    bool throttledCanceled = false;
                    TaskQueue queue = new TaskQueue(Llq(new TokenBucket(0.5, 1)), maxConcurrentTasks: 1);
                    queue.OnTaskCanceled += (s, d) => { if (d.Guid == throttledId) throttledCanceled = true; };
                    queue.Start();
                    AddKindTask(queue, log, "voice", "voice0");
                    Check.True(await log.WaitForAsync(1), "first priority task ran");
                    queue.AddTask(throttledId, "voice1", Meta("voice"), async token => { log.Record("voice1"); await Task.Delay(1, token); });
                    await Task.Delay(100);
                    Check.Equal(1, log.Count, "second is throttled");

                    Stopwatch sw = Stopwatch.StartNew();
                    queue.Dispose();
                    Check.True(await Check.WaitUntilAsync(() => throttledCanceled, 3000), "throttled task settled as canceled by dispose");
                    Check.True(sw.ElapsedMilliseconds < 1000, $"settled promptly, not after the 2 s policer refill ({sw.ElapsedMilliseconds} ms)");
                    Check.Equal(0, queue.QueuedCount, "nothing left queued");
                    await Task.Delay(100);
                    Check.Equal(1, log.Count, "throttled task never ran");
                }),

                TaskHandlerSuites.Case(Id, "PriorityAcrossSlots", "With two concurrency slots, freed slots go to the highest-priority waiting tasks", async ct =>
                {
                    StartLog log = new StartLog();
                    TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 2))
                    {
                        queue.Start();
                        for (int i = 0; i < 2; i++) await queue.EnqueueAsync("blocker" + i, async token => await gate.Task, priority: 0);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 2), "both slots busy");

                        // Durations are staggered so slots free one at a time: urgent finishes first (normal takes its
                        // slot while high still runs), then high finishes (low takes that slot).
                        await EnqueueLogged(queue, log, "low", (int)TaskPriority.Low, 20);
                        await EnqueueLogged(queue, log, "normal", (int)TaskPriority.Normal, 200);
                        await EnqueueLogged(queue, log, "urgent", (int)TaskPriority.Urgent, 20);
                        await EnqueueLogged(queue, log, "high", (int)TaskPriority.High, 150);
                        gate.SetResult(true);

                        Check.True(await log.WaitForAsync(4), "all ran");
                        List<string> names = log.Names();
                        Check.True(names.Take(2).OrderBy(n => n).SequenceEqual(new[] { "high", "urgent" }), $"the two freed slots go to urgent and high (order {log})");
                        Check.Equal("normal,low", String.Join(",", names.Skip(2)), $"then normal, then low (order {log})");
                    }
                }),

                TaskHandlerSuites.Case(Id, "RaisingLimitStartsHighestPriority", "Raising MaxConcurrentTasks with a priority scheduler starts the highest-priority waiting tasks", async ct =>
                {
                    StartLog log = new StartLog();
                    TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await gate.Task);
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        foreach (string name in new[] { "low", "urgent", "normal", "high" })
                        {
                            TaskPriority priority = (TaskPriority)Enum.Parse(typeof(TaskPriority), name, true);
                            await queue.EnqueueAsync(name, async token => { log.Record(name); await gate.Task; }, priority: (int)priority);
                        }

                        queue.MaxConcurrentTasks = 3;
                        Check.True(await log.WaitForAsync(2), "two more started");
                        await Task.Delay(100);
                        Check.True(log.Names().OrderBy(n => n).SequenceEqual(new[] { "high", "urgent" }), $"urgent and high take the new slots (started {log})");
                        gate.SetResult(true);
                        await queue.WaitForCompletionAsync();
                    }
                }),

                TaskHandlerSuites.Case(Id, "StopGuidOnScheduledTask", "Stop(guid) on a task waiting in a scheduler cancels its handle immediately and it never runs", async ct =>
                {
                    bool victimRan = false;
                    using (TaskQueue queue = new TaskQueue(new PriorityQoSQueue<TaskDetails>(5, t => t.Priority), maxConcurrentTasks: 1))
                    {
                        queue.Start();
                        await queue.EnqueueAsync("blocker", async token => await Task.Delay(150, token));
                        Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 1), "blocker running");
                        TaskHandle<int> victim = await queue.EnqueueAsync<int>("victim", token => { victimRan = true; return Task.FromResult(1); }, priority: 0);
                        TaskHandle<int> other = await queue.EnqueueAsync<int>("other", token => Task.FromResult(2), priority: 4);

                        queue.Stop(victim.Id);
                        Check.True(victim.Task.IsCanceled, "handle canceled immediately");
                        Check.Equal(2, await other.Task, "other task still runs");
                        Check.True(await Check.WaitUntilAsync(() => queue.GetStatistics().TotalCanceled == 1 && queue.QueuedCount == 0), "victim settled as canceled");
                        Check.False(victimRan, "victim never runs");
                    }
                }),

                TaskHandlerSuites.Case(Id, "LoweringLimitRepeated", "Lowering MaxConcurrentTasks under load never lets more tasks run than the new limit (20 repetitions)", async ct =>
                {
                    for (int iteration = 0; iteration < 20; iteration++)
                    {
                        TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        int current = 0;
                        int peak = 0;
                        using (TaskQueue queue = new TaskQueue(maxConcurrentTasks: 3))
                        {
                            queue.Start();
                            for (int i = 0; i < 3; i++) await queue.EnqueueAsync("g" + i, async token => await gate.Task);
                            Check.True(await Check.WaitUntilAsync(() => queue.RunningCount == 3), "three running");
                            for (int i = 0; i < 3; i++)
                            {
                                await queue.EnqueueAsync("late" + i, async token =>
                                {
                                    int now = Interlocked.Increment(ref current);
                                    lock (gate) { peak = Math.Max(peak, now); }
                                    await Task.Delay(5);
                                    Interlocked.Decrement(ref current);
                                });
                            }

                            queue.MaxConcurrentTasks = 1;
                            gate.SetResult(true);
                            await queue.WaitForCompletionAsync();
                            Check.Equal(1, peak, $"iteration {iteration}: later tasks ran one at a time");
                        }
                    }
                })
            };

            return new TestSuiteDescriptor(Id, "QoS Disciplines (fairness, aging, CBWFQ, WRR, LLQ)", cases);
        }

        private static WeightedFairQoSQueue<TaskDetails> Wfq(params WeightedFlow[] flows)
        {
            return new WeightedFairQoSQueue<TaskDetails>(t => Kind(t), flows);
        }

        private static LowLatencyQoSQueue<TaskDetails> Llq(TokenBucket? rateLimit)
        {
            return new LowLatencyQoSQueue<TaskDetails>(
                new[] { new TrafficClass<TaskDetails>("voice", t => Kind(t) == "voice", rateLimit: rateLimit) },
                new[] { new TrafficClass<TaskDetails>("fair", t => Kind(t) == "fair", weight: 1) });
        }

        private static string Kind(TaskDetails taskDetails)
        {
            return taskDetails.Metadata != null && taskDetails.Metadata.TryGetValue("kind", out object? value) ? value as string ?? "" : "";
        }

        private static Dictionary<string, object> Meta(string kind)
        {
            return new Dictionary<string, object> { { "kind", kind } };
        }

        private static void AddKindTask(TaskQueue queue, StartLog log, string kind, string name)
        {
            queue.AddTask(Guid.NewGuid(), name, Meta(kind), async token =>
            {
                log.Record(name);
                await Task.Delay(1, token);
            });
        }

        private static async Task EnqueueLogged(TaskQueue queue, StartLog log, string name, int priority, int durationMs)
        {
            await queue.EnqueueAsync(name, async token =>
            {
                log.Record(name);
                await Task.Delay(durationMs, token);
            }, priority: priority);
        }

        private static async Task<int> BackgroundIndexWithManualClock(long agingMs)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            StartLog log = new StartLog();
            PriorityQoSQueue<TaskDetails> scheduler = new PriorityQoSQueue<TaskDetails>(5, t => t.Priority, new QoSQueueOptions { TimeProvider = clock });
            if (agingMs > 0) scheduler.WithAging(agingMs);

            using (TaskQueue queue = new TaskQueue(scheduler, maxConcurrentTasks: 1))
            {
                await EnqueueLogged(queue, log, "background", (int)TaskPriority.Background, 1);
                clock.Advance(500);
                for (int i = 0; i < 10; i++) await EnqueueLogged(queue, log, "urgent" + i, (int)TaskPriority.Urgent, 1);
                queue.Start();
                Check.True(await log.WaitForAsync(11), "all ran");
                return log.Names().IndexOf("background");
            }
        }
    }
}
