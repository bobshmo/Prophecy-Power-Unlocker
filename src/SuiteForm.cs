using System.Diagnostics;
using System.Text.Json.Nodes;

namespace VictusPowerUnlockGui;

internal sealed class SuiteForm : Form
{
    private readonly MainForm _victus;
    private readonly NvpwrControlBlackwell.MainForm _nvidia;
    private readonly MvoltPanel _mvolt = new();
    private bool _fanBusy, _updatingFan;
    public SuiteForm(string? captureDirectory = null)
    {
        _victus = new MainForm(quietPreview: captureDirectory != null);
        _nvidia = new NvpwrControlBlackwell.MainForm(preview: captureDirectory != null);
        Text = "Prophecy Power Unlocker";
        ClientSize = new Size(1380, 970); MinimumSize = new Size(1050, 820);
        StartPosition = FormStartPosition.CenterScreen;
        _nvidia.TopLevel=false; _nvidia.FormBorderStyle=FormBorderStyle.None; _nvidia.Dock=DockStyle.Fill; Controls.Add(_nvidia);
        var fanBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(10, 6, 0, 0), BackColor = Color.FromArgb(32, 37, 47), ForeColor = Color.White };
        var fanToggle = new CheckBox { Text = "Fan Max", ForeColor = Color.White, AutoSize = true, Enabled = false, ThreeState = true, CheckState = CheckState.Indeterminate };
        var fanStatus = new Label { Text = "Reading fans…", AutoSize = true, Padding = new Padding(12, 3, 0, 0) };
        var fanRefresh = new Button { Text = "Read fans", AutoSize = true, ForeColor = Color.Black };
        fanBar.Controls.AddRange([fanToggle, fanRefresh, fanStatus]); Controls.Add(fanBar);
        async Task ReadFans()
        {
            if (_fanBusy) return; _fanBusy = true; fanToggle.Enabled = false;
            try
            {
                bool? actual = await Task.Run(HpPlatform.ReadFanMax);
                _updatingFan = true;
                fanToggle.CheckState = actual.HasValue ? (actual.Value ? CheckState.Checked : CheckState.Unchecked) : CheckState.Indeterminate;
                fanStatus.Text = actual.HasValue ? (actual.Value ? "Maximum fans active." : "Fan Max off — firmware controls fan speed.") : "Fan Max state unavailable.";
            }
            catch (Exception ex) { fanStatus.Text = ex.Message; }
            finally { _updatingFan = false; _fanBusy = false; fanToggle.ThreeState = false; fanToggle.Enabled = true; }
        }
        fanToggle.CheckedChanged += async (_, _) =>
        {
            if (_updatingFan || _fanBusy) return;
            bool requested = fanToggle.Checked; _fanBusy = true; fanToggle.Enabled = false;
            try
            {
                await Task.Run(() => HpPlatform.SetFanMax(requested));
                fanStatus.Text = requested ? "Maximum fans active — verified." : "Fan Max off — verified.";
            }
            catch (Exception ex)
            {
                _updatingFan = true; fanToggle.CheckState = CheckState.Indeterminate; _updatingFan = false;
                fanStatus.Text = ex.Message;
            }
            finally { _fanBusy = false; fanToggle.Enabled = true; }
        };
        fanRefresh.Click += async (_, _) => await ReadFans();
        if (captureDirectory == null && HpPlatform.IsVictus) Shown += async (_, _) => await ReadFans();
        else { fanStatus.Text = captureDirectory != null ? "Preview · HPCM precedes GPU CURRENT on HP Victus." : "Fan Max is available on HP Victus only."; fanRefresh.Enabled = false; }
        if (captureDirectory != null || HpPlatform.IsVictus) {
            _victus.TopLevel=false; _victus.FormBorderStyle=FormBorderStyle.None;
            _nvidia.AddUnifiedPage("victus","CPU / HP",_victus); _victus.Show();
        }
        _nvidia.AddUnifiedPage("profiles","Profiles",_mvolt);
        _victus.OpenNvidiaRequested+=()=>_nvidia.SelectUnifiedPage("power");
        _nvidia.Show();
        if(captureDirectory==null)Shown+=async(_,_)=>{
            while(_nvidia.OperationBusy)await Task.Delay(200);
            await _nvidia.DetectPcfAsync();
        };
        if (captureDirectory != null) Shown += async (_, _) =>
        {
            Directory.CreateDirectory(captureDirectory);
            string[] pages={"power","tuning","telemetry","compatibility","victus","profiles"};
            for (int i = 0; i < pages.Length; i++)
            {
                _nvidia.SelectUnifiedPage(pages[i]); await Task.Delay(200);
                using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
                DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(Path.Combine(captureDirectory, $"suite-{i}.png"));
            }
            Close();
        };
        FormClosing += (_, e) =>
        {
            if (_fanBusy || _mvolt.Busy || _victus.OperationBusy || _nvidia.OperationBusy) { e.Cancel = true; return; }
            _nvidia.CloseForSuite(); _victus.Close();
        };
    }
    private static void AddForm(TabControl tabs, string title, Form form)
    {
        var page = new TabPage(title) { AutoScroll = true };
        form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill; page.Controls.Add(form); tabs.TabPages.Add(page); form.Show();
    }
}
