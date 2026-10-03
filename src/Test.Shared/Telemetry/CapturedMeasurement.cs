namespace Test.Shared.Telemetry
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single metric measurement captured by <see cref="TelemetryCapture"/>.
    /// </summary>
    public class CapturedMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Instrument unit.
        /// </summary>
        public string? Unit { get; }

        /// <summary>
        /// Measured value (long values are widened to double).
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Measurement tags.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Tags { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="unit">Instrument unit.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Tags.</param>
        public CapturedMeasurement(string instrument, string? unit, double value, IReadOnlyDictionary<string, object?> tags)
        {
            Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            Unit = unit;
            Value = value;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        /// <summary>
        /// Get a tag value as a string, or null when absent.
        /// </summary>
        /// <param name="key">Tag key.</param>
        /// <returns>Tag value as string, or null.</returns>
        public string? Tag(string key)
        {
            return Tags.TryGetValue(key, out object? value) ? value?.ToString() : null;
        }
    }
}
