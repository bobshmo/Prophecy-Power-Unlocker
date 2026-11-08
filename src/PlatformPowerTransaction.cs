using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
using System.Text.Json;

namespace VictusPowerUnlockGui;

internal sealed record PlatformPowerSnapshot(PcfPowerValues Limits, bool Boost, uint CurrentMw, uint MaxMw, bool CurrentIsRequested=true)
{
    internal string Context => ContextFor(PcfPowerFields.ACMaxGPULimit);
    internal string ContextFor(PcfPowerFields field) => JsonSerializer.Serialize(new
    {
        Tpp = field == PcfPowerFields.ACTargetTPPLimit ? 0 : Limits.ACTargetTPPLimitInMilliwatts,
        GpuMax = field == PcfPowerFields.ACMaxGPULimit ? 0 : Limits.ACMaxGPULimitInMilliwatts,
        Limits.ACDefaultGPULimitInMilliwatts,
        Limits.ACMinGPULimitInMilliwatts,
        Boost,
        CurrentMw = CurrentIsRequested ? CurrentMw : (uint?)null,
        CurrentIsRequested,
        MaxMw
    });
}

internal interface IPlatformPowerSession : IDisposable
{
    PcfPowerValues ReadLimits();
    bool ReadBoost();
    (uint Current, uint Max) ReadGpu();
    bool CanRestoreCurrent { get; }
    bool CurrentIsRequested => false;
    void WriteLimits(PcfPowerValues values);
    void WriteBoost(bool value);
    void WriteCurrent(uint value);
    void ResetOverrides();
    IDisposable PrepareHp();
}

internal sealed class NativePlatformPowerSession : IPlatformPowerSession
{
    private readonly uint _controllerIndex;
    private readonly uint _layoutVersion;
    private bool _disposed;
    private readonly PowerBackend _gpu = new();
    public NativePlatformPowerSession()
    {
        using var pcf = PcfControllerDiscovery.Open();
        _controllerIndex = pcf.ControllerIndex;
        _layoutVersion = pcf.LayoutVersion;
    }
    // Do not keep private PCF mode initialized while HP/CURRENT preparation opens
    // its own PCF snapshot. Pin discovery, but scope every private-mode call.
    private T WithPcf<T>(Func<PcfPowerController, T> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var pcf = new PcfPowerController(_controllerIndex);
        if (pcf.LayoutVersion != _layoutVersion)
            throw new InvalidOperationException("PCF controller layout changed during the operation. Recheck Compatibility.");
        return operation(pcf);
    }
    // SMI is a public/aggregate ceiling, not a verified getter for E633 source F7.
    public bool CurrentIsRequested => false;
    public bool CanRestoreCurrent => _gpu.CheckCompatibility().CurrentWritesReady;
    public PcfPowerValues ReadLimits() => WithPcf(pcf => pcf.GetPowerValues());
    public bool ReadBoost() => WithPcf(pcf => pcf.GetDynamicBoostEnabled());
    public (uint Current, uint Max) ReadGpu()
    {
        var value = _gpu.GetPowerState();
        return (Mw(value.CurrentW), Mw(value.MaxW));
    }
    private static uint Mw(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value <= 0 || value.Value > 1000)
            throw new InvalidOperationException("NVIDIA CURRENT/MAX readback is unavailable.");
        return checked((uint)Math.Round(value.Value * 1000));
    }
    public void WriteLimits(PcfPowerValues values) => WithPcf(pcf =>
    {
        // Preserve unchanged MIN/DEFAULT fields instead of turning every live
        // getter value into an explicit override. Order changed companions safely.
        HpPlatform.RestorePcfValues(values, pcf.GetPowerValues, pcf.SetPowerField);
        return 0;
    });
    public void WriteBoost(bool value) => WithPcf(pcf => { pcf.SetDynamicBoostEnabled(value); return 0; });
    public void ResetOverrides() => WithPcf(pcf => { pcf.ResetAllOverrides(); return 0; });
    public void WriteCurrent(uint value)
    {
        var result = _gpu.RestoreCurrentExact(value);
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }
    public IDisposable PrepareHp() => HpPlatform.PrepareGpuPower();
    public void Dispose() => _disposed = true;
}

internal static class PlatformPowerTransaction
{
    internal static readonly object Sync = new();
    private sealed class EmptyLease : IDisposable { public void Dispose() { } }

    internal static bool Same(PcfPowerValues a, PcfPowerValues b) =>
        a.ACTargetTPPLimitInMilliwatts == b.ACTargetTPPLimitInMilliwatts &&
        a.ACDefaultGPULimitInMilliwatts == b.ACDefaultGPULimitInMilliwatts &&
        a.ACMinGPULimitInMilliwatts == b.ACMinGPULimitInMilliwatts &&
        a.ACMaxGPULimitInMilliwatts == b.ACMaxGPULimitInMilliwatts;

    private static void ValidateLimits(PcfPowerValues value)
    {
        if (value.ACTargetTPPLimitInMilliwatts is < 1000 or > 1000000 ||
            value.ACMinGPULimitInMilliwatts is < 1000 or > 1000000 ||
            value.ACDefaultGPULimitInMilliwatts is < 1000 or > 1000000 ||
            value.ACMaxGPULimitInMilliwatts is < 1000 or > 1000000 ||
            value.ACMinGPULimitInMilliwatts > value.ACDefaultGPULimitInMilliwatts ||
            value.ACDefaultGPULimitInMilliwatts > value.ACMaxGPULimitInMilliwatts)
            throw new InvalidOperationException("PCF power fields must be positive with MIN ≤ DEFAULT ≤ MAX. Zero is not a documented operating cap.");
    }

    internal static PlatformPowerSnapshot Capture(IPlatformPowerSession session)
    {
        var gpu = session.ReadGpu();
        if (gpu.Current == 0 || gpu.Max == 0 || gpu.Current > 1000000 || gpu.Max > 1000000 || (long)gpu.Current > (long)gpu.Max + 10)
            throw new InvalidOperationException("NVIDIA CURRENT/MAX are unavailable or inconsistent.");
        var limits = session.ReadLimits();
        ValidateLimits(limits);
        bool boost = session.ReadBoost();
        var second = session.ReadGpu();
        if (second.Max != gpu.Max || (session.CurrentIsRequested && second.Current != gpu.Current))
            throw new InvalidOperationException("NVIDIA power policy changed during the snapshot. Recheck before testing.");
        return new(limits, boost, gpu.Current, gpu.Max, session.CurrentIsRequested);
    }

    private static void Verify(IPlatformPowerSession session, PcfPowerValues desired, bool boost, uint current, uint max, bool verifyCurrent = true)
    {
        var actual = session.ReadGpu();
        if (!Same(session.ReadLimits(), desired) || session.ReadBoost() != boost ||
            (verifyCurrent && Math.Abs((long)actual.Current - current) > 10) || actual.Current == 0 || actual.Current > actual.Max + 10 || actual.Max != max)
            throw new InvalidOperationException("PCF fields, Dynamic Boost and NVIDIA CURRENT/MAX did not match the required readback.");
    }

    internal static void Restore(IPlatformPowerSession session, PlatformPowerSnapshot original, bool releaseProbeOverrides = false)
    {
        // Clear temporary low MIN/DEFAULT overrides before restoring only values
        // that differ from the OEM policy in the unchanged HP operating mode.
        if (releaseProbeOverrides) session.ResetOverrides();
        // Restore only the four documented power values, never a raw getter/status
        // buffer. Numeric readback cannot establish private override ownership.
        session.WriteLimits(original.Limits);
        if (session.ReadBoost() != original.Boost) session.WriteBoost(original.Boost);
        var gpu = session.ReadGpu();
        if (gpu.Max != original.MaxMw)
            throw new InvalidOperationException("NVIDIA MAX changed during the operation. Driver restart/recovery required; restoration is incomplete.");
        if (original.CurrentIsRequested && Math.Abs((long)gpu.Current - original.CurrentMw) > 10)
        {
            if (!session.CanRestoreCurrent)
                throw new InvalidOperationException("The original NVIDIA CURRENT cannot be restored: validate the driver before power testing.");
            session.WriteCurrent(original.CurrentMw);
        }
        Verify(session, original.Limits, original.Boost, original.CurrentMw, original.MaxMw, original.CurrentIsRequested);
    }

    internal static string ApplyField(PcfPowerFields field, uint mw, bool prepareHp = false)
    {
        lock (Sync)
        {
            TppCalibration.CheckIdle();
            using var session = new NativePlatformPowerSession();
            return Apply(session, field, mw, prepareHp);
        }
    }

    internal static string Apply(IPlatformPowerSession session, PcfPowerFields field, uint mw, bool prepareHp)
    {
        if (field != PcfPowerFields.ACTargetTPPLimit && field != PcfPowerFields.ACMaxGPULimit)
            throw new ArgumentOutOfRangeException(nameof(field));
        if (mw is < 1000 or > 1000000)
            throw new ArgumentOutOfRangeException(nameof(mw), "Use a positive power target; zero is not a documented operating cap.");
        lock (Sync)
        {
            var original = Capture(session);
            return ApplyDesired(session, original, original.Limits.With(field, mw), prepareHp, field.ToString());
        }
    }

    internal static string ApplyDesired(IPlatformPowerSession session, PlatformPowerSnapshot original, PcfPowerValues desired, bool prepareHp, string label)
    {
        lock (Sync)
        {
            ValidateLimits(desired);
            if (original.CurrentIsRequested && !session.CanRestoreCurrent)
                throw new InvalidOperationException("Validate the NVIDIA driver before changing PCF fields so the original CURRENT can be retained.");
            IDisposable? hp = null;
            try
            {
                if (prepareHp)
                {
                    hp = session.PrepareHp();
                    var prepared = session.ReadLimits();
                    // Preserve explicit requested changes and raised user caps,
                    // while inheriting unchanged base fields from the new mode.
                    desired = new PcfPowerValues(
                        desired.ACTargetTPPLimitInMilliwatts == original.Limits.ACTargetTPPLimitInMilliwatts
                            ? Math.Max(desired.ACTargetTPPLimitInMilliwatts, prepared.ACTargetTPPLimitInMilliwatts) : desired.ACTargetTPPLimitInMilliwatts,
                        desired.ACDefaultGPULimitInMilliwatts == original.Limits.ACDefaultGPULimitInMilliwatts
                            ? prepared.ACDefaultGPULimitInMilliwatts : desired.ACDefaultGPULimitInMilliwatts,
                        desired.ACMinGPULimitInMilliwatts == original.Limits.ACMinGPULimitInMilliwatts
                            ? prepared.ACMinGPULimitInMilliwatts : desired.ACMinGPULimitInMilliwatts,
                        desired.ACMaxGPULimitInMilliwatts == original.Limits.ACMaxGPULimitInMilliwatts
                            ? Math.Max(desired.ACMaxGPULimitInMilliwatts, prepared.ACMaxGPULimitInMilliwatts) : desired.ACMaxGPULimitInMilliwatts);
                    ValidateLimits(desired);
                }
                session.WriteLimits(desired);
                if (session.ReadBoost() != original.Boost) session.WriteBoost(original.Boost);
                var gpu = session.ReadGpu();
                if (gpu.Max != original.MaxMw)
                    throw new InvalidOperationException("NVIDIA MAX changed during the operation.");
                if (original.CurrentIsRequested && Math.Abs((long)gpu.Current - original.CurrentMw) > 10) session.WriteCurrent(original.CurrentMw);
                Verify(session, desired, original.Boost, original.CurrentMw, original.MaxMw, original.CurrentIsRequested);
                return $"{label} matched PCF readback; original Dynamic Boost and NVIDIA MAX retained. " +
                    (original.CurrentIsRequested ? "Configured CURRENT retained. " : "Public GPU ceiling observed only; no E633 request written. ") +
                    "Loaded power has not been verified.";
            }
            catch (Exception failure)
            {
                Rollback(session, original, failure);
                throw;
            }
            finally { hp?.Dispose(); }
        }
    }

    private static void Rollback(IPlatformPowerSession session, PlatformPowerSnapshot original, Exception failure)
    {
        try
        {
            using var recovery = session.CanRestoreCurrent ? session.PrepareHp() : new EmptyLease();
            Restore(session, original);
        }
        catch (Exception restoration)
        {
            throw new InvalidOperationException($"Apply failed ({failure.Message}) and full restoration failed: {restoration.Message}", failure);
        }
    }

    internal static OperationResult RecoverCurrent(int watts, int? tppWatts = null)
    {
        try
        {
            lock (Sync)
            {
                TppCalibration.CheckIdle();
                using var session = new NativePlatformPowerSession();
                return Recover(session, watts, tppWatts);
            }
        }
        catch (Exception failure) { return OperationResult.Fail(failure.Message); }
    }

    internal static OperationResult Recover(IPlatformPowerSession session, int watts, int? tppWatts)
    {
        if (watts is < 1 or > 1000 || tppWatts is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(watts));
        if (!session.CanRestoreCurrent) throw new InvalidOperationException("Validate the NVIDIA driver before GPU recovery.");
        var original = Capture(session);
        uint target = checked((uint)watts * 1000);
        uint budget = tppWatts.HasValue ? checked((uint)tppWatts.Value * 1000) : original.Limits.ACTargetTPPLimitInMilliwatts;
        if (target > original.MaxMw) throw new InvalidOperationException("Recovery target exceeds live NVIDIA MAX. Reboot and recheck the saved MAX override first.");
        if (budget <= target) throw new InvalidOperationException("TPP is a shared platform budget and must exceed the GPU target. Select a tested TPP with CPU/platform headroom before recovery; no settings changed.");
        IDisposable? hp = null;
        try
        {
            hp = session.PrepareHp();
            // HP's operating-mode switch can replace the OEM MIN/DEFAULT policy.
            // Recovery requests TPP and GPU MAX, not overrides of base values
            // observed in the previous mode. Read the prepared policy before SET.
            var prepared = session.ReadLimits();
            ValidateLimits(prepared);
            // Establish shared budget before raising the GPU contribution or issuing F7.
            var stage = prepared.With(PcfPowerFields.ACTargetTPPLimit, budget);
            session.WriteLimits(stage);
            if (!Same(session.ReadLimits(), stage)) throw new InvalidOperationException("Selected TPP did not match PCF readback; GPU request was not sent.");
            var desired = stage.With(PcfPowerFields.ACMaxGPULimit, Math.Max(stage.ACMaxGPULimitInMilliwatts, target));
            session.WriteLimits(desired);
            if (session.ReadBoost() != original.Boost) session.WriteBoost(original.Boost);
            Verify(session, desired, original.Boost, original.CurrentMw, original.MaxMw, false);
            session.WriteCurrent(target);
            Verify(session, desired, original.Boost, target, original.MaxMw, session.CurrentIsRequested);
            return OperationResult.Ok($"HP GPU state → TPP {budget / 1000.0:0.###} W → PCF GPU {desired.ACMaxGPULimitInMilliwatts / 1000.0:0.###} W → E633 request {watts} W accepted. Verify actual draw under load; CPU limits and overclock were not written. Source-specific CURRENT readback remains unavailable on the native path.");
        }
        catch (Exception failure) { Rollback(session, original, failure); throw; }
        finally { hp?.Dispose(); }
    }

    internal static void ApplyHpSettings(Action apply)
    {
        lock (Sync)
        {
            TppCalibration.CheckIdle();
            using var session = new NativePlatformPowerSession();
            PreserveAcrossHpSettings(session, apply);
        }
    }

    internal static string RestoreHpDefaults(Action restoreHp)
    {
        lock (Sync)
        {
            TppCalibration.CheckIdle();
            using var session = new NativePlatformPowerSession();
            return ResetAfterHpDefaults(session, restoreHp);
        }
    }

    internal static string ResetAfterHpDefaults(IPlatformPowerSession session, Action restoreHp)
    {
        lock (Sync)
        {
            var original = Capture(session);
            try
            {
                using var prepared = HpPlatform.EnterPrepared(restoreHp);
                session.ResetOverrides();
                var factory = session.ReadLimits();
                ValidateLimits(factory);
                var gpu = session.ReadGpu();
                if (gpu.Max != original.MaxMw || gpu.Current == 0 || gpu.Current > gpu.Max + 10)
                    throw new InvalidOperationException("NVIDIA MAX changed or CURRENT became inconsistent during the OEM policy reset.");
                if (!Same(factory, session.ReadLimits()))
                    throw new InvalidOperationException("OEM PCF defaults did not stabilize after the HP reset.");
                return $"HP default mode applied and PCF overrides released: ACTargetTPPLimit {factory.ACTargetTPPLimitInMilliwatts / 1000.0:0.###} W; ACMaxGPULimit {factory.ACMaxGPULimitInMilliwatts / 1000.0:0.###} W; DEFAULT {factory.ACDefaultGPULimitInMilliwatts / 1000.0:0.###} W; MIN {factory.ACMinGPULimitInMilliwatts / 1000.0:0.###} W.";
            }
            catch (Exception failure) { Rollback(session, original, failure); throw; }
        }
    }

    internal static void PreserveAcrossHpSettings(IPlatformPowerSession session, Action apply)
    {
        lock (Sync)
        {
            if (session.CurrentIsRequested && !session.CanRestoreCurrent)
                throw new InvalidOperationException("Validate the NVIDIA driver before HP GPU settings so a mode change cannot discard your existing power policy.");
            var original = Capture(session);
            try
            {
                using var prepared = HpPlatform.EnterPrepared(apply);
                Restore(session, original);
            }
            catch (Exception failure) { Rollback(session, original, failure); throw; }
        }
    }

    internal static OperationResult ReleasePcf()
    {
        try
        {
            lock (Sync)
            {
                TppCalibration.CheckIdle();
                using var pcf = PcfControllerDiscovery.Open();
                pcf.ResetAllOverrides();
                var value = pcf.GetPowerValues();
                return OperationResult.Ok($"PCF overrides released: TPP {value.ACTargetTPPLimitInMilliwatts / 1000.0:0.###} W; GPU cap {value.ACMaxGPULimitInMilliwatts / 1000.0:0.###} W. NVIDIA MAX and CPU controls were not written.");
            }
        }
        catch (Exception failure) { return OperationResult.Fail(failure.Message); }
    }

    internal static PcfPowerValues ProbeValues(PcfPowerValues original, PcfPowerFields field, uint target)
    {
        if (target is < 1000 or > 1000000)
            throw new ArgumentOutOfRangeException(nameof(target), "Use a positive target; zero is not a documented operating cap.");
        if (field != PcfPowerFields.ACMaxGPULimit && field != PcfPowerFields.ACTargetTPPLimit)
            throw new ArgumentOutOfRangeException(nameof(field));
        if (field == PcfPowerFields.ACTargetTPPLimit) return original.With(field, target);
        // A low MAX probe must not leave DEFAULT or MIN higher than MAX.
        return new(original.ACTargetTPPLimitInMilliwatts,
            Math.Min(original.ACDefaultGPULimitInMilliwatts, target),
            Math.Min(original.ACMinGPULimitInMilliwatts, target), target);
    }
}
