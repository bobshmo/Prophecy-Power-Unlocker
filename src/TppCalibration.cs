using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal interface ITppProbe : IDisposable {uint Read();void Write(uint value);void Restore();}
internal sealed class PcfTppProbe : ITppProbe
{
    private readonly PcfPowerController _controller=new();
    private readonly PcfPowerValues _original;
    public PcfTppProbe(){_original=_controller.GetPowerValues();}
    public uint Read()=>_controller.GetPowerValues().ACTargetTPPLimitInMilliwatts;
    public void Write(uint value)=>_controller.SetPowerField(PcfPowerFields.ACTargetTPPLimit,value);
    public void Restore(){_controller.RestoreStartupSnapshot();var actual=_controller.GetPowerValues();if(actual.ACTargetTPPLimitInMilliwatts!=_original.ACTargetTPPLimitInMilliwatts||actual.ACDefaultGPULimitInMilliwatts!=_original.ACDefaultGPULimitInMilliwatts||actual.ACMinGPULimitInMilliwatts!=_original.ACMinGPULimitInMilliwatts||actual.ACMaxGPULimitInMilliwatts!=_original.ACMaxGPULimitInMilliwatts)throw new InvalidOperationException("Original PCF snapshot restoration did not match readback.");}
    public void Dispose()=>_controller.Dispose();
}
internal sealed record TppCalibrationResult(string Identity,int Lower,int Upper,int[] Accepted,int[] Rejected,bool RestoreVerified)
{
    internal string Describe()=>Accepted.Length==0?"No tested target matched readback; original TPP restored.":$"{Accepted.Length} accepted / {Rejected.Length} rejected targets; accepted span {Accepted.Min()}–{Accepted.Max()} W within the {Lower}–{Upper} W test interval. "+(Accepted.Contains(Upper)?"Upper boundary not found; this is not a detected hardware maximum. ":"Targets above the highest accepted value were rejected or mismatched. ")+"Original TPP restored. Stock value is not inferred from these results.";
}
internal static class TppCalibration
{
    internal static TppCalibrationResult Run(ITppProbe probe,string identity,int lower,int upper,Action checkIdle,CancellationToken cancel,Action<int>? progress=null,Action? settle=null)
    {
        if(lower<1||upper<lower||upper>1000||upper-lower>300)throw new ArgumentOutOfRangeException(nameof(upper),"Choose a positive test interval of at most 300 W, within 1–1000 W.");
        checkIdle();uint original=probe.Read();if(original==0||original==uint.MaxValue)throw new InvalidOperationException("Original TPP readback is unavailable/released; no targets tested.");
        var accepted=new List<int>();var rejected=new List<int>();
        try{for(int watts=lower;watts<=upper;watts++){
            cancel.ThrowIfCancellationRequested();checkIdle();
            try{probe.Write((uint)watts*1000);settle?.Invoke();if(probe.Read()==(uint)watts*1000)accepted.Add(watts);else rejected.Add(watts);}
            catch(Exception ex) when(ex is InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){rejected.Add(watts);}
            progress?.Invoke(watts);
        }}finally{
            probe.Restore();settle?.Invoke();
            if(probe.Read()!=original)throw new InvalidOperationException("TPP scan stopped, but original TPP restoration could not be verified. Read Compatibility before applying other settings.");
        }
        return new(identity,lower,upper,accepted.ToArray(),rejected.ToArray(),true);
    }
    internal static void CheckIdle()
    {
        if(System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus!=System.Windows.Forms.PowerLineStatus.Online)throw new InvalidOperationException("Connect AC power before TPP calibration.");
        var telemetry=SystemProbe.GetTelemetry();
        if(!telemetry.UtilizationPct.HasValue||telemetry.UtilizationPct>10)throw new InvalidOperationException("Close GPU workloads before TPP calibration (GPU must be below 10% load).");
        if(!telemetry.GpuTempC.HasValue||telemetry.GpuTempC>65)throw new InvalidOperationException("GPU temperature must be readable and below 65°C before calibration.");
        bool cpuRead=false;
        using var query=new System.Management.ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
        foreach(System.Management.ManagementObject item in query.Get()){if(item["LoadPercentage"]==null)continue;cpuRead=true;if(Convert.ToInt32(item["LoadPercentage"])>15)throw new InvalidOperationException("Close CPU workloads before TPP calibration (CPU must be below 15% load).");}
        if(!cpuRead)throw new InvalidOperationException("CPU load is unavailable; calibration was not started.");
    }
    internal static TppCalibrationResult Calibrate(PlatformPowerScan device,int lower,int upper,CancellationToken cancel,Action<int>? progress)
    {
        var fresh=TppControl.Scan(PcfPowerFields.ACTargetTPPLimit);if(fresh.Identity!=device.Identity)throw new InvalidOperationException("Laptop/GPU changed. Scan again.");
        using var probe=new PcfTppProbe();
        var result=Run(probe,fresh.Identity,lower,upper,CheckIdle,cancel,progress,()=>Thread.Sleep(200));
        Directory.CreateDirectory(ResolverStateStore.DirectoryPath);
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fresh.Identity)));
        File.WriteAllText(Path.Combine(ResolverStateStore.DirectoryPath,"tpp-calibration-"+key+".json"),System.Text.Json.JsonSerializer.Serialize(result));
        return result;
    }
    internal static string Apply(TppCalibrationResult tested,int watts)
    {
        var fresh=TppControl.Scan(PcfPowerFields.ACTargetTPPLimit);
        if(fresh.Identity!=tested.Identity||!tested.RestoreVerified||!tested.Accepted.Contains(watts))throw new InvalidOperationException("Target/device does not match the tested TPP result.");
        using var pcf=new PcfPowerController();pcf.SetPowerField(PcfPowerFields.ACTargetTPPLimit,(uint)watts*1000);
        if(pcf.GetPowerValues().ACTargetTPPLimitInMilliwatts!=(uint)watts*1000)throw new InvalidOperationException("Applied TPP did not match readback.");
        return $"ACTargetTPPLimit {watts} W matched controller readback (calibrated target; not a hardware-safety guarantee).";
    }
    internal static TppCalibrationResult? Load(PlatformPowerScan device)
    {
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(device.Identity)));
        string file=Path.Combine(ResolverStateStore.DirectoryPath,"tpp-calibration-"+key+".json");
        if(!File.Exists(file))return null;
        try{var result=System.Text.Json.JsonSerializer.Deserialize<TppCalibrationResult>(File.ReadAllText(file));return result!=null&&result.Identity==device.Identity&&result.RestoreVerified&&result.Lower>0&&result.Upper<=1000&&result.Upper>=result.Lower&&result.Upper-result.Lower<=300&&result.Accepted!=null&&result.Rejected!=null&&result.Accepted.All(w=>w>=result.Lower&&w<=result.Upper)?result:null;}
        catch(Exception ex) when(ex is System.Text.Json.JsonException or IOException){return null;}
    }
}
