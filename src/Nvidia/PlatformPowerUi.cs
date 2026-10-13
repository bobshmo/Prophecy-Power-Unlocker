using NvAPIWrapper.GPU;
using VictusPowerUnlockGui;
namespace NvpwrControlBlackwell;
internal sealed partial class MainForm{
 private readonly Dictionary<PcfPowerFields,(NumericUpDown Input,Label Status,Button Apply)> _platformControls=new();
 private readonly Dictionary<PcfPowerFields,PlatformPowerScan> _platformScans=new();
 private TppCalibrationResult? _tppCalibration;
 private CancellationTokenSource? _tppCancel;
 private void AddPlatformPowerControls(FlowLayoutPanel root){foreach(var field in new[]{PcfPowerFields.ACTargetTPPLimit,PcfPowerFields.ACMaxGPULimit}){
  var card=Card(230,true);card.Dock=DockStyle.None;var title=new Label{Text=field+" (W)",AutoSize=true,Location=new Point(U(16),U(14))};var input=new NumericUpDown{Minimum=0,Maximum=1000,Enabled=false,Location=new Point(U(16),U(50)),Width=U(110)};var scan=ButtonBase("scanPlatform",U(165));scan.Text="Scan laptop / GPU";scan.Location=new Point(U(140),U(44));var apply=ButtonBase("applyPlatform",U(140));apply.Text="Apply";apply.Enabled=false;apply.Location=new Point(U(320),U(44));var status=new Label{Text="Scan for firmware stock/max bounds.",Location=new Point(U(16),U(98)),Size=new Size(U(900),U(115))};card.Resize+=(_,_)=>status.Width=Math.Max(U(220),card.ClientSize.Width-U(32));card.Controls.AddRange(new Control[]{title,input,scan,apply,status});_responsiveWide.Add(card);root.Controls.Add(card);_platformControls[field]=(input,status,apply);
  scan.Click+=async(_,_)=>await ScanPlatformPowerAsync();apply.Click+=async(_,_)=>{if(_busy||!_platformScans.TryGetValue(field,out var info))return;int watts=(int)input.Value;await RunOperationTask(()=>OperationResult.Ok(field==PcfPowerFields.ACTargetTPPLimit&&_tppCalibration?.Identity==info.Identity&&_tppCalibration.Accepted.Contains(watts)?TppCalibration.Apply(_tppCalibration,watts):field==PcfPowerFields.ACMaxGPULimit&&_pcfGpuTest!=null?PcfGpuTester.Apply(_pcfGpuTest,watts):TppControl.Apply(info,watts)),false);await ScanPlatformPowerAsync();};
  input.ValueChanged+=(_,_)=>{if(field==PcfPowerFields.ACMaxGPULimit&&_pcfGpuTest!=null&&!_busy)apply.Enabled=_pcfGpuTest.Accepted.Contains((int)input.Value);if(field==PcfPowerFields.ACTargetTPPLimit&&_tppCalibration!=null&&!_busy)apply.Enabled=_tppCalibration.Accepted.Contains((int)input.Value);};
  if(field==PcfPowerFields.ACTargetTPPLimit)AddTppCalibrationControls(root);else AddPcfGpuTools(root);
 }}
 private void AddTppCalibrationControls(FlowLayoutPanel root){
  var card=Card(190,true);card.Dock=DockStyle.None;
  var label=new Label{Text="ACTargetTPPLimit accepted-target calibration (W)",AutoSize=true,Location=new Point(U(16),U(14))};
  var low=new NumericUpDown{Minimum=1,Maximum=1000,Value=100,Location=new Point(U(16),U(50)),Width=U(90)};
  var high=new NumericUpDown{Minimum=1,Maximum=1000,Value=180,Location=new Point(U(125),U(50)),Width=U(90)};
  var start=ButtonBase("calibrateTpp",U(180));start.Text="Calibrate TPP range";start.Location=new Point(U(235),U(44));
  var cancel=ButtonBase("cancelTpp",U(110));cancel.Text="Cancel";cancel.Location=new Point(U(430),U(44));cancel.Enabled=false;
  var report=new Label{Text="Tests every whole-watt target inside the chosen interval. AC power and an idle CPU/GPU are required. Restores the original PCF snapshot. This does not establish electrical/thermal safety or factory stock.",Location=new Point(U(16),U(100)),Size=new Size(U(900),U(75))};
  card.Resize+=(_,_)=>report.Width=Math.Max(U(220),card.ClientSize.Width-U(32));card.Controls.AddRange(new Control[]{label,low,high,start,cancel,report});_responsiveWide.Add(card);root.Controls.Add(card);
  cancel.Click+=(_,_)=>_tppCancel?.Cancel();
  start.Click+=async(_,_)=>{
   if(_busy)return;
   if(!_platformScans.TryGetValue(PcfPowerFields.ACTargetTPPLimit,out var device)){await ScanPlatformPowerAsync();if(!_platformScans.TryGetValue(PcfPowerFields.ACTargetTPPLimit,out device))return;}
   int lower=(int)low.Value,upper=(int)high.Value;
   if(upper<lower||upper-lower>300){report.Text="Choose a valid interval no wider than 300 W.";return;}
   if(MessageBox.Show(this,$"Temporarily test TPP targets {lower}–{upper} W? Close games and other power-control apps first. The original PCF snapshot will be restored. Readback acceptance is not a safe operating-power guarantee.","Confirm TPP calibration",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   _tppCalibration=null;_tppCancel=new CancellationTokenSource();_busy=true;UpdateWriteButtons();start.Enabled=false;cancel.Enabled=true;low.Enabled=false;high.Enabled=false;
   try{var result=await Task.Run(()=>TppCalibration.Calibrate(device,lower,upper,_tppCancel.Token,w=>BeginInvoke(new Action(()=>report.Text=$"Testing {w} W; original snapshot will be restored."))));_tppCalibration=result;report.Text=result.Describe();}
   catch(OperationCanceledException){report.Text="Calibration cancelled; restoration completed.";}
   catch(Exception ex){report.Text="Calibration stopped: "+ex.Message;}
   finally{_tppCancel.Dispose();_tppCancel=null;_busy=false;UpdateWriteButtons();start.Enabled=true;cancel.Enabled=false;low.Enabled=true;high.Enabled=true;await ScanPlatformPowerAsync();}
  };
 }
 private async Task ScanPlatformPowerAsync(){if(_busy)return;_busy=true;UpdateWriteButtons();try{foreach(var entry in _platformControls){var c=entry.Value;c.Input.Enabled=false;c.Apply.Enabled=false;_platformScans.Remove(entry.Key);try{var result=await Task.Run(()=>TppControl.Scan(entry.Key));c.Input.Minimum=0;c.Input.Maximum=Math.Max(1000,result.CurrentMw/1000m);c.Input.Value=result.CurrentMw/1000m;_platformScans[entry.Key]=result;if(entry.Key==PcfPowerFields.ACTargetTPPLimit){if(_tppCalibration?.Identity!=result.Identity)_tppCalibration=null;_tppCalibration??=TppCalibration.Load(result);}c.Status.Text="Current "+(result.CurrentMw/1000m)+" W. "+(result.Adjustable?"Firmware stock "+result.StockW+" W → maximum "+result.MaximumW+" W.":"Stock/max bounds not advertised; adjustment unavailable.");if(result.Adjustable){c.Input.Minimum=0;c.Input.Maximum=result.MaximumW!.Value;c.Input.Minimum=result.StockW!.Value;c.Input.Value=Math.Clamp(result.CurrentMw/1000m,c.Input.Minimum,c.Input.Maximum);c.Input.Enabled=true;c.Apply.Enabled=true;}}catch(Exception ex){c.Status.Text="Scan unavailable: "+ex.Message;}}if(_tppCalibration is {Accepted.Length: >0} tested&&_platformScans.TryGetValue(PcfPowerFields.ACTargetTPPLimit,out var current)&&current.Identity==tested.Identity){var c=_platformControls[PcfPowerFields.ACTargetTPPLimit];c.Input.Minimum=0;c.Input.Maximum=tested.Accepted.Max();c.Input.Minimum=tested.Accepted.Min();c.Input.Value=Math.Clamp(current.CurrentMw/1000m,c.Input.Minimum,c.Input.Maximum);c.Input.Enabled=true;c.Apply.Enabled=tested.Accepted.Contains((int)c.Input.Value);c.Status.Text=tested.Describe();}RefreshPcfGpuTest();}finally{_busy=false;UpdateWriteButtons();}}
}
