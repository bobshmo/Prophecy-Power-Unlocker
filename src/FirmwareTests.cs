using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal static class FirmwareTests
{
    internal static void Run()
    {
        if(HpWmiMode.ModeForPolicy(0,true)!=OmenCore.Hardware.HpWmiBios.FanMode.LegacyPerformance || HpWmiMode.ModeForPolicy(1,true)!=OmenCore.Hardware.HpWmiBios.FanMode.Performance || HpWmiMode.ModeForPolicy(0,false)!=OmenCore.Hardware.HpWmiBios.FanMode.LegacyDefault)throw new Exception("WMI thermal policy mapping failed.");
        if(!PcfUnlock.ShouldAutoEnable(true,80000,115)||PcfUnlock.ShouldAutoEnable(false,80000,115)||PcfUnlock.ShouldAutoEnable(true,115000,115)||PcfUnlock.ShouldAutoEnable(true,0,115))throw new Exception("PCF detection policy failed.");
        using(var slider=new PowerSlider()){slider.SetRange(1,140,1);slider.Value=73;if(slider.Value!=73)throw new Exception("Custom CURRENT value rounded.");}
        var options=SystemProbe.ProcessOptions("nvflashb.exe","--save backup.rom",false);
        if(options.CreateNoWindow || options.WindowStyle!=System.Diagnostics.ProcessWindowStyle.Hidden || options.UseShellExecute)
            throw new Exception("NVFlash console allocation was disabled.");
        byte[] rom=new byte[0x10000];rom[0]=0x55;rom[1]=0xAA;rom[0x18]=0x40;
        "PCIR"u8.CopyTo(rom.AsSpan(0x40));BitConverter.GetBytes((ushort)0x10DE).CopyTo(rom,0x44);BitConverter.GetBytes((ushort)0x2D19).CopyTo(rom,0x46);
        if(!VbiosResolver.RomMatchesDevice(rom,0x2D19) || VbiosResolver.RomMatchesDevice(rom,0x2D98))throw new Exception("ROM device matching failed.");
        if(VbiosResolver.RomMatchesDevice(new byte[100],0x2D19))throw new Exception("Truncated ROM accepted.");
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".rom");
        try{
            File.WriteAllBytes(path,rom);var imported=FirmwareTools.Import(path,@"PCI\VEN_10DE&DEV_2D19");
            rom[^1]=1;File.WriteAllBytes(path,rom);
            if(imported.Sha256==FirmwareTools.Import(path,@"PCI\VEN_10DE&DEV_2D19").Sha256)throw new Exception("Changed ROM hash was not detected.");
            bool rejected=false;try{FirmwareTools.Import(path,@"PCI\VEN_10DE&DEV_2D98");}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Mismatched ROM imported.");
        }finally{File.Delete(path);}
    }
}
