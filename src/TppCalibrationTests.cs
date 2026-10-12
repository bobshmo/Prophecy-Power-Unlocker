namespace VictusPowerUnlockGui;
internal static class TppCalibrationTests
{
    private sealed class Fake : ITppProbe
    {
        public uint Value=125000;public int Restores,Writes;public bool RestoreFails;
        public uint Read()=>Value;
        public void Write(uint value){Writes++;Value=Math.Clamp(value,110000u,170000u);}
        public void Restore(){Restores++;if(!RestoreFails)Value=125000;}
        public void Dispose(){}
    }
    internal static void Run()
    {
        if(!TppCalibration.CpuLoadAllowsCalibration(0)||!TppCalibration.CpuLoadAllowsCalibration(24.99)||TppCalibration.CpuLoadAllowsCalibration(25)||TppCalibration.CpuLoadAllowsCalibration(26)||TppCalibration.CpuLoadAllowsCalibration(double.NaN))throw new Exception("CPU calibration threshold must be strictly below 25%.");
        if(CpuLoadReader.Calculate(0,0,0,90,100,0)!=10||CpuLoadReader.Calculate(0,0,0,0,100,0)!=100||CpuLoadReader.Calculate(0,0,0,100,100,0)!=0)throw new Exception("CPU idle accounting failed.");
        bool badCpu=false;try{CpuLoadReader.Calculate(0,0,0,0,0,0);}catch(InvalidOperationException){badCpu=true;}if(!badCpu)throw new Exception("Missing CPU sample accepted as idle.");
        var fake=new Fake();var result=TppCalibration.Run(fake,"test",100,180,()=>{},CancellationToken.None);
        if(result.Accepted.Min()!=110||result.Accepted.Max()!=170||result.Accepted.Length!=61||fake.Value!=125000||fake.Restores!=1)throw new Exception("Calibration clamping/restoration failed.");
        fake=new Fake();var cancel=new CancellationTokenSource();bool cancelled=false;
        try{TppCalibration.Run(fake,"test",100,180,()=>{},cancel.Token,_=>cancel.Cancel());}catch(OperationCanceledException){cancelled=true;}
        if(!cancelled||fake.Value!=125000||fake.Restores!=1)throw new Exception("Cancelled calibration did not restore original TPP.");
        fake=new Fake{RestoreFails=true};bool failed=false;
        try{TppCalibration.Run(fake,"test",100,110,()=>{},CancellationToken.None);}catch(InvalidOperationException){failed=true;}
        if(!failed)throw new Exception("Restoration failure was reported as success.");
        fake=new Fake();int checks=0;failed=false;
        try{TppCalibration.Run(fake,"test",100,110,()=>{if(++checks==3)throw new InvalidOperationException("load increased");},CancellationToken.None);}catch(InvalidOperationException){failed=true;}
        if(!failed||fake.Value!=125000)throw new Exception("Load interruption did not restore TPP.");
        fake=new Fake();result=TppCalibration.Run(fake,"test",110,130,()=>{},CancellationToken.None);
        if(!result.Describe().Contains("Upper boundary not found"))throw new Exception("Test interval endpoint mislabeled as hardware maximum.");
    }
}
