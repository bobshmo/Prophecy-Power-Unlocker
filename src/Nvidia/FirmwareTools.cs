using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NvpwrControlBlackwell;
internal sealed record ImportedRom(string Path,string Sha256,int DeviceId);
internal static class FirmwareTools
{
    internal static ImportedRom Import(string path,string pnpId)
    {
        var match=Regex.Match(pnpId??"",@"DEV_([0-9A-F]{4})",RegexOptions.IgnoreCase);
        if(!match.Success)throw new InvalidOperationException("GPU PCI identity is unavailable.");
        int device=Convert.ToInt32(match.Groups[1].Value,16);
        byte[] rom=File.ReadAllBytes(path);
        if(!VbiosResolver.RomMatchesDevice(rom,device))throw new InvalidOperationException("ROM does not contain matching NVIDIA PCI images for this GPU.");
        return new ImportedRom(System.IO.Path.GetFullPath(path),Convert.ToHexString(SHA256.HashData(rom)),device);
    }
    internal static OperationResult Backup(string destination)
    {
        if(!VbiosResolver.TryAutoDumpRom(out string dump,out string message))return OperationResult.Fail(message);
        File.Copy(dump,destination,true);
        return OperationResult.Ok("GPU ROM backup saved: "+destination);
    }
    internal static OperationResult Flash(ImportedRom selected)
    {
        string gpu=SystemProbe.GetGpuName();
        string pci=SystemProbe.FindNvidiaDeviceInstanceId(gpu);
        if(GpuProfiles.Detect(gpu,pci)==null)return OperationResult.Fail("GPU identity is unsupported or ambiguous.");
        var current=Import(selected.Path,pci);
        if(current.Sha256!=selected.Sha256 || current.DeviceId!=selected.DeviceId)return OperationResult.Fail("Imported ROM or GPU changed. Import the ROM again.");
        string tool=VbiosResolver.FindNvflash();
        if(string.IsNullOrEmpty(tool))return OperationResult.Fail("NVFlashB is unavailable.");
        string folder=System.IO.Path.Combine(ResolverStateStore.DirectoryPath,"firmware",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string staged=System.IO.Path.Combine(folder,"selected.rom");
        File.Copy(selected.Path,staged,false);
        if(Import(staged,pci).Sha256!=selected.Sha256)return OperationResult.Fail("Staged ROM verification failed.");
        if(!VbiosResolver.TryAutoDumpRom(out string backup,out string backupMessage))return OperationResult.Fail("Flash blocked: backup failed. "+backupMessage);
        Import(backup,pci);
        string retained=System.IO.Path.Combine(folder,"before-flash.rom");File.Copy(backup,retained,false);
        AppLog.Write("Flash requested; ROM SHA256="+selected.Sha256+"; backup="+retained);
        // No board-ID bypass or protection-disable flags. NVFlash retains its own checks and prompts.
        using var process=Process.Start(new ProcessStartInfo(tool,"\""+staged+"\""){UseShellExecute=true,WorkingDirectory=System.IO.Path.GetDirectoryName(tool)!});
        if(process==null)return OperationResult.Fail("NVFlashB could not start. Backup: "+retained);
        process.WaitForExit();
        return process.ExitCode==0?OperationResult.Ok("NVFlashB exited successfully. Follow its restart instructions; firmware activation has not been independently verified. Backup: "+retained):OperationResult.Fail("NVFlashB exited with code "+process.ExitCode+". Inspect its output. Backup: "+retained);
    }
}
