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
  if(p.Writes.Count!=141||p.Writes[0]!=0||p.Writes.Last()!=140000||r.Accepted.Contains(0)||r.Accepted.Contains(97)||r.Accepted.Max()!=115||p.Value!=90000||p.Restores!=1)throw new Exception("PCF full interval/zero/clamp/hole/restoration failed.");
  if(!r.Describe().Contains("Maximum not determined")||!PcfGpuTester.Valid(r,"gpu",150000)||PcfGpuTester.Valid(r,"gpu",180000)||PcfGpuTester.Valid(r,"other",150000))throw new Exception("PCF result provenance failed.");
  var cancel=new CancellationTokenSource();p=new Fake();bool stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,140,false,()=>{},cancel.Token,_=>cancel.Cancel());}catch(OperationCanceledException){stopped=true;}if(!stopped||p.Value!=90000||p.Restores!=1)throw new Exception("PCF cancellation restoration failed.");
  p=new Fake{FailRestore=true};stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,1,false,()=>{},CancellationToken.None);}catch(InvalidOperationException){stopped=true;}if(!stopped)throw new Exception("PCF restoration failure ignored.");
  p=new Fake();int checks=0;stopped=false;try{PcfGpuTester.Run(p,"gpu",150000,140,false,()=>{if(++checks==4)throw new InvalidOperationException("load");},CancellationToken.None);}catch(InvalidOperationException){stopped=true;}if(!stopped||p.Value!=90000)throw new Exception("PCF load interruption failed.");
  if(PcfGpuTester.Valid(r with{Accepted=r.Accepted.Concat(new[]{80}).ToArray()},"gpu",150000))throw new Exception("Duplicate/malformed PCF cache accepted.");
 }
}
