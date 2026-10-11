using System.Management;
using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal sealed record PcfDetection(bool Hp,int StockW,uint OriginalMw,uint CurrentMw,bool Restricted,string Message,string Pci);
internal static class PcfUnlock
{
    internal static bool ShouldAutoEnable(bool hp,uint limitMw,int stockW)=>hp&&limitMw>0&&stockW>0&&limitMw<(uint)stockW*1000;
    internal static PcfDetection Scan()
    {
        var profile=new PowerBackend().GetProfile();
        if(profile==null||profile.StockTgpMaxW<=0)throw new InvalidOperationException("Supported GPU identity is unavailable.");
        string manufacturer="";
        using(var query=new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem"))
            foreach(ManagementObject item in query.Get())manufacturer=Convert.ToString(item["Manufacturer"])??"";
        bool hp=manufacturer.Equals("HP",StringComparison.OrdinalIgnoreCase)||manufacturer.Contains("Hewlett",StringComparison.OrdinalIgnoreCase);
        using var pcf=new PcfPowerController();var values=pcf.GetPowerValues();
        uint original=values.ACMaxGPULimitInMilliwatts;
        if(original==0)throw new InvalidOperationException("PCF GPU cap readback is unavailable.");
        bool restricted=original<(uint)profile.StockTgpMaxW*1000;
        string pci=SystemProbe.FindNvidiaDeviceInstanceId(SystemProbe.GetGpuName());
        uint baseline=original;
        string saved=Path.Combine(ResolverStateStore.DirectoryPath,"pcf-original.txt");
        if(File.Exists(saved)){var lines=File.ReadAllLines(saved);if(lines.Length==2&&lines[0]==pci&&uint.TryParse(lines[1],out uint old)&&old>0&&old<=(uint)profile.StockTgpMaxW*1000)baseline=old;}
        return new(hp,profile.StockTgpMaxW,baseline,original,restricted,$"{manufacturer}: PCF {original/1000.0:0.###} W; stock + Dynamic Boost {profile.StockTgpMaxW} W.",pci);
    }
    internal static string Apply(PcfDetection expected,bool enable)
    {
        var current=Scan();
        if(current.Pci!=expected.Pci||current.StockW!=expected.StockW||current.Hp!=expected.Hp)throw new InvalidOperationException("Device changed. Scan PCF again.");
        uint target=enable?(uint)expected.StockW*1000:expected.OriginalMw;
        if(enable&&current.Restricted){Directory.CreateDirectory(ResolverStateStore.DirectoryPath);File.WriteAllLines(Path.Combine(ResolverStateStore.DirectoryPath,"pcf-original.txt"),new[]{SystemProbe.FindNvidiaDeviceInstanceId(SystemProbe.GetGpuName()),expected.OriginalMw.ToString()});}
        using var pcf=new PcfPowerController();pcf.SetPowerField(PcfPowerFields.ACMaxGPULimit,target);
        if(pcf.GetPowerValues().ACMaxGPULimitInMilliwatts!=target)throw new InvalidOperationException("PCF write did not match readback.");
        return $"PCF Max (HP unlock) {(enable?"enabled":"disabled")}: {target/1000.0:0.###} W verified.";
    }
}
