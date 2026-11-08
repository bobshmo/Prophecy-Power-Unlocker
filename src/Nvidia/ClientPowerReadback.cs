using System.Runtime.InteropServices;
namespace NvpwrControlBlackwell;
internal static class ClientPowerReadback
{
 internal static object ReadBoard()
 {
  using var nv=new NvApiSession();if(!nv.Open(out var error))return new{Error=error};
  var results=new List<object>();
  foreach(var spec in new[]{(Id:0x31B7A4CDu,Version:0x106E0u,Size:1760,MaskOffset:4,Mask:4u,Name:"Legacy board status"),(Id:0x67F31384u,Version:0x2BA030u,Size:0x29A030,MaskOffset:0x88,Mask:0u,Name:"Current board information")})
  {
   var get=nv.GetGpuBuffer(spec.Id);if(get==null){results.Add(new{spec.Name,Error="GET unsupported"});continue;}
   byte[] bytes=new byte[spec.Size+4096];BitConverter.GetBytes(spec.Version).CopyTo(bytes,0);BitConverter.GetBytes(spec.Mask).CopyTo(bytes,spec.MaskOffset);
   var mem=Marshal.AllocHGlobal(bytes.Length);
   try{Marshal.Copy(bytes,0,mem,bytes.Length);int rc=get(nv.Gpu,mem);Marshal.Copy(mem,bytes,0,bytes.Length);
    var words=new List<object>();if(rc==0)for(int offset=4;offset<spec.Size;offset+=4){uint value=BitConverter.ToUInt32(bytes,offset);if(value!=0)words.Add(new{Offset=offset,Value=value,Hex=$"0x{value:X}"});}
    results.Add(new{spec.Name,Id=$"0x{spec.Id:X}",spec.Version,ReturnedVersion=BitConverter.ToUInt32(bytes,0),Status=rc,NonzeroWords=words});
   }finally{Marshal.FreeHGlobal(mem);}
  }
  return new{SettersInvoked=false,Results=results};
 }
 internal static object ReadSources()
 {
  using var nv=new NvApiSession();if(!nv.Open(out var error))return new{Error=error};
  var get=nv.GetGpuBuffer(0x33AB0353);if(get==null)return new{Error="Client power GET unsupported"};
  var results=new List<object>();
  foreach(uint source in new uint[]{0xF7,0xF8,0xFE})
  {
   byte[] bytes=new byte[1320];BitConverter.GetBytes(0x10528u).CopyTo(bytes,0);BitConverter.GetBytes(1u).CopyTo(bytes,4);BitConverter.GetBytes(source).CopyTo(bytes,44);
   var mem=Marshal.AllocHGlobal(bytes.Length);
   try{Marshal.Copy(bytes,0,mem,bytes.Length);int rc=get(nv.Gpu,mem);Marshal.Copy(mem,bytes,0,bytes.Length);results.Add(new{Source=$"0x{source:X2}",Status=rc,RequestMw=rc==0?(uint?)BitConverter.ToUInt32(bytes,40):null,Released=rc==0&&BitConverter.ToUInt32(bytes,40)==uint.MaxValue});}
   finally{Marshal.FreeHGlobal(mem);}
  }
  return new{Interface="0x33AB0353",Version="0x10528",SettersInvoked=false,Results=results};
 }
}
