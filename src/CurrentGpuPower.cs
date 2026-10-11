using NvpwrControlBlackwell;

namespace VictusPowerUnlockGui;

internal sealed class CurrentGpuPower
{
    private readonly PowerBackend _backend = new();
    public static bool NeedsMaxChange(double? liveMax, int watts)
    {
        if (!liveMax.HasValue) throw new InvalidOperationException("Live NVIDIA MAX is unavailable. Read compatibility in NVIDIA power & telemetry.");
        return watts > liveMax.Value + 0.01;
    }
    public string Apply(int watts)
    {
        var compatibility = _backend.CheckCompatibility();
        if (compatibility.Profile == null || watts < 1 || watts > int.MaxValue/1000)
            throw new InvalidOperationException("Unsupported target or GPU: " + compatibility.Reason);
        var live = _backend.GetPowerState();
        if (NeedsMaxChange(live.MaxW, watts))
        {
            var result = _backend.SetMaxOverride(watts);
            Require(result);
            return "NVIDIA MAX override saved. " + result.Message + " CURRENT has not been raised yet.";
        }
        var applied = _backend.SetCurrent(watts);
        Require(applied);
        return "NVIDIA CURRENT: " + applied.Message;
    }
    public string Read()
    {
        var c = _backend.CheckCompatibility();
        var live = _backend.GetPowerState();
        var pending = _backend.GetInstalledMaxOverrideTarget();
        return $"NVIDIA CURRENT {live.CurrentW?.ToString("0.##") ?? "unavailable"} W; live MAX {live.MaxW?.ToString("0.##") ?? "unavailable"} W; saved MAX {pending?.ToString() ?? "none"}. {c.Reason}";
    }
    public string Restore()
    {
        var c = _backend.CheckCompatibility();
        if (c.Profile == null) throw new InvalidOperationException(c.Reason);
        int stock = c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0 ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
        var current = _backend.SetCurrent(stock); Require(current);
        var max = _backend.RemoveMaxOverride(); Require(max);
        return current.Message + Environment.NewLine + max.Message;
    }
    private static void Require(OperationResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }
    public static void TestRouting()
    {
        if (!NeedsMaxChange(115, 125) || NeedsMaxChange(125, 125) || NeedsMaxChange(140, 125))
            throw new Exception("NVIDIA MAX/CURRENT routing failed.");
        try { NeedsMaxChange(null, 125); throw new Exception("Missing readback accepted."); }
        catch (InvalidOperationException) { }
    }
}
