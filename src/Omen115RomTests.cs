using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal static class Omen115RomTests
{
    internal static void Run()
    {
        // Synthetic ROM containing only the reviewed format and power values;
        // no user's ROM, device-instance ID or validation is distributed.
        byte[] bytes=new byte[0x10000];bytes[0]=0x55;bytes[1]=0xAA;bytes[0x18]=0x40;
        "PCIR"u8.CopyTo(bytes.AsSpan(0x40));BitConverter.GetBytes((ushort)0x10DE).CopyTo(bytes,0x44);BitConverter.GetBytes((ushort)0x2F58).CopyTo(bytes,0x46);
        int table=0x200,entry=table+0x38+2*0x66;
        bytes[table]=0x40;bytes[table+1]=0x38;bytes[table+2]=0x66;bytes[table+3]=3;
        foreach(var pair in new[]{(0x0D,5000u),(0x11,80000u),(0x15,115000u),(0x49,100000u)})BitConverter.GetBytes(pair.Item2).CopyTo(bytes,entry+pair.Item1);
        string folder=Path.Combine(Path.GetTempPath(),"OmenRomTests-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        string? previous=Environment.GetEnvironmentVariable("PROPHECY_STATE_DIR");Environment.SetEnvironmentVariable("PROPHECY_STATE_DIR",Path.Combine(folder,"validation"));
        try{
            string file=Path.Combine(folder,"synthetic.rom");File.WriteAllBytes(file,bytes);
            string pci=@"PCI\VEN_10DE&DEV_2F58&SUBSYS_8E71103C";
            var result=VbiosResolver.ResolveFromRom(file,GpuProfiles.Detect("RTX 5070 Ti Laptop",pci),"fixture",pci);
            if(!result.Resolved||result.StockMaxW!=115||result.CandidateCount!=1||result.SemanticScore!=100||result.ShadowOffset!=entry+0x15)throw new Exception("HP OMEN 115 W structural resolution failed.");
            string other=@"PCI\VEN_10DE&DEV_2F58&SUBSYS_1234103C";
            if(VbiosResolver.ResolveFromRom(file,GpuProfiles.Detect("RTX 5070 Ti Laptop",other),"fixture",other).Resolved)throw new Exception("HP variant was applied to an unreviewed board.");
            bytes[0x46]=0x19;bytes[0x47]=0x2D;File.WriteAllBytes(file,bytes);
            if(VbiosResolver.ResolveFromRom(file,GpuProfiles.Detect("RTX 5070 Ti Laptop",pci),"fixture",pci).Resolved)throw new Exception("Wrong ROM device accepted.");
        }finally{Environment.SetEnvironmentVariable("PROPHECY_STATE_DIR",previous);Directory.Delete(folder,true);}
    }
}
