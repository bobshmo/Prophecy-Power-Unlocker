using System.Runtime.InteropServices;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using VictusPowerUnlockGui;
namespace NvpwrControlBlackwell;
internal static class PlatformPolicyEvidence
{
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int BufferRead(IntPtr buffer);
 internal static object Read()
 {
  var discovery=PcfControllerDiscovery.ReadMetadata();
  using var nv=new NvApiSession();if(!nv.Open(out var error))return new{Error=error};var records=new List<object>();
  // Global PCF getters need the same private interface session as discovery.
  // Initializing that interface does not write any power fields.
  PcfApi.InitializeMode1();
  try {
  foreach(var spec in new[]{(Name:"ConfigurationInfo",Id:0x80E9A056u,Version:0x240C8u,Size:0x40C8,Offset:0),(Name:"ControllerInfo",Id:0xB4C5D8BAu,Version:0x1189Cu,Size:0x189C,Offset:4),(Name:"SystemPowerLimitInfo",Id:0xB6A3DA5Bu,Version:0x12388u,Size:0x2388,Offset:4),(Name:"MasterInfo",Id:0x1071E0D3u,Version:0x33208u,Size:0x3208,Offset:4),(Name:"MasterControl",Id:0x2F04D0C1u,Version:0x32308u,Size:0x2308,Offset:4),(Name:"MasterStatus",Id:0xE415C04Eu,Version:0x11108u,Size:0x1108,Offset:4)}){
   var pointer=nv.QI(spec.Id);if(pointer==IntPtr.Zero){records.Add(new{spec.Name,Unsupported=true});continue;}byte[] bytes=new byte[65536];BitConverter.GetBytes(spec.Version).CopyTo(bytes,spec.Offset);if(spec.Name is "MasterStatus" or "MasterControl")BitConverter.GetBytes(discovery.MasterMask??0u).CopyTo(bytes,0);
   if(spec.Name=="MasterControl"){
    // Known GET selectors from the matching master info / original probe.
    bytes[8]=4;bytes[8+0x118]=6;
   }
   var mem=Marshal.AllocHGlobal(bytes.Length);
   try{Marshal.Copy(bytes,0,mem,bytes.Length);int rc=Marshal.GetDelegateForFunctionPointer<BufferRead>(pointer)(mem);Marshal.Copy(mem,bytes,0,bytes.Length);var words=new List<object>();if(rc==0)for(int offset=0;offset<spec.Size;offset+=4){uint v=BitConverter.ToUInt32(bytes,offset);if(v!=0)words.Add(new{Offset=offset,Value=v,Hex=$"0x{v:X}"});}records.Add(new{spec.Name,Status=rc,ReturnedVersion=BitConverter.ToUInt32(bytes,spec.Offset),VersionMatches=BitConverter.ToUInt32(bytes,spec.Offset)==spec.Version,RawHex=Convert.ToHexString(bytes.AsSpan(0,spec.Size)),Words=words});}finally{Marshal.FreeHGlobal(mem);}
  }
  object? controller=null;bool? boost=null;
  if(discovery.SelectedIndex is uint index){
   var candidate=discovery.Candidates.Single(c=>c.Index==index);
   int size=candidate.LayoutVersion==0x10C88?0xC88:candidate.LayoutVersion==0x21640?0x1640:0;
   if(size!=0){var bytes=PcfApi.GetControllerBuffer(candidate.LayoutVersion,size,1u<<(int)index);controller=new{Index=index,Version=candidate.LayoutVersion,ReturnedVersion=BitConverter.ToUInt32(bytes,4),RawHex=Convert.ToHexString(bytes),Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),KnownValues=candidate.Values};}
   boost=PcfApi.GetDynamicBoostStatus();
  }
  // Getter/version from the original local NvApi_ref.cs. Retain raw words:
  // channel normalization is not evidence of a separate absolute wattage cap.
  var topology=ReadGpuBuffer(nv,0xEDCF624Eu,0x10048u,72);
  var voltage=ReadGpuBuffer(nv,0x465F9BCFu,0x1004Cu,76);
  var clockLimits=ReadGpuBuffer(nv,0xE440B867u,0x2030Cu,780);
  var clockDomains=ReadGpuBuffer(nv,0x64B43A6Au,0x10928u,2344);
  // Same supported Pstates20 V2 GET as NvApiTuner.ReadPstates, retaining the
  // full record including absolute/range union fields omitted by tuning telemetry.
  var pstates=ReadGpuBuffer(nv,0x6FF81213u,0x21CF8u,7416);
  var gpu=PhysicalGPU.GetPhysicalGPUs().Single();return new{Utc=DateTimeOffset.UtcNow,SettersInvoked=false,Discovery=discovery,Controller=controller,DynamicBoost=boost,PerformanceDecrease=gpu.PerformanceControl.CurrentPerformanceDecreaseReason.ToString(),ActiveLimit=gpu.PerformanceControl.CurrentActiveLimit.ToString(),VoltageRails=voltage,ClockClientLimits=clockLimits,ClockDomains=clockDomains,Pstates20=pstates,PowerTopology=topology,Records=records};
  } finally { PcfApi.UnloadMode1(); }
 }
 private static object ReadGpuBuffer(NvApiSession nv,uint id,uint version,int size)
 {
  var getter=nv.GetGpuBuffer(id);if(getter==null)return new{Unsupported=true};
  byte[] bytes=new byte[65536];BitConverter.GetBytes(version).CopyTo(bytes,0);
  IntPtr mem=Marshal.AllocHGlobal(bytes.Length);
  try {
   Marshal.Copy(bytes,0,mem,bytes.Length);int rc=getter(nv.Gpu,mem);Marshal.Copy(mem,bytes,0,bytes.Length);
   var words=new List<object>();if(rc==0)for(int offset=0;offset<size;offset+=4){uint value=BitConverter.ToUInt32(bytes,offset);if(value!=0)words.Add(new{Offset=offset,Value=value,Hex=$"0x{value:X}"});}
   return new{Id=id,Version=version,Status=rc,RawHex=Convert.ToHexString(bytes.AsSpan(0,size)),Words=words};
  } finally {Marshal.FreeHGlobal(mem);}
 }
}
