namespace VictusPowerUnlockGui;
internal static class GpuLoadVerificationTests
{
    internal static void Run()
    {
        GpuLoadSample[] Run(double draw, double load = 100) => Enumerable.Repeat(new GpuLoadSample(140, 140, draw, load, 55), 15).ToArray();
        if (GpuLoadVerification.Analyze(Run(70)).Outcome != GpuLoadOutcome.DrawBelowTarget ||
            GpuLoadVerification.Analyze(Run(97)).Outcome != GpuLoadOutcome.DrawBelowTarget)
            throw new Exception("Matching 140 W readback must not verify a 70/97 W loaded draw.");
        if (GpuLoadVerification.Analyze(Run(137)).Outcome != GpuLoadOutcome.DrawNearTarget)
            throw new Exception("A sustained near-target loaded run was not recognized.");
        if (GpuLoadVerification.Analyze(Run(140, 10)).Outcome != GpuLoadOutcome.InsufficientLoad)
            throw new Exception("Idle power was treated as an unlock measurement.");
        var changed = Run(137); changed[7] = changed[7] with { CurrentW = 115 };
        if (GpuLoadVerification.Analyze(changed).Outcome != GpuLoadOutcome.PolicyChanged)
            throw new Exception("Policy changes must invalidate a comparison.");
        var spike = Run(70); spike[7] = spike[7] with { DrawW = 140 };
        if (GpuLoadVerification.Analyze(spike).Outcome != GpuLoadOutcome.DrawBelowTarget)
            throw new Exception("One power spike must not verify sustained power.");
        var absent = Run(137).Select(s => s with { DrawW = null }).ToArray();
        if (GpuLoadVerification.Analyze(absent).Outcome != GpuLoadOutcome.InsufficientLoad)
            throw new Exception("Missing draw telemetry must be inconclusive.");
    }
}
