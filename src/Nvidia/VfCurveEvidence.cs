using System.Runtime.InteropServices;
namespace NvpwrControlBlackwell;
internal static class VfCurveEvidence
{
 private static byte[] Query(NvApiSession nv,uint id,uint version,int size,byte[]? mask=null)
 {
  var get=nv.GetGpuBuffer(id)??throw new InvalidOperationException($"GET 0x{id:X} unavailable");byte[] b=new byte[size];BitConverter.GetBytes(version).CopyTo(b,0);mask?.CopyTo(b,4);var mem=Marshal.AllocHGlobal(size);
  try{Marshal.Copy(b,0,mem,size);int rc=get(nv.Gpu,mem);if(rc!=0)throw new InvalidOperationException($"GET 0x{id:X} rc={rc}");Marshal.Copy(mem,b,0,size);return b;}finally{Marshal.FreeHGlobal(mem);}
 }
 internal static object Read()
 {
  using var nv=new NvApiSession();if(!nv.Open(out var error))return new{Error=error};
  var info=Query(nv,0x507B4B59,0x1182C,6188);var mask=info[4..36];var status=Query(nv,0x21537AD4,0x11C28,7208,mask);var control=Query(nv,0x23F1B133,0x12420,9248,mask);
  var points=new List<object>();for(int p=0;p<128;p++){int so=40+p*28,co=p==0?-1:NvApiTuner.CurveControlOffset(p);int type=BitConverter.ToInt32(status,so),voltage=BitConverter.ToInt32(status,so+8),frequency=BitConverter.ToInt32(status,so+4),delta=p==0?0:BitConverter.ToInt32(control,co+20);if(voltage>0)points.Add(new{Index=p,Type=type,VoltageUv=voltage,FrequencyKhz=frequency,ControlDeltaKhz=delta});}
  return new{SettersInvoked=false,ImplSha256=nv.ImplHash,Points=points,ControlSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(control))};
 }
}
