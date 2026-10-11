#nullable enable
namespace NvpwrControlBlackwell;

internal static class ResolverStateStore
{
    private static readonly object Gate=new();
    private static readonly string[] Names={"driver-resolver-cache.txt","vbios-resolver-cache-v3.txt"};
    internal static string DirectoryPath=>Environment.GetEnvironmentVariable("PROPHECY_STATE_DIR") is {Length: >0} path
        ?Path.GetFullPath(path):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ProphecyPowerUnlocker","validation");
    internal static string CacheFile(string name)
    {
        if(!Names.Contains(name))throw new ArgumentException("Unknown resolver cache.",nameof(name));
        string target=Path.Combine(DirectoryPath,name);
        lock(Gate){if(!File.Exists(target))foreach(string dir in new[]{"prophecy-state","state"}){
            string old=Path.Combine(AppContext.BaseDirectory,dir,name);
            if(!File.Exists(old))continue;
            Directory.CreateDirectory(DirectoryPath);File.Copy(old,target,false);break;
        }}
        return target;
    }
    internal static OperationResult Import(string folder)
    {
        int count=0;
        lock(Gate){foreach(string name in Names){
            string? source=new[]{Path.Combine(folder,"prophecy-state",name),Path.Combine(folder,"state",name),Path.Combine(folder,name)}.FirstOrDefault(File.Exists);
            if(source==null)continue;
            string target=CacheFile(name);
            if(Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase)){count++;continue;}
            var existing=File.Exists(target)?File.ReadAllLines(target):Array.Empty<string>();
            var incoming=File.ReadAllLines(source).Where(s=>!string.IsNullOrWhiteSpace(s));
            Directory.CreateDirectory(DirectoryPath);File.WriteAllLines(target,existing.Concat(incoming).Distinct(StringComparer.OrdinalIgnoreCase));count++;
        }}
        return count==0?OperationResult.Fail("No previous resolver caches found. Select the old app folder or its state folder."):
            OperationResult.Ok("Imported previous resolver state. Exact driver hashes and GPU/VBIOS identity are checked before enabling writes; no hardware settings were applied.");
    }
}
