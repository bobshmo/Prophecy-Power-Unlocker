using NvpwrControlBlackwell;

namespace VictusPowerUnlockGui;

internal sealed record GpuLoadSample(double? CurrentW, double? MaxW, double? DrawW, double? LoadPct, double? TemperatureC);
internal enum GpuLoadOutcome { InsufficientLoad, PolicyChanged, DrawBelowTarget, DrawNearTarget }
internal sealed record GpuLoadResult(GpuLoadOutcome Outcome, int Samples, int LoadedSamples,
    double? TargetW, double? AverageW, double? PeakW, double? PeakTemperatureC)
{
    internal string Describe() => Outcome switch
    {
        GpuLoadOutcome.InsufficientLoad => "Inconclusive: keep a GPU workload above 90% utilization running for the full observation. No power setting was changed.",
        GpuLoadOutcome.PolicyChanged => "Inconclusive: NVIDIA CURRENT/MAX changed or became unavailable during observation. Recheck the active power policy before comparing runs.",
        GpuLoadOutcome.DrawNearTarget => $"Observed draw near CURRENT {TargetW:0.##} W: loaded average {AverageW:0.##} W, peak {PeakW:0.##} W ({LoadedSamples}/{Samples} loaded samples). This verifies this run, not a platform maximum.",
        _ => $"Draw did not reach CURRENT {TargetW:0.##} W: loaded average {AverageW:0.##} W, peak {PeakW:0.##} W ({LoadedSamples}/{Samples} loaded samples). A matching cap readback alone has not verified the unlock."
    };
}

// Observation is deliberately separate from idle write acceptance. It has no setters.
internal static class GpuLoadVerification
{
    internal static GpuLoadResult Analyze(IReadOnlyList<GpuLoadSample> samples)
    {
        static bool Valid(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value >= 0;
        var loaded = samples.Where(s => Valid(s.LoadPct) && s.LoadPct >= 90 && Valid(s.DrawW)).ToArray();
        double? average = loaded.Length == 0 ? null : loaded.Average(s => s.DrawW!.Value);
        double? peak = loaded.Length == 0 ? null : loaded.Max(s => s.DrawW!.Value);
        var temperatures = loaded.Where(s => Valid(s.TemperatureC)).Select(s => s.TemperatureC!.Value).ToArray();
        double? temperature = temperatures.Length == 0 ? null : temperatures.Max();
        double? target = samples.Count > 0 ? samples[0].CurrentW : null;
        GpuLoadResult Result(GpuLoadOutcome outcome) => new(outcome, samples.Count, loaded.Length, target, average, peak, temperature);
        if (samples.Count == 0 || !Valid(target) || target <= 0 || !Valid(samples[0].MaxW) ||
            samples.Any(s => !Valid(s.CurrentW) || !Valid(s.MaxW) ||
                Math.Abs(s.CurrentW!.Value - target.Value) > 0.51 ||
                Math.Abs(s.MaxW!.Value - samples[0].MaxW!.Value) > 0.51 || s.CurrentW > s.MaxW + 0.51))
            return Result(GpuLoadOutcome.PolicyChanged);
        if (loaded.Length < 8 || loaded.Length < samples.Count * 0.7)
            return Result(GpuLoadOutcome.InsufficientLoad);
        double tolerance = Math.Max(5, target.Value * 0.05);
        int nearTarget = loaded.Count(s => s.DrawW >= target.Value - tolerance);
        return Result(average >= target.Value - tolerance && nearTarget >= loaded.Length * 0.7
            ? GpuLoadOutcome.DrawNearTarget : GpuLoadOutcome.DrawBelowTarget);
    }

    internal static GpuLoadResult Observe(CancellationToken cancel, Action<int>? progress = null)
    {
        var samples = new List<GpuLoadSample>();
        for (int i = 0; i < 15; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var t = SystemProbe.GetTelemetry();
            samples.Add(new(t.Power.CurrentW, t.Power.MaxW, t.PowerW, t.UtilizationPct, t.GpuTempC));
            progress?.Invoke(i + 1);
            if (i < 14 && cancel.WaitHandle.WaitOne(1000)) cancel.ThrowIfCancellationRequested();
        }
        return Analyze(samples);
    }
}
