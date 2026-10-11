using OmenCore.Hardware;
using System.Management;
namespace VictusPowerUnlockGui;
internal sealed record HpModeScan(int Policy,string Mode,bool ExtremeSupported,bool ExtremeUnlocked,string Details);
internal static class HpWmiMode
{
    internal static bool Enabled;
    internal static HpWmiBios.FanMode ModeForPolicy(int policy,bool performance)=>policy<1?
        (performance?HpWmiBios.FanMode.LegacyPerformance:HpWmiBios.FanMode.LegacyDefault):
        (performance?HpWmiBios.FanMode.Performance:HpWmiBios.FanMode.Default);
    internal static HpModeScan Scan(int watts)
    {
        string manufacturer="";
        using(var query=new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem"))
            foreach(ManagementObject item in query.Get())manufacturer=Convert.ToString(item["Manufacturer"])??"";
        if(!manufacturer.Equals("HP",StringComparison.OrdinalIgnoreCase)&&!manufacturer.Contains("Hewlett",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("HP WMI modes require an HP board.");
        using var hp=new HpWmiBios();if(!hp.IsAvailable)throw new InvalidOperationException(hp.Status);
        int policy=(int)hp.ThermalPolicy;
        object? design=hp.SystemDesign;
        bool Flag(string name)=>design?.GetType().GetProperty(name)?.GetValue(design) is true;
        bool extreme=Flag("IsExtremeModeSupport"),unlocked=Flag("IsExtremeModeUnlock");
        string mode=policy<1?"Legacy Performance":"Performance";
        return new(policy,mode,extreme,unlocked,$"HP WMI policy V{policy}; selected CURRENT {watts} W → {mode}. Unleashed: {(extreme?(unlocked?"firmware flag unlocked; command not verified":"firmware flag supported but locked"):"not advertised")}. WMI acceptance does not confirm sustained wattage.");
    }
    internal static string Apply(HpModeScan scan,bool enabled)
    {
        lock(HpPlatform.Sync){
            var current=Scan(0);if(current.Policy!=scan.Policy)throw new InvalidOperationException("HP thermal policy changed; scan again.");
            using var hp=new HpWmiBios();
            var mode=ModeForPolicy(scan.Policy,enabled);
            if(!hp.SetFanMode(mode))throw new InvalidOperationException("HP refused the selected WMI mode.");
            Enabled=enabled;
            return $"HP WMI accepted {mode}. {(enabled?"Selected mode will prepare CURRENT writes.":"Default mode requested.")}";
        }
    }
}
