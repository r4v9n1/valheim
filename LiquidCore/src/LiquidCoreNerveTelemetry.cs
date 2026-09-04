using System;
using System.Collections.Generic;
using System.Globalization;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;

namespace PhysicalWater
{
    /// <summary>
    /// Permanent allocation-free-on-idle causal latency telemetry. Samples are
    /// retained in fixed rings so median and p95 remain bounded over long runs.
    /// </summary>
    internal sealed class LiquidCoreNerveTelemetry
    {
        private sealed class EventStatistics
        {
            private const int Capacity = 128;
            private readonly double[] _samples = new double[Capacity];
            private readonly double[] _sorted = new double[Capacity];
            private int _next;
            private int _retained;
            private long _count;
            private double _sum;
            private double _worst;

            internal void Add(double value)
            {
                _samples[_next] = value;
                _next = (_next + 1) % Capacity;
                if (_retained < Capacity) _retained++;
                _count++;
                _sum += value;
                if (value > _worst) _worst = value;
            }

            internal string Describe()
            {
                for (int i = 0; i < _retained; i++) _sorted[i] = _samples[i];
                Array.Sort(_sorted, 0, _retained);
                double median = Percentile(0.50);
                double p95 = Percentile(0.95);
                return "n=" + _count +
                       ", mean=" + (_sum / Math.Max(1L, _count)).ToString("F3", CultureInfo.InvariantCulture) +
                       "ms, median=" + median.ToString("F3", CultureInfo.InvariantCulture) +
                       "ms, p95=" + p95.ToString("F3", CultureInfo.InvariantCulture) +
                       "ms, worst=" + _worst.ToString("F3", CultureInfo.InvariantCulture) + "ms";
            }

            private double Percentile(double fraction)
            {
                if (_retained == 0) return 0.0;
                int index = (int)Math.Ceiling(fraction * _retained) - 1;
                return _sorted[Math.Max(0, Math.Min(_retained - 1, index))];
            }
        }

        private sealed class EventAggregate
        {
            private readonly EventStatistics _sensing = new EventStatistics();
            private readonly EventStatistics _lc = new EventStatistics();
            private readonly EventStatistics _total = new EventStatistics();

            internal void Add(double sensing, double lc, double total)
            {
                _sensing.Add(sensing);
                _lc.Add(lc);
                _total.Add(total);
            }

            internal string Describe()
            {
                return "pce{" + _sensing.Describe() + "}, lc{" + _lc.Describe() +
                       "}, total{" + _total.Describe() + "}";
            }
        }

        private readonly Dictionary<string, EventAggregate> _statistics =
            new Dictionary<string, EventAggregate>(StringComparer.Ordinal);

        internal void RecordBatch(
            IReadOnlyList<ProbeColonyCausalGeometrySignal> signals,
            long applyStartTimestamp,
            long solverReadyTimestamp,
            double applyMilliseconds,
            VolumetricFiniteSolidUpdateDiagnostics update,
            int sdfCells,
            int cutCells,
            int apertureFaces,
            int gpuBytes)
        {
            if (signals == null) return;
            for (int i = 0; i < signals.Count; i++)
            {
                ProbeColonyCausalGeometrySignal signal = signals[i];
                double sensing = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    signal.EventTimestamp, signal.ReadyTimestamp);
                double readyWait = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    signal.ReadyTimestamp, applyStartTimestamp);
                double lc = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    signal.ReadyTimestamp, solverReadyTimestamp);
                double total = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    signal.EventTimestamp, solverReadyTimestamp);
                string eventType = signal.Category + "/" + signal.ChangeKind;
                if (!_statistics.TryGetValue(eventType, out EventAggregate statistics))
                {
                    statistics = new EventAggregate();
                    _statistics.Add(eventType, statistics);
                }
                statistics.Add(sensing, lc, total);

                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_NERVE_EVENT type=" + eventType +
                    ", source=" + signal.SourceId +
                    ", asset=" + (string.IsNullOrEmpty(signal.AssetClassId) ? "unknown" : signal.AssetClassId) +
                    ", db=" + (signal.DatabaseHit ? "hit" : "miss") +
                    ", revision=" + signal.SourceRevision +
                    ", handles=" + signal.SourceRuntimeHandle + "/" + signal.DependencyHandle +
                    ", handoff=shared-runtime/dependency-handles" +
                    ", copiedGeometryBytes=0" +
                    ", dirty=" + signal.DirtyWorldBounds +
                    ", ticks=" + signal.EventTimestamp + "/" + signal.ReadyTimestamp + "/" + solverReadyTimestamp +
                    ", sensing=" + sensing.ToString("F3", CultureInfo.InvariantCulture) +
                    "ms, readyWait=" + readyWait.ToString("F3", CultureInfo.InvariantCulture) +
                    "ms, apply=" + applyMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    "ms, lc=" + lc.ToString("F3", CultureInfo.InvariantCulture) +
                    "ms, total=" + total.ToString("F3", CultureInfo.InvariantCulture) +
                    "ms, full=" + update.Geometry.FullRebuild +
                    ", cells=" + update.Geometry.ChangedCells + "/" + update.Geometry.DirtyRegionCells +
                    ", gpu=" + sdfCells + "/" + cutCells + "/" + apertureFaces + "/" + gpuBytes +
                    ", aggregate[" + statistics.Describe() + "].");
            }
        }
    }
}
