using NvAPIWrapper.GPU;

namespace VictusPowerUnlockGui;

internal static class PcfControllerDiscoveryTests
{
    private static byte[] ControllerInfo(uint mask)
    {
        var data = new byte[PcfControllerDiscovery.ControllerInfoSize];
        BitConverter.GetBytes(mask).CopyTo(data, 0);
        BitConverter.GetBytes(PcfControllerDiscovery.ControllerInfoVersion).CopyTo(data, 4);
        for (int index = 0; index < 32; index++)
            if ((mask & (1u << index)) != 0)
                data[PcfControllerDiscovery.ControllerEntryOffset + index * PcfControllerDiscovery.ControllerEntryStride] = 1;
        return data;
    }

    private static byte[] MasterInfo(uint mask)
    {
        var data = new byte[PcfControllerDiscovery.MasterInfoSize];
        BitConverter.GetBytes(mask).CopyTo(data, 0);
        BitConverter.GetBytes(PcfControllerDiscovery.MasterInfoVersion).CopyTo(data, 4);
        return data;
    }

    private static PcfControllerCandidate Good(uint index) => new(index, 0x10C88, new(125000, 90000, 80000, 140000));
    private static void Require(bool success, string message) { if (!success) throw new Exception(message); }
    private static void Rejects(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }

    internal static void Run()
    {
        var seen = new List<uint>();
        // Master bits 0/1 do not mean controllers 0/1. Only controller bit 5 is probed.
        var result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(1u << 5), MasterInfo(3)), index =>
        { seen.Add(index); return Good(index); });
        Require(result.SelectedIndex == 5 && result.ReportedMask == (1u << 5) && result.MasterMask == 3 && seen.SequenceEqual(new uint[] { 5 }),
            "PCF selection confused master and controller bitmaps or silently used controller zero.");
        Require(PcfControllerDiscovery.Plausible(Good(0).Values), "GPU maximum above shared TPP was incorrectly rejected.");

        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(3), null), Good);
        Require(result.SelectedIndex == null && result.Candidates.Length == 2 && result.Reason.Contains("ambiguous"),
            "Ambiguous PCF controllers were accepted.");

        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(0), MasterInfo(1)), _ => throw new Exception("Must not probe"));
        Require(result.SelectedIndex == null && result.Candidates.Length == 0, "Empty controller bitmap fell back to master/controller zero.");

        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(1), null, "Master query unsupported"), Good);
        Require(result.SelectedIndex == 0 && result.MasterMask == null && result.MasterDiagnostic != null,
            "An unsupported optional master query blocked valid controller selection.");

        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(3), null), index => index == 0 ?
            new(index, 0x10C88, new(125000, 90000, 80000, uint.MaxValue)) : Good(index));
        Require(result.SelectedIndex == 1, "Released/sentinel power values were accepted as a controller.");

        foreach (var values in new[]
        {
            new PcfPowerValues(0, 90000, 80000, 115000),
            new PcfPowerValues(uint.MaxValue, 90000, 80000, 115000),
            new PcfPowerValues(125000, 90000, 0, 115000),
            new PcfPowerValues(125000, 70000, 80000, 115000),
            new PcfPowerValues(125000, 120000, 80000, 115000),
            new PcfPowerValues(125000, 90000, 80000, 1000001)
        }) Require(!PcfControllerDiscovery.Plausible(values), "Incoherent PCF power fields accepted.");

        var invalid = ControllerInfo(1);
        invalid[4] ^= 1;
        Rejects(() => PcfControllerDiscovery.ReadControllerMask(invalid), "Unknown controller-info version accepted.");
        Rejects(() => PcfControllerDiscovery.ReadControllerMask(new byte[8]), "Truncated controller-info buffer accepted.");
        invalid = ControllerInfo(1);
        invalid[PcfControllerDiscovery.ControllerEntryOffset] = 0;
        Rejects(() => PcfControllerDiscovery.ReadControllerMask(invalid), "Bitmap with an invalid reported entry accepted.");
        invalid = ControllerInfo(0);
        invalid[PcfControllerDiscovery.ControllerEntryOffset] = 1;
        Rejects(() => PcfControllerDiscovery.ReadControllerMask(invalid), "Valid entry missing from bitmap accepted.");

        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(1), null), _ => Good(2));
        Require(result.SelectedIndex == null, "Probe returned an unreported controller index.");
        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(1), null), index => Good(index) with { LayoutVersion = 0xDEADBEEF });
        Require(result.SelectedIndex == null, "Unsupported controller power layout accepted.");
        result = PcfControllerDiscovery.Discover(() => throw new InvalidOperationException("No driver"), Good);
        Require(result.SelectedIndex == null && result.Reason.Contains("No driver"), "Driver-query failure silently selected zero.");
        result = PcfControllerDiscovery.Discover(() => new(ControllerInfo(1u << 31), null), Good);
        Require(result.SelectedIndex == 31, "High controller bitmap bit was decoded incorrectly.");
    }
}
