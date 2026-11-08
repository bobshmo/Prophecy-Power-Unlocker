namespace VictusPowerUnlockGui;
internal static class PcfGpuTesterTests
{
 private sealed class Fake:ITppProbe{
  public uint Value=90000;public int Restores;public bool FailRestore;public readonly List<uint> Writes=new();
  public uint Read()=>Value;
  public void Write(uint value){Writes.Add(value);Value=value==0?uint.MaxValue:Math.Clamp(value,80000u,115000u);if(value==97000)throw new InvalidOperationException("unsupported hole");}
  public void Restore(){Restores++;if(!FailRestore)Value=90000;}public void Dispose(){}
 }
 internal static void Run(){
  if(PcfUnlock.SelectStockTarget(140,115)!=115||PcfUnlock.SelectStockTarget(115,140)!=115||PcfUnlock.SelectStockTarget(115,null)!=115)throw new Exception("PCF baseline selection failed.");
  var p=new Fake();var r=PcfGpuTester.Run(p,"gpu",150000,140,false,()=>{},CancellationToken.None);
  if(p.Writes.Count!=140||p.Writes[0]!=1000||p.Writes.Last()!=140000||r.Accepted.Contains(0)||r.Accepted.Contains(97)||r.Accepted.Max()!=115||p.Value!=90000||p.Restores!=141||r.Skipped==null||r.Skipped.Length!=0)throw new Exception("PCF full interval/zero/clamp/hole/restoration failed.");
  if(!r.Describe().Contains("Configured test maximum")||!PcfGpuTester.Valid(r,"gpu",150000)||PcfGpuTester.Valid(r,"gpu",180000)||PcfGpuTester.Valid(r,"other",150000))throw new Exception("PCF result provenance failed.");
  var bounded=new Fake();var range=PcfGpuTester.Run(bounded,"gpu",150000,115,false,()=>{},CancellationToken.None,minimum:80);
  if(bounded.Writes.Count!=36||bounded.Writes.First()!=80000||bounded.Writes.Last()!=115000||range.Minimum!=80||!PcfGpuTester.Valid(range,"gpu",150000)||range.Accepted.Contains(97))throw new Exception("Reported-minimum to configured-maximum sweep failed.");
  if(PcfGpuTester.MinimumWholeWatts(80500)!=81)throw new Exception("Fractional reported minimum rounded down.");
  bool invalid=false;try{PcfGpuTester.Run(new Fake(),"gpu",150000,79,false,()=>{},CancellationToken.None,minimum:80);}catch(ArgumentOutOfRangeException){invalid=true;}if(!invalid)throw new Exception("Maximum below minimum accepted.");
  if(PcfGpuTester.Valid(range with{Minimum=79},"gpu",150000)||PcfGpuTester.Valid(range with{Protocol=3},"gpu",150000))throw new Exception("Wrong interval/old PCF cache accepted.");
  var cancel=new CancellationTokenSource();p=new Fake();bool stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,140,false,()=>{},cancel.Token,_=>cancel.Cancel());}catch(OperationCanceledException){stopped=true;}if(!stopped||p.Value!=90000||p.Restores!=2)throw new Exception("PCF cancellation restoration failed.");
  p=new Fake{FailRestore=true};stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,1,false,()=>{},CancellationToken.None);}catch(InvalidOperationException){stopped=true;}if(!stopped)throw new Exception("PCF restoration failure ignored.");
  p=new Fake();int checks=0;stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,140,false,()=>{if(++checks==4)throw new InvalidOperationException("load");},CancellationToken.None);}catch(InvalidOperationException){stopped=true;}if(!stopped||p.Value!=90000)throw new Exception("PCF load interruption failed.");
  if(PcfGpuTester.Valid(r with{Accepted=r.Accepted.Concat(new[]{80}).ToArray()},"gpu",150000))throw new Exception("Duplicate/malformed PCF cache accepted.");
  if(PcfGpuTester.Valid(r with{Protocol=0},"gpu",150000)||PcfGpuTester.Valid(r with{Protocol=2},"gpu",150000)||PcfGpuTester.Valid(r,"gpu",150000,"changed")||PcfGpuTester.Valid(r with{Skipped=new[]{0}},"gpu",150000)||PcfGpuTester.Valid(r with{Accepted=null!},"gpu",150000))throw new Exception("Legacy, changed-context or incomplete PCF cache accepted.");
 }
}
