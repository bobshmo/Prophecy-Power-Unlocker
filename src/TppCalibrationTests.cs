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
