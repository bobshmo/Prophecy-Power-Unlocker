namespace VictusPowerUnlockGui;

internal static class PcfBaselineTests
{
    internal static void Run()
    {
        if(!PcfUnlock.BaselineReached(115000,115)||!PcfUnlock.BaselineReached(140000,115)||PcfUnlock.BaselineReached(90000,115))
            throw new Exception("PCF baseline threshold must include higher active caps.");
        if(PcfUnlock.BaselineReached(0,115)||PcfUnlock.BaselineReached(uint.MaxValue,115)||PcfUnlock.ShouldAutoEnable(true,uint.MaxValue,115))
            throw new Exception("Released/unavailable PCF readback cannot enable the baseline.");
        if(PcfUnlock.SelectEnabledTarget(115,140000)!=140000||PcfUnlock.SelectEnabledTarget(115,115000)!=115000||PcfUnlock.SelectEnabledTarget(115,90000)!=115000)
            throw new Exception("Enabling the PCF baseline reduced a higher cap or missed a restricted cap.");
        foreach(uint invalid in new uint[]{0,uint.MaxValue})
        {
            bool refused=false;
            try{PcfUnlock.SelectEnabledTarget(115,invalid);}catch(InvalidOperationException){refused=true;}
            if(!refused)throw new Exception("Unavailable PCF cap accepted as an enable target.");
        }
    }
}
