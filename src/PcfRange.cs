using NvpwrControlBlackwell;
namespace VictusPowerUnlockGui;
internal static class PcfRange
{
    internal static void Validate(GpuProfile? profile,int watts)
    {
        int max=profile?.StockTgpMaxW??0;
        if(max<=0)throw new InvalidOperationException("PCF GPU identity is unavailable. Read GPU before applying.");
        if(watts<1||watts>max)throw new ArgumentOutOfRangeException(nameof(watts),$"PCF accepts 1–{max} W including Dynamic Boost.");
    }
    internal static void Test()
    {
        foreach(var p in GpuProfiles.All()) {
            int max=p.Id switch {"5050" or "5060" or "5070"=>115,"5070ti" or "4050" or "4060" or "4070"=>140,_=>175};
            if(p.StockTgpMaxW!=max)throw new Exception("Wrong stock PCF ceiling: "+p.Id);
            foreach(int w in new[]{1,73,99,max})Validate(p,w);
            foreach(int w in new[]{0,-1,max+1}) {
                bool rejected=false;try{Validate(p,w);}catch(ArgumentOutOfRangeException){rejected=true;}
                if(!rejected)throw new Exception("Invalid PCF wattage accepted.");
            }
        }
        try{Validate(null,100);}catch(InvalidOperationException){return;}
        throw new Exception("Unknown GPU accepted for PCF.");
    }
}
