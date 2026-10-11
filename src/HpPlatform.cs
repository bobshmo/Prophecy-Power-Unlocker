using OmenCore.Hardware;

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
    private sealed class NoHpLease : IDisposable { public void Dispose() {} }

    private static bool? _fanMax;
    public static int SendCurrent(Func<int> write) => GuardCurrent(BeforeCurrentPower, write);
    private static int GuardCurrent(Func<IDisposable> prepare, Func<int> write)
    {
        using var lease = prepare();
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
        if (!IsVictus) return new NoHpLease();
        Monitor.Enter(Sync);
        try
        {
            using var hp = Open();
            // Same legacy performance-mode value used by the validated Victus HP path.
            var performanceMode=HpWmiMode.Enabled?HpWmiMode.ModeForPolicy((int)hp.ThermalPolicy,true):HpWmiBios.FanMode.LegacyPerformance;
            if (!hp.SetFanMode(performanceMode))
                throw new InvalidOperationException("HPCM performance mode was refused. GPU CURRENT was not sent.");
            NvpwrControlBlackwell.AppLog.Write("HPCM performance command accepted before GPU CURRENT.");
            if (_fanMax == true && (!hp.SetFanMax(true) || hp.GetFanMax() != true))
                throw new InvalidOperationException("Could not preserve Fan Max after HPCM. GPU CURRENT was not sent.");
            return new HeldLock();
        }
        catch { Monitor.Exit(Sync); throw; }
    }
    private sealed class HeldLock : IDisposable
    {
        public void Dispose() => Monitor.Exit(Sync);
    }
    private static HpWmiBios Open()
    {
        var hp = new HpWmiBios();
        if (!hp.IsAvailable) { string message = hp.Status; hp.Dispose(); throw new InvalidOperationException("HP interface unavailable: " + message); }
        return hp;
    }
}
