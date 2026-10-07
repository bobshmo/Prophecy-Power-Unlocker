using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace VictusPowerUnlockGui;

internal sealed record VoltageRange(int CurrentUv, int MinimumUv, int MaximumUv)
{
    public int Validate(decimal millivolts)
    {
        decimal uv = millivolts * 1000m;
        if (uv != decimal.Truncate(uv) || uv < MinimumUv || uv > MaximumUv)
            throw new ArgumentOutOfRangeException(nameof(millivolts), "Voltage offset is outside NVIDIA's reported range.");
        return checked((int)uv);
    }
}

internal interface IVoltageTransport
{
    VoltageRange Read();
    void Write(int microvolts);
}

internal sealed class NvidiaVoltageTransport : IVoltageTransport
{
    private PhysicalGPU? _gpu;
    private PhysicalGPU Gpu => _gpu ??= PhysicalGPU.GetPhysicalGPUs()
        .SingleOrDefault(g => NvpwrControlBlackwell.GpuProfiles.Detect(g.FullName,
            NvpwrControlBlackwell.SystemProbe.FindNvidiaDeviceInstanceId(g.FullName)) != null)
        ?? throw new NotSupportedException("Exactly one supported RTX laptop GPU must be present.");

    public VoltageRange Read()
    {
        var states = GPUApi.GetPerformanceStates20(Gpu.Handle);
        if (states.Voltages.TryGetValue(PerformanceStateId.P0_3DPerformance, out var entries))
            foreach (var entry in entries)
            {
                var delta = entry.ValueDeltaInMicroVolt;
                if (entry.DomainId == PerformanceVoltageDomain.Core && entry.IsEditable &&
                    delta.DeltaRange.Minimum <= delta.DeltaRange.Maximum &&
                    (delta.DeltaRange.Minimum != 0 || delta.DeltaRange.Maximum != 0))
                    return new(delta.DeltaValue, delta.DeltaRange.Minimum, delta.DeltaRange.Maximum);
            }
        throw new NotSupportedException("GPU mV offset unavailable: NVIDIA does not expose an editable NVVDD offset on this GPU/driver.");
    }

    public void Write(int microvolts)
    {
        Read().Validate(microvolts / 1000m);
        // Minimal P0 request: only NVVDD, with no clock entries or other states.
        var entry = new PerformanceStates20BaseVoltageEntryV1(PerformanceVoltageDomain.Core,
            new PerformanceStates20ParameterDelta(microvolts));
        var p0 = new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance,
            Array.Empty<PerformanceStates20ClockEntryV1>(), new[] { entry });
        GPUApi.SetPerformanceStates20(Gpu.Handle, new PerformanceStates20InfoV1(new[] { p0 }, 0, 1));
    }
}

internal sealed class GpuVoltageController(IVoltageTransport transport)
{
    private int? _baseline;
    public VoltageRange Read() => transport.Read();
    public VoltageRange Apply(decimal millivolts)
    {
        var before = Read();
        int requested = before.Validate(millivolts);
        _baseline ??= before.CurrentUv;
        try { transport.Write(requested); return Verify(requested); }
        catch (Exception applyError)
        {
            try { transport.Write(before.CurrentUv); Verify(before.CurrentUv); }
            catch (Exception rollbackError)
            { throw new InvalidOperationException($"Voltage apply failed: {applyError.Message}. Rollback also failed: {rollbackError.Message}"); }
            throw new InvalidOperationException($"Voltage apply failed; previous offset restored: {applyError.Message}");
        }
    }
    public VoltageRange? Restore()
    {
        if (!_baseline.HasValue) return null;
        var result = Apply(_baseline.Value / 1000m);
        _baseline = null;
        return result;
    }
    private VoltageRange Verify(int expected)
    {
        var result = Read();
        if (result.CurrentUv != expected) throw new InvalidOperationException("GPU voltage offset readback did not match.");
        return result;
    }
}

internal static class VoltageTests
{
    private sealed class Fake : IVoltageTransport
    {
        public int Value = -12500, Writes;
        public bool Mismatch;
        public VoltageRange Read() => new(Value, -100000, 100000);
        public void Write(int uv) { Writes++; Value = Mismatch ? uv + 1000 : uv; Mismatch = false; }
    }
    public static void Run()
    {
        var fake = new Fake(); var controller = new GpuVoltageController(fake);
        try { controller.Apply(101); throw new Exception("Invalid voltage accepted"); }
        catch (ArgumentOutOfRangeException) { }
        if (fake.Writes != 0) throw new Exception("Invalid voltage caused a write");
        controller.Apply(-25.5m);
        if (fake.Value != -25500) throw new Exception("mV conversion failed");
        controller.Restore();
        if (fake.Value != -12500) throw new Exception("Baseline restore failed");
        fake.Mismatch = true;
        try { controller.Apply(10); throw new Exception("Readback mismatch accepted"); }
        catch (InvalidOperationException) { }
        if (fake.Value != -12500) throw new Exception("Rollback failed");
    }
}
