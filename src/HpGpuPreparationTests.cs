using NvAPIWrapper.GPU;

namespace VictusPowerUnlockGui;

internal static class HpGpuPreparationTests
{
    internal static void Run()
    {
        if (HpPlatform.SelectGpsTarget(75,"8DD2","F.15")!=75 || HpPlatform.SelectGpsTarget(87,"other","F.1")!=87 || HpPlatform.SelectGpsTarget(0,"8DD2","F.15")!=87 || HpPlatform.SelectGpsTarget(0,"other","F.15")!=0 || HpPlatform.SelectGpsTarget(0,"8DD2","F.16")!=0) throw new Exception("GPS target preservation/verified factory repair failed.");
        var original = new PcfPowerValues(180000, 90000, 80000, 140000);
        var current = new PcfPowerValues(125000, 90000, 80000, 100000);
        var writes = new List<PcfPowerFields>();
        HpPlatform.RestorePcfValues(original, () => current, (field, value) => {
            writes.Add(field);
            current = current.With(field, value);
        });
        if (!writes.SequenceEqual(new[] { PcfPowerFields.ACMaxGPULimit, PcfPowerFields.ACTargetTPPLimit }))
            throw new Exception("HPCM restoration must target changed PCF fields only, GPU max before TPP.");
        writes.Clear();
        HpPlatform.RestorePcfValues(original, () => current, (field, value) => writes.Add(field));
        if (writes.Count != 0) throw new Exception("Unchanged PCF fields were unnecessarily written.");

        current = new PcfPowerValues(125000, 90000, 80000, 100000);
        bool failed = false;
        try { HpPlatform.RestorePcfValues(original, () => current, (_, _) => { }); }
        catch (InvalidOperationException) { failed = true; }
        if (!failed) throw new Exception("HPCM PCF restore readback mismatch was ignored.");

        // Preserve captured controller values exactly, including fractional watts
        // and release values. Never round them to UI whole-watt targets.
        original = new PcfPowerValues(150123, 90000, uint.MaxValue, 115987);
        current = new PcfPowerValues(125000, 90000, 80000, 100000);
        HpPlatform.RestorePcfValues(original, () => current, (field, value) => current = current.With(field, value));
        if (current.ACTargetTPPLimitInMilliwatts != 150123 || current.ACMaxGPULimitInMilliwatts != 115987 || current.ACMinGPULimitInMilliwatts != uint.MaxValue)
            throw new Exception("HPCM PCF restore lost exact milliwatt/controller values.");

        foreach (var target in new[] {
            new PcfPowerValues(150000, 5000, 5000, 5000),
            new PcfPowerValues(180000, 130000, 120000, 140000)
        })
        {
            current = new PcfPowerValues(125000, 90000, 80000, 100000);
            HpPlatform.RestorePcfValues(target, () => current, (field, value) => {
                current = current.With(field, value);
                if (current.ACMinGPULimitInMilliwatts > current.ACDefaultGPULimitInMilliwatts ||
                    current.ACDefaultGPULimitInMilliwatts > current.ACMaxGPULimitInMilliwatts)
                    throw new Exception("HPCM restoration created an incoherent intermediate GPU budget.");
            });
        }
        bool boost = false;
        HpPlatform.RestorePcfState(current, true, () => current, (_, _) => { }, () => boost, value => boost = value);
        if (!boost) throw new Exception("Dynamic Boost was not restored after HPCM.");
        failed = false;
        try { HpPlatform.RestorePcfState(current, true, () => current, (_, _) => { }, () => false, _ => { }); }
        catch (InvalidOperationException) { failed = true; }
        if (!failed) throw new Exception("Dynamic Boost restoration readback mismatch was ignored.");

        var backend = new NvpwrControlBlackwell.PowerBackend();
        // Invalid requests fail before compatibility or hardware access.
        if (backend.RestoreCurrentExact(0).Success || backend.RestoreCurrentExact(uint.MaxValue).Success || backend.SetCurrent(0).Success)
            throw new Exception("Invalid CURRENT restoration targets were accepted.");
    }
}

