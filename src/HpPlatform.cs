using System.Reflection;
using OmenCore.Hardware;
using NvAPIWrapper.GPU;

namespace VictusPowerUnlockGui;

internal static class HpPlatform
{
    internal static readonly object Sync = new();
    private static readonly Lazy<bool> VictusHost = new(() => {
        try { using var q = new System.Management.ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
            foreach (System.Management.ManagementObject o in q.Get())
                if ((Convert.ToString(o["Manufacturer"]) ?? "").Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                    (Convert.ToString(o["Model"]) ?? "").Contains("Victus", StringComparison.OrdinalIgnoreCase)) return true;
        } catch { }
        return false;
    });
    public static bool IsVictus => VictusHost.Value;
    private static readonly Lazy<bool> HpHost = new(() => {
        try { using var q = new System.Management.ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            foreach (System.Management.ManagementObject o in q.Get()) {
                string manufacturer = Convert.ToString(o["Manufacturer"]) ?? "";
                if (manufacturer.Equals("HP", StringComparison.OrdinalIgnoreCase) || manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) return true;
            }
        } catch { }
        return false;
    });
    [ThreadStatic] private static int _preparedDepth;
    private sealed class NoHpLease : IDisposable { public void Dispose() {} }

    private static bool? _fanMax;
    public static int SendCurrent(Func<int> write) => GuardCurrent(BeforeCurrentPower, write);
    internal static T WithCurrentPreparation<T>(Func<T> write) => GuardCurrent(BeforeCurrentPower, write);
    private static T GuardCurrent<T>(Func<IDisposable> prepare, Func<T> write)
    {
        using var lease = _preparedDepth > 0 ? new NoHpLease() : prepare();
        return write();
    }
    public static void TestCurrentGuard()
    {
        bool wrote = false;
        try { GuardCurrent(() => throw new InvalidOperationException("HP rejected"), () => { wrote = true; return 0; }); }
        catch (InvalidOperationException) { }
        if (wrote) throw new Exception("CURRENT executed after an HPCM failure.");
        var events = new List<string>();
        GuardCurrent(() => { events.Add("HPCM"); return new TestLease(events); }, () => { events.Add("CURRENT"); return 0; });
        if (string.Join(",", events) != "HPCM,CURRENT,release") throw new Exception("HPCM/CURRENT ordering failed.");
        events.Clear();
        GuardCurrent(() => { events.Add("HPCM"); return new TestLease(events); }, () => {
            events.Add("NvAPI open");
            try { events.Add("CURRENT"); return 0; }
            finally { events.Add("NvAPI close"); }
        });
        if (string.Join(",", events) != "HPCM,NvAPI open,CURRENT,NvAPI close,release")
            throw new Exception("CURRENT preparation must precede the NVIDIA session and remain held until it closes.");
        events.Clear();
        using (EnterPrepared(() => events.Add("prepared")))
        using (EnterPrepared(() => events.Add("duplicate preparation")))
            GuardCurrent(() => { events.Add("duplicate HPCM"); return new TestLease(events); }, () => { events.Add("CURRENT"); return 0; });
        if (string.Join(",", events) != "prepared,CURRENT" || Monitor.IsEntered(Sync)) throw new Exception("Prepared GPU transaction repeated HPCM or leaked its lock.");
        try { using var lease = EnterPrepared(() => throw new InvalidOperationException("GPU mode refused")); }
        catch (InvalidOperationException) { }
        if (Monitor.IsEntered(Sync)) throw new Exception("Failed HP GPU preparation leaked its lock.");
        events.Clear();
        GuardCurrent(() => { events.Add("HPCM"); return new TestLease(events); }, () => { events.Add("CURRENT"); return 0; });
        if (string.Join(",", events) != "HPCM,CURRENT,release") throw new Exception("Preparation depth leaked into an ordinary CURRENT write.");
        HpGpuPreparationTests.Run();
    }
    private sealed class TestLease(List<string> events) : IDisposable
    {
        public void Dispose() => events.Add("release");
    }
    public static bool? ReadFanMax()
    {
        lock (Sync) { using var hp = Open(); return _fanMax = hp.GetFanMax(); }
    }
    public static bool SetFanMax(bool enabled)
    {
        lock (Sync)
        {
            using var hp = Open();
            if (!hp.SetFanMax(enabled)) throw new InvalidOperationException("HP refused Fan Max.");
            var readback = hp.GetFanMax();
            _fanMax = readback;
            if (readback != enabled) throw new InvalidOperationException("Fan Max readback did not match; refresh the toggle before retrying.");
            return enabled;
        }
    }
    public static IDisposable BeforeCurrentPower()
    {
        if (_preparedDepth > 0) return new NoHpLease();
        if (!IsVictus) return new NoHpLease();
        Monitor.Enter(Sync);
        PcfPowerController? pcf = null;
        try
        {
            using var hp = Open();
            bool preserveFanMax = hp.GetFanMax() == true;
            byte gpsTarget = GetGpuGpsTarget(hp);
            PcfPowerValues? original = null;
            bool originalBoost;
            try { pcf = VictusPowerUnlockGui.PcfControllerDiscovery.Open(); original = pcf.GetPowerValues(); originalBoost = pcf.GetDynamicBoostEnabled(); }
            catch (Exception ex)
            {
                pcf?.Dispose(); pcf = null;
                throw new InvalidOperationException("HP CURRENT preparation stopped before HPCM because the PCF power state could not be captured. Recheck the platform controller in Compatibility; its TPP/GPU limits must be readable so performance mode cannot silently replace them. " + SafeError.Describe(ex), ex);
            }
            try
            {
                var performanceMode = HpWmiMode.ModeForPolicy((int)hp.ThermalPolicy, true);
                if (!hp.SetFanMode(performanceMode))
                    throw new InvalidOperationException("HPCM performance mode was refused. GPU CURRENT was not sent.");
                ConfigureGpuMaximum(hp, gpsTarget);
                NvpwrControlBlackwell.AppLog.Write("HPCM performance command accepted before GPU CURRENT; GPU GPS target preserved/repaired.");
                if (preserveFanMax && (!hp.SetFanMax(true) || hp.GetFanMax() != true))
                    throw new InvalidOperationException("Could not preserve Fan Max after HPCM. GPU CURRENT was not sent.");
            }
            finally
            {
                if (pcf != null && original != null)
                    RestorePcfState(original, originalBoost, pcf.GetPowerValues, pcf.SetPowerField, pcf.GetDynamicBoostEnabled, pcf.SetDynamicBoostEnabled);
            }
            return new HeldLock(pcf);
        }
        catch { pcf?.Dispose(); Monitor.Exit(Sync); throw; }
    }

    // This lease must remain on one thread. It prepares only HP's GPU mode; callers
    // then restore/apply PCF and CURRENT without a second mode command undoing them.
    internal static IDisposable PrepareGpuPower()
    {
        if (!HpHost.Value) return new NoHpLease();
        return EnterPrepared(() => {
            using var hp = Open();
            bool preserveFanMax = hp.GetFanMax() == true;
            byte gpsTarget = GetGpuGpsTarget(hp);
            var mode = HpWmiMode.ModeForPolicy((int)hp.ThermalPolicy, true);
            if (!hp.SetFanMode(mode)) throw new InvalidOperationException("HP performance mode was refused; GPU power preparation stopped.");
            ConfigureGpuMaximum(hp, gpsTarget);
            var state = hp.GetGpuPowerDetailed();
            if (!state.HasValue || !state.Value.customTgp || state.Value.ppabLevel < 1 || state.Value.dState != 1)
                throw new InvalidOperationException("HP GPU Maximum state did not read back as cTGP/PPAB/D1; GPU power preparation stopped.");
            if (preserveFanMax && (!hp.SetFanMax(true) || hp.GetFanMax() != true))
                throw new InvalidOperationException("HP GPU power preparation could not preserve Fan Max.");
            _fanMax = hp.GetFanMax();
            NvpwrControlBlackwell.AppLog.Write("HP " + mode + " and GPU Maximum cTGP/PPAB/D1 prepared; PLGPU and CPU SMU unchanged.");
        });
    }

    internal static IDisposable EnterPrepared(Action prepare)
    {
        Monitor.Enter(Sync);
        try
        {
            if (_preparedDepth == 0) prepare();
            _preparedDepth++;
            return new PreparedLease();
        }
        catch { Monitor.Exit(Sync); throw; }
    }

    internal static void RestorePcfValues(PcfPowerValues original, Func<PcfPowerValues> read, Action<PcfPowerFields, uint> write)
    {
        var actual = read();
        // Restore only exposed fields changed by HPCM. Never replay a raw GET buffer.
        // Raise MAX/lower MIN first, then DEFAULT, then lower MAX/raise MIN.
        // This keeps ordinary numeric MIN <= DEFAULT <= MAX during restoration,
        // including an intentionally low GPU cap that HPCM replaced with stock.
        if (actual.ACMaxGPULimitInMilliwatts < original.ACMaxGPULimitInMilliwatts) write(PcfPowerFields.ACMaxGPULimit, original.ACMaxGPULimitInMilliwatts);
        if (actual.ACMinGPULimitInMilliwatts > original.ACMinGPULimitInMilliwatts) write(PcfPowerFields.ACMinGPULimit, original.ACMinGPULimitInMilliwatts);
        if (actual.ACDefaultGPULimitInMilliwatts != original.ACDefaultGPULimitInMilliwatts) write(PcfPowerFields.ACDefaultGPULimit, original.ACDefaultGPULimitInMilliwatts);
        if (actual.ACMinGPULimitInMilliwatts < original.ACMinGPULimitInMilliwatts) write(PcfPowerFields.ACMinGPULimit, original.ACMinGPULimitInMilliwatts);
        if (actual.ACMaxGPULimitInMilliwatts > original.ACMaxGPULimitInMilliwatts) write(PcfPowerFields.ACMaxGPULimit, original.ACMaxGPULimitInMilliwatts);
        if (actual.ACTargetTPPLimitInMilliwatts != original.ACTargetTPPLimitInMilliwatts) write(PcfPowerFields.ACTargetTPPLimit, original.ACTargetTPPLimitInMilliwatts);
        actual = read();
        if (actual.ACTargetTPPLimitInMilliwatts != original.ACTargetTPPLimitInMilliwatts || actual.ACDefaultGPULimitInMilliwatts != original.ACDefaultGPULimitInMilliwatts ||
            actual.ACMinGPULimitInMilliwatts != original.ACMinGPULimitInMilliwatts || actual.ACMaxGPULimitInMilliwatts != original.ACMaxGPULimitInMilliwatts)
            throw new InvalidOperationException($"PCF target/readback mismatch: requested TPP/default/min/max {original.ACTargetTPPLimitInMilliwatts}/{original.ACDefaultGPULimitInMilliwatts}/{original.ACMinGPULimitInMilliwatts}/{original.ACMaxGPULimitInMilliwatts} mW; actual {actual.ACTargetTPPLimitInMilliwatts}/{actual.ACDefaultGPULimitInMilliwatts}/{actual.ACMinGPULimitInMilliwatts}/{actual.ACMaxGPULimitInMilliwatts} mW. GPU CURRENT was not sent.");
    }

    internal static void RestorePcfState(PcfPowerValues original, bool originalBoost, Func<PcfPowerValues> read,
        Action<PcfPowerFields, uint> write, Func<bool> readBoost, Action<bool> writeBoost)
    {
        RestorePcfValues(original, read, write);
        if (readBoost() != originalBoost) writeBoost(originalBoost);
        if (readBoost() != originalBoost)
            throw new InvalidOperationException("HP performance mode changed Dynamic Boost and restoration did not match. GPU CURRENT was not sent.");
    }

    private sealed class PreparedLease : IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("HP GPU preparation must be completed on its original thread.");
            _disposed = true; _preparedDepth--; Monitor.Exit(Sync);
        }
    }
    private sealed class HeldLock(PcfPowerController? pcf = null) : IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("HP CURRENT preparation must be completed on its original thread.");
            _disposed = true;
            try { pcf?.Dispose(); } finally { Monitor.Exit(Sync); }
        }
    }
    // The original 8DD2/F.15 ACPI GM22 writes byte 3 to PEGP.GPST and GM21
    // returns it. Do not zero a temperature control as though it were padding.
    internal static byte SelectGpsTarget(byte current, string board, string bios)
    {
        if (current != 0) return current;
        return board.Equals("8DD2", StringComparison.OrdinalIgnoreCase) && bios.Equals("F.15", StringComparison.OrdinalIgnoreCase) ? (byte)87 : current;
    }
    private static byte[] GpuCommand(HpWmiBios hp, uint command, byte[] payload, byte outputSize)
    {
        var method = typeof(HpWmiBios).GetMethod("SendBiosCommand", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(HpWmiBios.BiosCmd), typeof(uint), typeof(byte[]), typeof(byte)], modifiers: null)
            ?? throw new MissingMethodException("HP GPU command transport unavailable.");
        return method.Invoke(hp, [HpWmiBios.BiosCmd.Default, command, payload, outputSize]) as byte[]
            ?? throw new InvalidOperationException($"HP GPU command 0x{command:X2} failed.");
    }
    internal static byte GetGpuGpsTarget(HpWmiBios hp)
    {
        var raw = GpuCommand(hp, 0x21, new byte[4], 4);
        if (raw.Length < 4) throw new InvalidOperationException("HP GPU state is incomplete; no temperature-target payload selected.");
        byte current = raw[3];
        if (current != 0) return current;
        string board = "", bios = "";
        using (var q = new System.Management.ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard"))
            foreach (System.Management.ManagementObject item in q.Get()) board = Convert.ToString(item["Product"]) ?? "";
        using (var q = new System.Management.ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
            foreach (System.Management.ManagementObject item in q.Get()) bios = Convert.ToString(item["SMBIOSBIOSVersion"]) ?? "";
        byte target = SelectGpsTarget(current, board, bios);
        if (target != current) NvpwrControlBlackwell.AppLog.Write("Repairing zero GPU GPS target to ACPI initial 87 C on verified 8DD2/F.15; future nonzero targets are preserved.");
        return target;
    }
    internal static void ConfigureGpuMaximum(HpWmiBios hp, byte gpsTarget)
    {
        GpuCommand(hp, 0x22, [1, 1, 1, gpsTarget], 0);
        var raw = GpuCommand(hp, 0x21, new byte[4], 4);
        if (raw.Length < 4 || raw[0] != 1 || raw[1] < 1 || raw[2] != 1 || (gpsTarget != 0 && raw[3] != gpsTarget))
            throw new InvalidOperationException("HP GPU Maximum/GPS target readback mismatch; power request stopped.");
    }

    private static HpWmiBios Open()
    {
        var hp = new HpWmiBios();
        if (!hp.IsAvailable) { string message = hp.Status; hp.Dispose(); throw new InvalidOperationException("HP interface unavailable: " + message); }
        return hp;
    }
}
