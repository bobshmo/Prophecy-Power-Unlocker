using VictusPowerUnlockGui;

namespace NvpwrControlBlackwell;
internal sealed partial class MainForm
{
    private CancellationTokenSource? _loadObservationCancel;
    private void AddPowerRecoveryControls(FlowLayoutPanel root)
    {
        var card = Card(330, true); card.Dock = DockStyle.None;
        var title = new Label { Text = "GPU recovery & loaded verification", AutoSize = true, Location = new Point(U(16), U(14)) };
        var recover = ButtonBase("recoverGpuPower", U(185)); recover.Text = "Recover GPU power"; recover.Location = new Point(U(16), U(50));
        var release = ButtonBase("releasePcf", U(185)); release.Text = "Release PCF overrides"; release.Location = new Point(U(215), U(50));
        var verify = ButtonBase("verifyGpuLoad", U(175)); verify.Text = "Verify under load"; verify.Location = new Point(U(414), U(50));
        var cancel = ButtonBase("cancelGpuLoad", U(100)); cancel.Text = "Cancel"; cancel.Enabled = false; cancel.Location = new Point(U(603), U(50));
        var actions = new FlowLayoutPanel { Location = new Point(U(16), U(50)), Size = new Size(U(900), U(90)), WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.AddRange(new Control[] { recover, release, verify, cancel });
        var setTpp = new CheckBox { Text = "Use selected TPP (W)", AutoSize = true, Location = new Point(U(16), U(143)) };
        var tpp = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Enabled = false, Location = new Point(U(215), U(139)), Width = U(100) };
        setTpp.CheckedChanged += (_, _) => { tpp.Enabled = setTpp.Checked; if (setTpp.Checked && _platformScans.TryGetValue(NvAPIWrapper.GPU.PcfPowerFields.ACTargetTPPLimit, out var scan)) tpp.Value = Math.Clamp(scan.CurrentMw / 1000m, tpp.Minimum, tpp.Maximum); };
        var report = new Label { Text = "Recovery prepares HP GPU state, establishes the selected TPP before PCF and the saved CURRENT request. TPP must leave headroom above the GPU target. If unchecked, existing TPP is retained. CPU settings and overclock are retained. Stop workloads before recovery. Start your usual GPU workload before Verify under load; observation changes no settings.", Location = new Point(U(16), U(185)), Size = new Size(U(900), U(112)) };
        card.Resize += (_, _) => { actions.Width = report.Width = Math.Max(U(220), card.ClientSize.Width - U(32)); };
        card.Controls.AddRange(new Control[] { title, actions, setTpp, tpp, report }); _responsiveWide.Add(card); root.Controls.Add(card);
        recover.Click += async (_, _) =>
        {
            if (_busy) return;
            int target = _settings.CurrentSelection > 0 ? _settings.CurrentSelection : _currentSlider.Value;
            int? budget = setTpp.Checked ? (int)tpp.Value : null;
            await RunOperationTask(() => PlatformPowerTransaction.RecoverCurrent(target, budget), true);
            await ScanPlatformPowerAsync();
        };
        release.Click += async (_, _) =>
        {
            if (_busy) return;
            if (MessageBox.Show(this, "Release the PCF overrides and return its power fields to the OEM defaults? Stop workloads first. The saved NVIDIA MAX override is retained.", "Release PCF overrides", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await RunOperationTask(PlatformPowerTransaction.ReleasePcf, true);
            await ScanPlatformPowerAsync();
        };
        cancel.Click += (_, _) => _loadObservationCancel?.Cancel();
        verify.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true; UpdateWriteButtons(); recover.Enabled = release.Enabled = verify.Enabled = false; cancel.Enabled = true;
            _loadObservationCancel = new CancellationTokenSource();
            try
            {
                var result = await Task.Run(() => GpuLoadVerification.Observe(_loadObservationCancel.Token,
                    n => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => report.Text = $"Observing GPU draw: {n}/15 samples. Keep the same workload running.")); }));
                report.Text = result.Describe();
            }
            catch (OperationCanceledException) { report.Text = "Observation cancelled. No power setting was changed."; }
            catch (Exception ex) { report.Text = "Observation unavailable: " + SafeError.Describe(ex); }
            finally { _loadObservationCancel.Dispose(); _loadObservationCancel = null; _busy = false; recover.Enabled = release.Enabled = verify.Enabled = true; cancel.Enabled = false; UpdateWriteButtons(); }
        };
    }
}

