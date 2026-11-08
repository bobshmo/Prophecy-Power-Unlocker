using System.Runtime.InteropServices;
namespace NvpwrControlBlackwell;
internal static class F7ContributionReadback
{
 internal static uint? Decode(byte[] data)
 {
  if(data.Length<1760||BitConverter.ToUInt32(data,0)!=0x106E0||(BitConverter.ToUInt32(data,4)&4)==0||(BitConverter.ToUInt32(data,8)&4)==0)return null;
  uint count=BitConverter.ToUInt32(data,260);if(count==0||count>32)return null;uint? result=null;bool seen=false;
  for(int i=0;i<count;i++){int offset=264+i*8;if(BitConverter.ToUInt32(data,offset)!=0xF7)continue;if(seen)return null;seen=true;uint value=BitConverter.ToUInt32(data,offset+4);if(value is >0 and <=1000000)result=value;}
  return result;
 }
 internal static uint? Read()
 {
  try{using var nv=new NvApiSession();if(!nv.Open(out _))return null;var get=nv.GetGpuBuffer(0x31B7A4CD);if(get==null)return null;byte[] bytes=new byte[1760];BitConverter.GetBytes(0x106E0u).CopyTo(bytes,0);BitConverter.GetBytes(4u).CopyTo(bytes,4);var mem=Marshal.AllocHGlobal(bytes.Length);
  try{Marshal.Copy(bytes,0,mem,bytes.Length);if(get(nv.Gpu,mem)!=0)return null;Marshal.Copy(mem,bytes,0,bytes.Length);return Decode(bytes);}finally{Marshal.FreeHGlobal(mem);}}
  catch{return null;}
 }
 internal static void Test()
 {
  byte[] Buffer(uint source=0xF7,uint value=140000){var b=new byte[1760];BitConverter.GetBytes(0x106E0u).CopyTo(b,0);BitConverter.GetBytes(4u).CopyTo(b,4);BitConverter.GetBytes(4u).CopyTo(b,8);BitConverter.GetBytes(1u).CopyTo(b,260);BitConverter.GetBytes(source).CopyTo(b,264);BitConverter.GetBytes(value).CopyTo(b,268);return b;}
  if(Decode(Buffer())!=140000||Decode(Buffer(0xFE))!=null||Decode(Buffer(value:uint.MaxValue))!=null)throw new Exception("F7 contribution source/release decoding failed.");
  var wrong=Buffer();wrong[0]=0;if(Decode(wrong)!=null)throw new Exception("Unknown board status version accepted.");
  var duplicate=Buffer();BitConverter.GetBytes(2u).CopyTo(duplicate,260);BitConverter.GetBytes(0xF7u).CopyTo(duplicate,272);BitConverter.GetBytes(115000u).CopyTo(duplicate,276);if(Decode(duplicate)!=null)throw new Exception("Ambiguous F7 source accepted.");
 }
}
