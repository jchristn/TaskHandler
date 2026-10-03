namespace Test.Shared.Telemetry
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using TaskHandler;

    /// <summary>
    /// In-memory collector for TaskHandler telemetry. Subscribes a <see cref="MeterListener"/> to the
    /// TaskHandler meter and an <see cref="ActivityListener"/> to the TaskHandler activity source (plus the
    /// test caller source), and records every measurement and every stopped activity.
    /// Dispose to unsubscribe.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        /// <summary>
        /// Name of the activity source used by tests to create caller and user spans.
        /// </summary>
        public const string CallerSourceName = "Test.Telemetry.Caller";

        /// <summary>
        /// Activity source used by tests to create caller and user spans.
        /// </summary>
        public static readonly ActivitySource CallerSource = new ActivitySource(CallerSourceName);

        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Activities = new ConcurrentQueue<Activity>();
        private readonly HashSet<string> _PublishedMeters = new HashSet<string>();
        private readonly object _Lock = new object();

        /// <summary>
        /// Instantiate and subscribe.
        /// </summary>
        public TelemetryCapture()
        {
            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TaskHandlerTelemetryNames.ActivitySourceName || source.Name == CallerSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _Activities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_ActivityListener);

            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != TaskHandlerTelemetryNames.MeterName) return;
                lock (_Lock)
                {
                    _PublishedMeters.Add(instrument.Meter.Name + "@" + (instrument.Meter.Version ?? ""));
                }

                listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.Start();
        }

        /// <summary>
        /// Meter name and version pairs ("name@version") that were published to this listener.
        /// </summary>
        public IReadOnlyCollection<string> PublishedMeters
        {
            get
            {
                lock (_Lock)
                {
                    return _PublishedMeters.ToList();
                }
            }
        }

        /// <summary>
        /// Every captured measurement.
        /// </summary>
        public IReadOnlyList<CapturedMeasurement> AllMeasurements
        {
            get
            {
                return _Measurements.ToList();
            }
        }

        /// <summary>
        /// Every captured (stopped) activity.
        /// </summary>
        public IReadOnlyList<Activity> AllActivities
        {
            get
            {
                return _Activities.ToList();
            }
        }

        /// <summary>
        /// Poll all observable instruments now, capturing their current values.
        /// </summary>
        public void RecordObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Measurements for an instrument, filtered to a queue name when supplied.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="queueName">Queue name filter, or null for no filter.</param>
        /// <returns>Matching measurements.</returns>
        public List<CapturedMeasurement> Measurements(string instrument, string? queueName)
        {
            return _Measurements
                .Where(m => m.Instrument == instrument)
                .Where(m => queueName == null || m.Tag(TaskHandlerTelemetryNames.AttrQueueName) == queueName)
                .ToList();
        }

        /// <summary>
        /// Sum of values for an instrument and queue, optionally filtered by one tag.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="queueName">Queue name filter, or null.</param>
        /// <param name="tagKey">Optional tag key filter.</param>
        /// <param name="tagValue">Required tag value when tagKey is supplied.</param>
        /// <returns>Sum of matching values.</returns>
        public double Sum(string instrument, string? queueName, string? tagKey = null, string? tagValue = null)
        {
            return Filter(Measurements(instrument, queueName), tagKey, tagValue).Sum(m => m.Value);
        }

        /// <summary>
        /// Count of measurements for an instrument and queue, optionally filtered by one tag.
        /// For a histogram this is the number of recorded observations.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="queueName">Queue name filter, or null.</param>
        /// <param name="tagKey">Optional tag key filter.</param>
        /// <param name="tagValue">Required tag value when tagKey is supplied.</param>
        /// <returns>Count of matching measurements.</returns>
        public int Count(string instrument, string? queueName, string? tagKey = null, string? tagValue = null)
        {
            return Filter(Measurements(instrument, queueName), tagKey, tagValue).Count;
        }

        /// <summary>
        /// Most recent value of an observable instrument for a queue, or null if none was observed.
        /// Call <see cref="RecordObservables"/> first.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="queueName">Queue name.</param>
        /// <returns>Latest value, or null.</returns>
        public double? Latest(string instrument, string? queueName)
        {
            CapturedMeasurement? last = Measurements(instrument, queueName).LastOrDefault();
            return last?.Value;
        }

        /// <summary>
        /// Stopped activities with the given name whose taskhandler.queue.name tag matches.
        /// </summary>
        /// <param name="name">Activity (span) name.</param>
        /// <param name="queueName">Queue name, or null to skip the queue filter.</param>
        /// <returns>Matching activities.</returns>
        public List<Activity> Spans(string name, string? queueName)
        {
            return _Activities
                .Where(a => a.OperationName == name)
                .Where(a => queueName == null || (a.GetTagItem(TaskHandlerTelemetryNames.AttrQueueName) as string) == queueName)
                .ToList();
        }

        /// <summary>
        /// Stopped activities with the given name whose parent span id is the supplied span id.
        /// </summary>
        /// <param name="name">Activity (span) name.</param>
        /// <param name="parentSpanId">Parent span id.</param>
        /// <returns>Matching activities.</returns>
        public List<Activity> ChildSpans(string name, ActivitySpanId parentSpanId)
        {
            return _Activities.Where(a => a.OperationName == name && a.ParentSpanId == parentSpanId).ToList();
        }

        /// <summary>
        /// Unsubscribe both listeners.
        /// </summary>
        public void Dispose()
        {
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            Dictionary<string, object?> copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags) copy[tag.Key] = tag.Value;
            _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, Convert.ToDouble(value), copy));
        }

        private static List<CapturedMeasurement> Filter(List<CapturedMeasurement> source, string? tagKey, string? tagValue)
        {
            if (tagKey == null) return source;
            return source.Where(m => m.Tag(tagKey) == tagValue).ToList();
        }
    }
}
