using System.Management;
using NvAPIWrapper.GPU;
using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal sealed record PlatformPowerScan(string Identity,uint CurrentMw,int? StockW,int? MaximumW,PcfPowerFields Field){internal bool Adjustable=>StockW.HasValue&&MaximumW.HasValue;}
internal static class TppControl{
 internal static bool ValidBounds(int stock,int max)=>stock>0&&max>=stock&&max<=1000;
 internal static PlatformPowerScan Scan(PcfPowerFields field){
  if(field!=PcfPowerFields.ACTargetTPPLimit&&field!=PcfPowerFields.ACMaxGPULimit)throw new ArgumentException("Unsupported platform field.");
  string machine="";using(var q=new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))foreach(ManagementObject x in q.Get())machine=Convert.ToString(x["Manufacturer"])+" "+Convert.ToString(x["Model"]);
  string gpu=SystemProbe.GetGpuName(),pci=SystemProbe.FindNvidiaDeviceInstanceId(gpu);using var pcf=new PcfPowerController();var v=pcf.GetPowerValues();uint current=field==PcfPowerFields.ACTargetTPPLimit?v.ACTargetTPPLimitInMilliwatts:v.ACMaxGPULimitInMilliwatts;int? stock=null,max=null;
  using(var q=new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))foreach(ManagementObject x in q.Get())machine+=" | BIOS "+Convert.ToString(x["SMBIOSBIOSVersion"]);
  machine+=" | NVIDIA "+SystemProbe.GetDriverVersion()+" | PCF "+pcf.ControllerIndex+"/"+pcf.LayoutVersion;
  machine+=" | VBIOS "+SystemProbe.GetVbios();
  try{using var q=new ManagementObjectSearcher(@"root\HP\InstrumentedBIOS","SELECT * FROM HP_BIOSInteger");foreach(ManagementObject x in q.Get()){
   string name=Convert.ToString(x["Name"])??"";if(!(field==PcfPowerFields.ACTargetTPPLimit?(name.Contains("TPP",StringComparison.OrdinalIgnoreCase)||name.Contains("Total Platform Power",StringComparison.OrdinalIgnoreCase)):(name.Contains("ACMaxGPULimit",StringComparison.OrdinalIgnoreCase)||name.Contains("GPU Max Power",StringComparison.OrdinalIgnoreCase))))continue;
   object? Read(string key)=>x.Properties.Cast<PropertyData>().FirstOrDefault(p=>p.Name.Equals(key,StringComparison.OrdinalIgnoreCase))?.Value;
   string unit=Convert.ToString(Read("Units"))??"";if(!(unit.Equals("W",StringComparison.OrdinalIgnoreCase)||unit.Equals("Watts",StringComparison.OrdinalIgnoreCase)||name.Contains("(W)",StringComparison.OrdinalIgnoreCase)))continue;
   if(int.TryParse(Convert.ToString(Read("DefaultValue")??Read("FactoryDefault")??Read("Default")),out int low)&&int.TryParse(Convert.ToString(Read("UpperBound")),out int high)&&ValidBounds(low,high)){stock=low;max=high;break;}
  }}catch(ManagementException){}
  return new(machine+" | "+gpu+" | "+pci,current,stock,max,field);
 }
 internal static string Apply(PlatformPowerScan expected,int watts){var fresh=Scan(expected.Field);if(fresh.Identity!=expected.Identity||!fresh.Adjustable)throw new InvalidOperationException("Device/range unavailable. Scan again.");if(watts<fresh.StockW||watts>fresh.MaximumW)throw new ArgumentOutOfRangeException(nameof(watts));using var pcf=new PcfPowerController();pcf.SetPowerField(expected.Field,(uint)watts*1000);var v=pcf.GetPowerValues();uint actual=expected.Field==PcfPowerFields.ACTargetTPPLimit?v.ACTargetTPPLimitInMilliwatts:v.ACMaxGPULimitInMilliwatts;if(actual!=(uint)watts*1000)throw new InvalidOperationException("PCF readback mismatch.");return expected.Field+": "+watts+" W verified.";}
}
