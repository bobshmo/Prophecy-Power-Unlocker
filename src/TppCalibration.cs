using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal interface ITppProbe : IDisposable {uint Read();void Write(uint value);void Restore();}
internal sealed class PcfTppProbe : ITppProbe
{
    private readonly IPlatformPowerSession _session;
    private readonly PlatformPowerSnapshot _original;
    private readonly IDisposable? _prepared;
    private readonly PcfPowerFields _field;
    internal PlatformPowerSnapshot Original=>_original;
    public PcfTppProbe(PcfPowerFields field=PcfPowerFields.ACTargetTPPLimit):this(new NativePlatformPowerSession(),field){}
    internal PcfTppProbe(IPlatformPowerSession session,PcfPowerFields field=PcfPowerFields.ACTargetTPPLimit)
    {
        _session=session;_field=field;PlatformPowerSnapshot? captured=null;
        try
        {
            if(field!=PcfPowerFields.ACTargetTPPLimit&&field!=PcfPowerFields.ACMaxGPULimit)throw new ArgumentOutOfRangeException(nameof(field));
            if(_session.CurrentIsRequested&&!_session.CanRestoreCurrent)throw new InvalidOperationException("Validate the NVIDIA driver before scanning: CURRENT restoration must be available.");
            _original=captured=PlatformPowerTransaction.Capture(_session);
            // Calibration must retain the active HP mode. Changing it first can
            // turn its different OEM MIN/DEFAULT values into persistent overrides.
            _prepared=HpPlatform.EnterPrepared(()=>{});Restore();
        }
        catch(Exception failure)
        {
            try
            {
                if(captured!=null)PlatformPowerTransaction.Restore(_session,captured);
            }
            catch(Exception restoration){throw new InvalidOperationException($"Scanner preparation failed ({failure.Message}) and full restoration failed: {restoration.Message}",failure);}
            finally{try{_prepared?.Dispose();}finally{_session.Dispose();}}
            throw;
        }
    }
    public uint Read(){var v=_session.ReadLimits();return _field==PcfPowerFields.ACMaxGPULimit?v.ACMaxGPULimitInMilliwatts:v.ACTargetTPPLimitInMilliwatts;}
    public void Write(uint value)=>_session.WriteLimits(PlatformPowerTransaction.ProbeValues(_original.Limits,_field,value));
    public void Restore()=>PlatformPowerTransaction.Restore(_session,_original,releaseProbeOverrides:true);
    public void Dispose(){try{_prepared?.Dispose();}finally{_session.Dispose();}}
}
internal sealed record TppCalibrationResult(string Identity,int Lower,int Upper,int[] Accepted,int[] Rejected,bool RestoreVerified,int Protocol=0,string Context="")
{
    internal string Describe()=>Accepted.Length==0?"No tested target matched readback; temporary PCF overrides released and original power values restored.":$"{Accepted.Length} accepted / {Rejected.Length} rejected targets; accepted span {Accepted.Min()}–{Accepted.Max()} W within the {Lower}–{Upper} W test interval. "+(Accepted.Contains(Upper)?"Upper boundary not found; this is not a detected hardware maximum. ":"Targets above the highest accepted value were rejected or mismatched. ")+"Temporary overrides released, then original PCF values and Dynamic Boost restored; NVIDIA MAX checked, public CURRENT observed after every target. Stock value and loaded power are not inferred from readback.";
}
internal static class TppCalibration
{
    internal static bool CpuLoadAllowsCalibration(double load)=>double.IsFinite(load)&&load>=0&&load<25;
    internal static TppCalibrationResult Run(ITppProbe probe,string identity,int lower,int upper,Action checkIdle,CancellationToken cancel,Action<int>? progress=null,Action? settle=null,string context="test")
    {
        if(lower<1||upper<lower||upper>1000||upper-lower>300)throw new ArgumentOutOfRangeException(nameof(upper),"Choose a positive test interval of at most 300 W, within 1–1000 W.");
        checkIdle();uint original=probe.Read();if(original==0||original==uint.MaxValue)throw new InvalidOperationException("Original TPP readback is unavailable/released; no targets tested.");
        var accepted=new List<int>();var rejected=new List<int>();
        try{for(int watts=lower;watts<=upper;watts++){
            cancel.ThrowIfCancellationRequested();checkIdle();
            try{try{probe.Write((uint)watts*1000);settle?.Invoke();if(probe.Read()==(uint)watts*1000)accepted.Add(watts);else rejected.Add(watts);}
            catch(Exception ex) when(ex is InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){rejected.Add(watts);}}
            finally{probe.Restore();if(probe.Read()!=original)throw new InvalidOperationException("Original power state was not restored after a probe; scan stopped.");}
            progress?.Invoke(watts);
        }}finally{
            probe.Restore();settle?.Invoke();
            if(probe.Read()!=original)throw new InvalidOperationException("TPP scan stopped, but original TPP restoration could not be verified. Read Compatibility before applying other settings.");
        }
        return new(identity,lower,upper,accepted.ToArray(),rejected.ToArray(),true,3,context);
    }
    internal static void CheckIdle()
    {
        if(System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus!=System.Windows.Forms.PowerLineStatus.Online)throw new InvalidOperationException("Connect AC power before TPP calibration.");
        var telemetry=SystemProbe.GetTelemetry();
        if(!telemetry.UtilizationPct.HasValue||telemetry.UtilizationPct>10)throw new InvalidOperationException("Close GPU workloads before TPP calibration (GPU must be below 10% load).");
        if(!telemetry.GpuTempC.HasValue||telemetry.GpuTempC>65)throw new InvalidOperationException("GPU temperature must be readable and below 65°C before calibration.");
        double cpuLoad=CpuLoadReader.Read();
        if(!CpuLoadAllowsCalibration(cpuLoad))throw new InvalidOperationException($"Close CPU workloads before TPP calibration (current load {cpuLoad:0.#}%; CPU must be below 25%).");
    }
    internal static TppCalibrationResult Calibrate(PlatformPowerScan device,int lower,int upper,CancellationToken cancel,Action<int>? progress)
    {
        var fresh=TppControl.Scan(PcfPowerFields.ACTargetTPPLimit);if(fresh.Identity!=device.Identity)throw new InvalidOperationException("Laptop/GPU changed. Scan again.");
        lock(PlatformPowerTransaction.Sync){CheckIdle();using var probe=new PcfTppProbe();
        var result=Run(probe,fresh.Identity,lower,upper,CheckIdle,cancel,progress,()=>Thread.Sleep(200),probe.Original.ContextFor(PcfPowerFields.ACTargetTPPLimit));
        Directory.CreateDirectory(ResolverStateStore.DirectoryPath);
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fresh.Identity)));
        File.WriteAllText(Path.Combine(ResolverStateStore.DirectoryPath,"tpp-calibration-"+key+".json"),System.Text.Json.JsonSerializer.Serialize(result));
        return result;}
    }
    internal static string Apply(TppCalibrationResult tested,int watts)
    {
        lock(PlatformPowerTransaction.Sync)
        {
            CheckIdle();var fresh=TppControl.Scan(PcfPowerFields.ACTargetTPPLimit);
            using var s=new NativePlatformPowerSession();var original=PlatformPowerTransaction.Capture(s);
            if(!Valid(tested,fresh.Identity,original.ContextFor(PcfPowerFields.ACTargetTPPLimit))||!tested.Accepted.Contains(watts))throw new InvalidOperationException("Target/device/power context does not match the tested TPP result. Scan again after driver recovery or policy changes.");
            return PlatformPowerTransaction.ApplyDesired(s,original,original.Limits.With(PcfPowerFields.ACTargetTPPLimit,(uint)watts*1000),false,$"ACTargetTPPLimit {watts} W");
        }
    }
    internal static bool Valid(TppCalibrationResult result,string identity,string context="test")=>result.Protocol==3&&result.Context==context&&result.Identity==identity&&result.RestoreVerified&&result.Lower>0&&result.Upper<=1000&&result.Upper>=result.Lower&&result.Upper-result.Lower<=300&&result.Accepted!=null&&result.Rejected!=null&&result.Accepted.Concat(result.Rejected).Order().SequenceEqual(Enumerable.Range(result.Lower,result.Upper-result.Lower+1));
    internal static TppCalibrationResult? Load(PlatformPowerScan device)
    {
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(device.Identity)));
        string file=Path.Combine(ResolverStateStore.DirectoryPath,"tpp-calibration-"+key+".json");
        if(!File.Exists(file))return null;
        try{lock(PlatformPowerTransaction.Sync){var result=System.Text.Json.JsonSerializer.Deserialize<TppCalibrationResult>(File.ReadAllText(file));using var s=new NativePlatformPowerSession();return result!=null&&Valid(result,device.Identity,PlatformPowerTransaction.Capture(s).ContextFor(PcfPowerFields.ACTargetTPPLimit))?result:null;}}
        catch(Exception ex) when(ex is System.Text.Json.JsonException or IOException or InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){return null;}
    }
}
