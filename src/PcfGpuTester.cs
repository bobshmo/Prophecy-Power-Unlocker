using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal sealed record PcfGpuTestResult(string Identity,uint TppMw,int Ceiling,bool FirmwareMaximum,int[] Accepted,int[] Rejected,bool RestoreVerified)
{
 internal string Describe()=> $"Tested every value 0–{Ceiling} W: {Accepted.Length} matched / {Rejected.Length} rejected or clamped. "+
  (Accepted.Any(w=>w>0)?$"Highest matching positive target: {Accepted.Max()} W. ":"No positive target matched. ")+
  (FirmwareMaximum?"Firmware-advertised maximum used. ":"Maximum not determined; the GPU profile's test ceiling is not a detected PCF maximum. ")+
  "Original PCF snapshot restored. Zero is a controller command, not a proven 0 W cap. Readback does not prove safe or sustained operating power.";
}
internal static class PcfGpuTester
{
 internal static PcfGpuTestResult Run(ITppProbe probe,string identity,uint tpp,int ceiling,bool firmwareMaximum,Action checkIdle,CancellationToken cancel,Action<int>? progress=null,Action? settle=null)
 {
  if(ceiling<1||ceiling>1000)throw new ArgumentOutOfRangeException(nameof(ceiling));
  checkIdle();uint original=probe.Read();if(original==uint.MaxValue)throw new InvalidOperationException("Original PCF value is released; cannot verify restoration.");
  var yes=new List<int>();var no=new List<int>();
  try{for(int w=0;w<=ceiling;w++){cancel.ThrowIfCancellationRequested();checkIdle();
   try{probe.Write((uint)w*1000);settle?.Invoke();if(probe.Read()==(uint)w*1000)yes.Add(w);else no.Add(w);}
   catch(Exception ex)when(ex is InvalidOperationException or NvAPIWrapper.Native.Exceptions.NVIDIAApiException){no.Add(w);}
   progress?.Invoke(w);
  }}finally{probe.Restore();settle?.Invoke();if(probe.Read()!=original)throw new InvalidOperationException("PCF restoration failed. Read live limits before other changes.");}
  return new(identity,tpp,ceiling,firmwareMaximum,yes.ToArray(),no.ToArray(),true);
 }
 internal static PcfGpuTestResult Test(PlatformPowerScan expected,CancellationToken cancel,Action<int>? progress)
 {
  var fresh=TppControl.Scan(PcfPowerFields.ACMaxGPULimit);if(fresh.Identity!=expected.Identity)throw new InvalidOperationException("Device changed; scan again.");
  var profile=new PowerBackend().GetProfile()??throw new InvalidOperationException("Unsupported GPU.");
  int ceiling=fresh.MaximumW??profile.MaxPowerW;
  using var read=new PcfPowerController();uint tpp=read.GetPowerValues().ACTargetTPPLimitInMilliwatts;
  using var probe=new PcfTppProbe(PcfPowerFields.ACMaxGPULimit);
  var result=Run(probe,fresh.Identity,tpp,ceiling,fresh.MaximumW.HasValue,TppCalibration.CheckIdle,cancel,progress,()=>Thread.Sleep(200));
  Directory.CreateDirectory(ResolverStateStore.DirectoryPath);File.WriteAllText(PathFor(fresh.Identity),System.Text.Json.JsonSerializer.Serialize(result));return result;
 }
 internal static string PathFor(string identity)=>Path.Combine(ResolverStateStore.DirectoryPath,"pcf-gpu-test-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))+".json");
 internal static bool Valid(PcfGpuTestResult r,string identity,uint tpp)=>r.Identity==identity&&r.TppMw==tpp&&r.RestoreVerified&&r.Ceiling is >0 and <=1000&&r.Accepted!=null&&r.Rejected!=null&&r.Accepted.Concat(r.Rejected).Order().SequenceEqual(Enumerable.Range(0,r.Ceiling+1));
 internal static PcfGpuTestResult? Load(PlatformPowerScan fresh){try{using var p=new PcfPowerController();var r=System.Text.Json.JsonSerializer.Deserialize<PcfGpuTestResult>(File.ReadAllText(PathFor(fresh.Identity)));return r!=null&&Valid(r,fresh.Identity,p.GetPowerValues().ACTargetTPPLimitInMilliwatts)?r:null;}catch(Exception ex)when(ex is IOException or System.Text.Json.JsonException){return null;}}
 internal static string Apply(PcfGpuTestResult r,int watts){var fresh=TppControl.Scan(PcfPowerFields.ACMaxGPULimit);using var p=new PcfPowerController();if(!Valid(r,fresh.Identity,p.GetPowerValues().ACTargetTPPLimitInMilliwatts)||watts<=0||!r.Accepted.Contains(watts))throw new InvalidOperationException("Positive target/device/TPP does not match the tested result. Retest after TPP changes.");TppCalibration.CheckIdle();p.SetPowerField(PcfPowerFields.ACMaxGPULimit,(uint)watts*1000);if(p.GetPowerValues().ACMaxGPULimitInMilliwatts!=(uint)watts*1000)throw new InvalidOperationException("PCF target readback mismatch.");return $"ACMaxGPULimit {watts} W verified.";}
}
