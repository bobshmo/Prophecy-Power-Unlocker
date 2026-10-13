using System.Management;
using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal sealed record PcfDetection(bool Hp,int StockW,uint OriginalMw,uint CurrentMw,bool Restricted,string Message,string Pci);
internal static class PcfUnlock
{
    internal static int SelectStockTarget(int modelStock,int? resolvedStock)=>resolvedStock is >0 ? Math.Min(modelStock,resolvedStock.Value) : modelStock;
    internal static bool ShouldAutoEnable(bool hp,uint limitMw,int stockW)=>hp&&limitMw>0&&stockW>0&&limitMw<(uint)stockW*1000;
    internal static PcfDetection Scan()
    {
        var compatibility=new PowerBackend().CheckCompatibility();
        var profile=compatibility.Profile;
        if(profile==null||profile.StockTgpMaxW<=0)throw new InvalidOperationException("Supported GPU identity is unavailable.");
        string manufacturer="";
        using(var query=new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem"))
            foreach(ManagementObject item in query.Get())manufacturer=Convert.ToString(item["Manufacturer"])??"";
        bool hp=manufacturer.Equals("HP",StringComparison.OrdinalIgnoreCase)||manufacturer.Contains("Hewlett",StringComparison.OrdinalIgnoreCase);
        using var pcf=new PcfPowerController();var values=pcf.GetPowerValues();
        uint original=values.ACMaxGPULimitInMilliwatts;
        if(original==0)throw new InvalidOperationException("PCF GPU cap readback is unavailable.");
        int stock=SelectStockTarget(profile.StockTgpMaxW,compatibility.VbiosResolver?.Resolved==true?compatibility.VbiosResolver.StockMaxW:null);
        bool restricted=original<(uint)stock*1000;
        string pci=SystemProbe.FindNvidiaDeviceInstanceId(SystemProbe.GetGpuName());
        uint baseline=original;
        string saved=Path.Combine(ResolverStateStore.DirectoryPath,"pcf-original.txt");
        if(File.Exists(saved)){var lines=File.ReadAllLines(saved);if(lines.Length==2&&lines[0]==pci&&uint.TryParse(lines[1],out uint old)&&old>0&&old<=(uint)stock*1000)baseline=old;}
        return new(hp,stock,baseline,original,restricted,$"{manufacturer}: ACMaxGPULimit {original/1000.0:0.###} W; baseline target {stock} W ({(compatibility.VbiosResolver?.Resolved==true?"resolved VBIOS":"GPU model fallback")}). This is not a scanned PCF maximum.",pci);
    }
    internal static string Apply(PcfDetection expected,bool enable)
    {
        var current=Scan();
        if(current.Pci!=expected.Pci||current.StockW!=expected.StockW||current.Hp!=expected.Hp)throw new InvalidOperationException("Device changed. Scan PCF again.");
        uint target=enable?(uint)expected.StockW*1000:expected.OriginalMw;
        TppCalibration.CheckIdle();
        if(enable&&current.Hp){using var hp=new OmenCore.Hardware.HpWmiBios();bool preserveMax=hp.GetFanMax()==true;var mode=HpWmiMode.Scan(expected.StockW);HpWmiMode.Apply(mode,true);if(preserveMax&&(!hp.SetFanMax(true)||hp.GetFanMax()!=true))throw new InvalidOperationException("HP performance mode did not preserve Fan Max; PCF not changed.");}
        if(enable&&current.Restricted){Directory.CreateDirectory(ResolverStateStore.DirectoryPath);File.WriteAllLines(Path.Combine(ResolverStateStore.DirectoryPath,"pcf-original.txt"),new[]{SystemProbe.FindNvidiaDeviceInstanceId(SystemProbe.GetGpuName()),expected.OriginalMw.ToString()});}
        using var pcf=new PcfPowerController();
        try{pcf.SetPowerField(PcfPowerFields.ACMaxGPULimit,target);if(pcf.GetPowerValues().ACMaxGPULimitInMilliwatts!=target)throw new InvalidOperationException("PCF write did not match readback.");}
        catch{pcf.RestoreStartupSnapshot();if(pcf.GetPowerValues().ACMaxGPULimitInMilliwatts!=current.CurrentMw)throw new InvalidOperationException("PCF write failed and original GPU cap restoration did not match readback.");throw;}
        return $"PCF Max (HP unlock) {(enable?"enabled":"disabled")}: {target/1000.0:0.###} W verified.";
    }
}
