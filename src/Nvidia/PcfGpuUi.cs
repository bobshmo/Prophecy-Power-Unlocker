using NvAPIWrapper.GPU;
using VictusPowerUnlockGui;
namespace NvpwrControlBlackwell;
internal sealed partial class MainForm
{
 private PcfGpuTestResult? _pcfGpuTest;
 private void AddPcfGpuTools(FlowLayoutPanel root)
 {
  var card=Card(215,true);card.Dock=DockStyle.None;
  var toggle=new CheckBox{Text="ACMaxGPULimit — PCF Max (HP unlock)",AutoSize=true,Location=new Point(U(16),U(16)),Enabled=false};
  var detect=ButtonBase("scanPcfBaseline",U(190));detect.Text="Detect / enable HP unlock";detect.Location=new Point(U(16),U(52));
  var test=ButtonBase("testPcf",U(185));test.Text="Test all PCF GPU values";test.Location=new Point(U(220),U(52));
  var cancel=ButtonBase("cancelPcf",U(100));cancel.Text="Cancel";cancel.Enabled=false;cancel.Location=new Point(U(420),U(52));
  var report=new Label{Text="Restores the working baseline toggle without requiring BIOS range metadata. The tester checks every whole-watt target from 0 through the firmware maximum, or a profile test ceiling if no maximum is advertised. Close games and other power-control apps first.",Location=new Point(U(16),U(105)),Size=new Size(U(900),U(95))};
  card.Resize+=(_,_)=>report.Width=Math.Max(U(220),card.ClientSize.Width-U(32));card.Controls.AddRange(new Control[]{toggle,detect,test,cancel,report});_responsiveWide.Add(card);root.Controls.Add(card);
  PcfDetection? detected=null;bool updating=false;CancellationTokenSource? cancellation=null;
  void UpdateToggle(PcfDetection d){updating=true;toggle.Checked=d.CurrentMw==(uint)d.StockW*1000;toggle.Enabled=true;updating=false;}
  detect.Click+=async(_,_)=>{if(_busy)return;_busy=true;detect.Enabled=false;toggle.Enabled=false;UpdateWriteButtons();try{
   detected=await Task.Run(PcfUnlock.Scan);report.Text=detected.Message;
   bool enable=PcfUnlock.ShouldAutoEnable(detected.Hp,detected.CurrentMw,detected.StockW);
   if(!detected.Hp&&detected.Restricted)enable=MessageBox.Show(this,$"Enable ACMaxGPULimit baseline {detected.StockW} W on this non-HP board?","Enable PCF baseline",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
   if(enable){report.Text=await Task.Run(()=>PcfUnlock.Apply(detected,true));detected=await Task.Run(PcfUnlock.Scan);}
   UpdateToggle(detected);
  }catch(Exception ex){report.Text=ex.Message;}finally{_busy=false;detect.Enabled=true;UpdateWriteButtons();}};
  toggle.CheckedChanged+=async(_,_)=>{if(updating)return;if(_busy||detected==null){if(detected!=null)UpdateToggle(detected);return;}bool enable=toggle.Checked;
   if(enable&&!detected.Hp&&MessageBox.Show(this,"Enable the PCF baseline on this non-HP board?","Enable PCF baseline",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){UpdateToggle(detected);return;}
   _busy=true;toggle.Enabled=false;UpdateWriteButtons();try{report.Text=await Task.Run(()=>PcfUnlock.Apply(detected,enable));}catch(Exception ex){report.Text=ex.Message;}finally{try{detected=await Task.Run(PcfUnlock.Scan);UpdateToggle(detected);}catch{toggle.Enabled=false;}_busy=false;UpdateWriteButtons();}
  };
  cancel.Click+=(_,_)=>cancellation?.Cancel();
  test.Click+=async(_,_)=>{if(_busy)return;
   if(MessageBox.Show(this,"Test every whole-watt ACMaxGPULimit target from zero? Zero can have special controller semantics. The original PCF snapshot will be restored. Accepted readback does not establish safe operating power. Close games and other power-control apps.","Confirm PCF GPU tester",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   _busy=true;test.Enabled=false;detect.Enabled=false;toggle.Enabled=false;cancel.Enabled=true;UpdateWriteButtons();cancellation=new CancellationTokenSource();
   try{var device=await Task.Run(()=>TppControl.Scan(PcfPowerFields.ACMaxGPULimit));_pcfGpuTest=await Task.Run(()=>PcfGpuTester.Test(device,cancellation.Token,w=>BeginInvoke(new Action(()=>report.Text=$"Testing ACMaxGPULimit {w} W; original PCF snapshot will be restored."))));report.Text=_pcfGpuTest.Describe();}
   catch(OperationCanceledException){report.Text="Cancelled; original PCF snapshot restored.";}catch(Exception ex){report.Text="Test stopped: "+ex.Message;}
   finally{cancellation.Dispose();cancellation=null;_busy=false;test.Enabled=true;detect.Enabled=true;cancel.Enabled=false;UpdateWriteButtons();await ScanPlatformPowerAsync();}
  };
 }
 private void RefreshPcfGpuTest(){if(!_platformScans.TryGetValue(PcfPowerFields.ACMaxGPULimit,out var device))return;_pcfGpuTest=PcfGpuTester.Load(device);if(_pcfGpuTest==null)return;var c=_platformControls[PcfPowerFields.ACMaxGPULimit];var positive=_pcfGpuTest.Accepted.Where(w=>w>0).ToArray();c.Status.Text=_pcfGpuTest.Describe();if(positive.Length==0)return;c.Input.Minimum=0;c.Input.Maximum=positive.Max();c.Input.Minimum=positive.Min();c.Input.Value=Math.Clamp(device.CurrentMw/1000m,c.Input.Minimum,c.Input.Maximum);c.Input.Enabled=true;c.Apply.Enabled=_pcfGpuTest.Accepted.Contains((int)c.Input.Value);}
}
