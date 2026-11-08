using NvAPIWrapper.GPU;
using VictusPowerUnlockGui;
namespace NvpwrControlBlackwell;
internal sealed partial class MainForm
{
 private PcfGpuTestResult? _pcfGpuTest;
 private Action<PlatformPowerScan>? _refreshPcfBaseline;
 private void AddPcfGpuTools(FlowLayoutPanel root)
 {
  var card=Card(250,true);card.Dock=DockStyle.None;
  var toggle=new CheckBox{Text="ACMaxGPULimit — PCF Max (HP unlock)",AutoSize=true,Location=new Point(U(16),U(16)),Enabled=false};
  var detect=ButtonBase("scanPcfBaseline",U(190));detect.Text="Detect / enable HP unlock";detect.Location=new Point(U(16),U(52));
  var test=ButtonBase("testPcf",U(185));test.Text="Test minimum → max";test.Location=new Point(U(220),U(52));
  var cancel=ButtonBase("cancelPcf",U(100));cancel.Text="Cancel";cancel.Enabled=false;cancel.Location=new Point(U(420),U(52));
  var maxLabel=new Label{Text="Test max (W)",AutoSize=true,Location=new Point(U(16),U(94))};
  var testMax=new NumericUpDown{Minimum=1,Maximum=1000,Value=Math.Clamp(_compat.Profile?.MaxPowerW??1,1,1000),Location=new Point(U(120),U(90)),Width=U(95)};
  bool maxEdited=false;testMax.ValueChanged+=(_,_)=>maxEdited=true;
  var report=new Label{Text="The baseline toggle raises a restricted controller cap to the stock threshold and preserves higher caps. It does not establish effective GPU power. The tester checks every whole-watt target from the reported ACMinGPULimit through Test max. This does not discover a hardware maximum. Close games and other power-control apps first.",Location=new Point(U(16),U(130)),Size=new Size(U(900),U(95))};
  card.Resize+=(_,_)=>report.Width=Math.Max(U(220),card.ClientSize.Width-U(32));card.Controls.AddRange(new Control[]{toggle,detect,test,cancel,maxLabel,testMax,report});_responsiveWide.Add(card);root.Controls.Add(card);
  PcfDetection? detected=null;bool updating=false;CancellationTokenSource? cancellation=null;
  void UpdateToggle(PcfDetection d){updating=true;toggle.Text=$"ACMaxGPULimit — PCF Max (HP unlock): baseline {d.StockW} W / current {d.CurrentMw/1000.0:0.###} W";toggle.Checked=PcfUnlock.BaselineReached(d.CurrentMw,d.StockW);toggle.Enabled=PcfUnlock.IsFiniteCap(d.CurrentMw);updating=false;}
  _refreshPcfBaseline=scan=>{if(!maxEdited&&_compat.Profile is {} profile){testMax.Value=Math.Clamp(scan.MaximumW??profile.MaxPowerW,1,1000);maxEdited=false;}if(detected==null)return;if(!scan.Identity.EndsWith(" | "+detected.Pci,StringComparison.OrdinalIgnoreCase)){toggle.Enabled=false;return;}detected=detected with{CurrentMw=scan.CurrentMw,Restricted=!PcfUnlock.BaselineReached(scan.CurrentMw,detected.StockW)};UpdateToggle(detected);};
  detect.Click+=async(_,_)=>{if(_busy)return;_busy=true;detect.Enabled=false;toggle.Enabled=false;UpdateWriteButtons();try{
   detected=await Task.Run(PcfUnlock.Scan);report.Text=detected.Message;
   bool enable=PcfUnlock.ShouldAutoEnable(detected.Hp,detected.CurrentMw,detected.StockW);
   if(!detected.Hp&&detected.Restricted)enable=MessageBox.Show(this,$"Enable ACMaxGPULimit baseline {detected.StockW} W on this non-HP board?","Enable PCF baseline",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
   if(enable){report.Text=await Task.Run(()=>PcfUnlock.Apply(detected,true));detected=await Task.Run(PcfUnlock.Scan);}
   UpdateToggle(detected);
  }catch(Exception ex){report.Text=SafeError.Describe(ex);}finally{_busy=false;detect.Enabled=true;UpdateWriteButtons();}};
  toggle.CheckedChanged+=async(_,_)=>{if(updating)return;if(_busy||detected==null){if(detected!=null)UpdateToggle(detected);return;}bool enable=toggle.Checked;
   if(enable&&!detected.Hp&&MessageBox.Show(this,"Enable the PCF baseline on this non-HP board?","Enable PCF baseline",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){UpdateToggle(detected);return;}
   _busy=true;toggle.Enabled=false;UpdateWriteButtons();try{report.Text=await Task.Run(()=>PcfUnlock.Apply(detected,enable));}catch(Exception ex){report.Text=SafeError.Describe(ex);}finally{try{detected=await Task.Run(PcfUnlock.Scan);UpdateToggle(detected);}catch{toggle.Enabled=false;}_busy=false;UpdateWriteButtons();}
  };
  cancel.Click+=(_,_)=>cancellation?.Cancel();
  test.Click+=async(_,_)=>{if(_busy)return;
   int configuredMaximum=(int)testMax.Value;
   if(MessageBox.Show(this,$"Test every whole-watt ACMaxGPULimit target from the controller-reported minimum through {configuredMaximum} W? Values below the minimum are not written. Each attempt restores PCF and Dynamic Boost; checks MAX without replaying public CURRENT. Close games and other power-control apps. Use Verify under load afterward to measure effective power.","Confirm PCF GPU tester",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   _busy=true;test.Enabled=false;testMax.Enabled=false;detect.Enabled=false;toggle.Enabled=false;cancel.Enabled=true;UpdateWriteButtons();cancellation=new CancellationTokenSource();
   try{var device=await Task.Run(()=>TppControl.Scan(PcfPowerFields.ACMaxGPULimit));_pcfGpuTest=await Task.Run(()=>PcfGpuTester.Test(device,cancellation.Token,w=>BeginInvoke(new Action(()=>report.Text=$"Testing ACMaxGPULimit {w} W; original PCF snapshot will be restored.")),configuredMaximum));report.Text=_pcfGpuTest.Describe();}
   catch(OperationCanceledException){report.Text="Cancelled; original PCF snapshot restored.";}catch(Exception ex){report.Text="Test stopped: "+SafeError.Describe(ex);}
   finally{cancellation.Dispose();cancellation=null;_busy=false;test.Enabled=true;testMax.Enabled=true;detect.Enabled=true;cancel.Enabled=false;UpdateWriteButtons();await ScanPlatformPowerAsync();}
  };
 }
 private void RefreshPcfGpuTest(){if(!_platformScans.TryGetValue(PcfPowerFields.ACMaxGPULimit,out var device))return;_refreshPcfBaseline?.Invoke(device);_pcfGpuTest=PcfGpuTester.Load(device);if(_pcfGpuTest==null)return;var c=_platformControls[PcfPowerFields.ACMaxGPULimit];var positive=_pcfGpuTest.Accepted.Where(w=>w>0).ToArray();c.Status.Text=_pcfGpuTest.Describe();if(positive.Length==0)return;c.Input.Minimum=0;c.Input.Maximum=positive.Max();c.Input.Minimum=positive.Min();c.Input.Value=Math.Clamp(device.CurrentMw/1000m,c.Input.Minimum,c.Input.Maximum);c.Input.Enabled=true;c.Apply.Enabled=_pcfGpuTest.Accepted.Contains((int)c.Input.Value);}
}
