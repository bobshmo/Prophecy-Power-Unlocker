using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal static class GpuDetectionTests
{
    public static void Run()
    {
        int count = 0;
        foreach (var profile in GpuProfiles.All()) foreach (int id in profile.KnownDeviceIds)
        {
            string pci = $"PCI\\VEN_10DE&DEV_{id:X4}&SUBSYS_1234103C\\TEST";
            if (GpuProfiles.Detect(profile.Match, pci)?.Id != profile.Id || GpuProfiles.Detect("", pci)?.Id != profile.Id)
                throw new Exception($"Failed PCI mapping {id:X4}");
            if (GpuProfiles.Detect(profile.Match, pci.Replace("VEN_10DE", "VEN_1002")) != null)
                throw new Exception("Non-NVIDIA vendor accepted.");
            count++;
        }
        if (count != 22) throw new Exception("Expected all 22 documented laptop PCI IDs.");
        if (GpuProfiles.Detect("RTX 5060 Laptop", "PCI\\VEN_10DE&DEV_2717") != null) throw new Exception("Conflicting model identity accepted.");
        if (GpuProfiles.Detect("RTX 4060 Laptop", "PCI\\VEN_10DE&DEV_2882") != null) throw new Exception("Non-laptop device accepted.");
        if (GpuProfiles.Detect("RTX 5060 Laptop", "PCI\\VEN_10DE&DEV_FFFF") != null) throw new Exception("Unknown PCI device accepted.");
        if (GpuProfiles.Detect("RTX 5070 Ti Laptop", "PCI\\VEN_10DE&DEV_2F18")?.Id != "5070ti") throw new Exception("Ti variant misidentified.");
    }
}
