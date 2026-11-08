using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal sealed record PcfGpuTestResult(string Identity,uint TppMw,int Ceiling,bool FirmwareMaximum,int[] Accepted,int[] Rejected,bool RestoreVerified,int Protocol=0,string Context="",int[]? Skipped=null,int Minimum=1)
{
 internal string Describe()=> $"Range {Minimum}–{Ceiling} W: {Accepted.Length} matched / {Rejected.Length} rejected or clamped. Targets below the reported minimum are not tested. "+
  (Accepted.Any(w=>w>0)?$"Highest matching positive target: {Accepted.Max()} W. ":"No positive target matched. ")+
  (FirmwareMaximum?"Firmware-advertised maximum used. ":"Configured test maximum is not a detected PCF maximum. ")+
  "Temporary PCF overrides released and original PCF values and Dynamic Boost restored; NVIDIA MAX checked, public CURRENT observed after each probe. Readback does not prove safe or sustained operating power.";
}
internal static class PcfGpuTester
{
 internal static PcfGpuTestResult Run(ITppProbe probe,string identity,uint tpp,int ceiling,bool firmwareMaximum,Action checkIdle,CancellationToken cancel,Action<int>? progress=null,Action? settle=null,string context="test",int minimum=1)
 {
  if(minimum<1||ceiling<minimum||ceiling>1000)throw new ArgumentOutOfRangeException(nameof(ceiling));
  checkIdle();uint original=probe.Read();if(original==0||original==uint.MaxValue)throw new InvalidOperationException("Original PCF value is unavailable/released; cannot verify restoration.");
  var yes=new List<int>();var no=new List<int>();var skipped=new List<int>();
  try{for(int w=minimum;w<=ceiling;w++){cancel.ThrowIfCancellationRequested();checkIdle();
   try{try{probe.Write((uint)w*1000);settle?.Invoke();if(probe.Read()==(uint)w*1000)yes.Add(w);else no.Add(w);}
   catch(Exception ex)when(ex is InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){no.Add(w);}}
   finally{probe.Restore();if(probe.Read()!=original)throw new InvalidOperationException("Original power state did not restore after a probe; scan stopped.");}
   progress?.Invoke(w);
  }}finally{probe.Restore();settle?.Invoke();if(probe.Read()!=original)throw new InvalidOperationException("PCF restoration failed. Read live limits before other changes.");}
  return new(identity,tpp,ceiling,firmwareMaximum,yes.ToArray(),no.ToArray(),true,4,context,skipped.ToArray(),minimum);
 }
 internal static PcfGpuTestResult Test(PlatformPowerScan expected,CancellationToken cancel,Action<int>? progress,int configuredMaximum)
 {
  var fresh=TppControl.Scan(PcfPowerFields.ACMaxGPULimit);if(fresh.Identity!=expected.Identity)throw new InvalidOperationException("Device changed; scan again.");
  int ceiling=configuredMaximum;
  if(ceiling is <1 or >1000)throw new ArgumentOutOfRangeException(nameof(configuredMaximum));
  if(fresh.MaximumW.HasValue&&ceiling>fresh.MaximumW.Value)throw new InvalidOperationException("Test maximum exceeds the firmware-advertised maximum.");
  lock(PlatformPowerTransaction.Sync){TppCalibration.CheckIdle();using var probe=new PcfTppProbe(PcfPowerFields.ACMaxGPULimit);uint tpp=probe.Original.Limits.ACTargetTPPLimitInMilliwatts;
  int minimum=MinimumWholeWatts(probe.Original.Limits.ACMinGPULimitInMilliwatts);
  var result=Run(probe,fresh.Identity,tpp,ceiling,fresh.MaximumW==ceiling,TppCalibration.CheckIdle,cancel,progress,()=>Thread.Sleep(200),probe.Original.Context,minimum);
  Directory.CreateDirectory(ResolverStateStore.DirectoryPath);File.WriteAllText(PathFor(fresh.Identity),System.Text.Json.JsonSerializer.Serialize(result));return result;}
 }
 internal static int MinimumWholeWatts(uint minimumMw)
 {
  if(!PcfUnlock.IsFiniteCap(minimumMw)||minimumMw>1000000)throw new InvalidOperationException("Reported PCF GPU minimum is unavailable; no targets tested.");
  return checked((int)((minimumMw+999)/1000));
 }
 internal static string PathFor(string identity)=>Path.Combine(ResolverStateStore.DirectoryPath,"pcf-gpu-test-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))+".json");
 internal static bool Valid(PcfGpuTestResult r,string identity,uint tpp,string context="test")=>r.Protocol==4&&r.Context==context&&r.Identity==identity&&r.TppMw==tpp&&r.RestoreVerified&&r.Minimum is >0 and <=1000&&r.Ceiling>=r.Minimum&&r.Ceiling<=1000&&r.Accepted!=null&&r.Rejected!=null&&r.Skipped!=null&&r.Skipped.Length==0&&!r.Accepted.Contains(0)&&r.Accepted.Concat(r.Rejected).Concat(r.Skipped).Order().SequenceEqual(Enumerable.Range(r.Minimum,r.Ceiling-r.Minimum+1));
 internal static PcfGpuTestResult? Load(PlatformPowerScan fresh)
 {
  try{lock(PlatformPowerTransaction.Sync){if(!File.Exists(PathFor(fresh.Identity)))return null;using var s=new NativePlatformPowerSession();var snapshot=PlatformPowerTransaction.Capture(s);var r=System.Text.Json.JsonSerializer.Deserialize<PcfGpuTestResult>(File.ReadAllText(PathFor(fresh.Identity)));return r!=null&&Valid(r,fresh.Identity,snapshot.Limits.ACTargetTPPLimitInMilliwatts,snapshot.Context)?r:null;}}
  catch(Exception ex)when(ex is IOException or System.Text.Json.JsonException or InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){return null;}
 }
 internal static string Apply(PcfGpuTestResult r,int watts)
 {
  lock(PlatformPowerTransaction.Sync)
  {
   TppCalibration.CheckIdle();var fresh=TppControl.Scan(PcfPowerFields.ACMaxGPULimit);using var s=new NativePlatformPowerSession();var snapshot=PlatformPowerTransaction.Capture(s);
   if(!Valid(r,fresh.Identity,snapshot.Limits.ACTargetTPPLimitInMilliwatts,snapshot.Context)||watts<=0||!r.Accepted.Contains(watts))throw new InvalidOperationException("Target/device/power context does not match the tested result. Retest after driver recovery, TPP or policy changes.");
   var expected=PlatformPowerTransaction.ProbeValues(snapshot.Limits,PcfPowerFields.ACMaxGPULimit,(uint)watts*1000);
   return PlatformPowerTransaction.ApplyDesired(s,snapshot,expected,false,$"ACMaxGPULimit {watts} W");
  }
 }
}
