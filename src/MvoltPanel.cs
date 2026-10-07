using System.Diagnostics;
using System.Text.Json.Nodes;

namespace VictusPowerUnlockGui;

internal sealed class MvoltPanel : UserControl
{
    private JsonObject? _import;
    private string? _engine;
    private readonly Label _title = new() { Dock = DockStyle.Fill, Text = "No profile imported. Select your own mVolt profile and official mVolt executable." };
    private readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Fill };
    public bool Busy { get; private set; }
    public MvoltPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        _grid.Columns.Add("control", "Control"); _grid.Columns.Add("enabled", "Enabled"); _grid.Columns.Add("values", "Saved values");
        _grid.Columns[2].FillWeight = 200; _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        layout.Controls.Add(_title); layout.Controls.Add(_buttons); layout.Controls.Add(_grid); layout.Controls.Add(_log); Controls.Add(layout);
        AddButton("Select mVolt", SelectEngine);
        AddButton("Import profile", Import);
        AddButton("Apply profile", Apply);
        AddButton("Read tuning", async () => Log(await Execute("--status")));
        AddButton("Open mVolt", () => { RequireEngine(); Process.Start(new ProcessStartInfo(_engine!) { UseShellExecute = true }); return Task.CompletedTask; });
        string bundled = Path.Combine(AppContext.BaseDirectory, "mvolt", "mVolt+.exe");
        if (File.Exists(bundled)) _engine = bundled;
    }
    private Task SelectEngine()
    {
        using var pick = new OpenFileDialog { Title = "Select the official mVolt+.exe", Filter = "mVolt executable|*.exe" };
        if (pick.ShowDialog(this) == DialogResult.OK) { _engine = pick.FileName; Log("Selected mVolt. No settings applied."); }
        return Task.CompletedTask;
    }
    private Task Import()
    {
        using var pick = new OpenFileDialog { Title = "Select your mVolt profile file", Filter = "mVolt profiles|*.json", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mVolt+", "profiles") };
        if (pick.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var doc = JsonNode.Parse(File.ReadAllText(pick.FileName))!.AsObject();
        if (doc["schema"]?.GetValue<string>() != "mvolt.profile.v3" || doc["profiles"] is not JsonArray profiles || profiles.Count == 0)
            throw new InvalidOperationException("Expected a mVolt v3 profile store with at least one saved profile.");
        using var choose = new Form { Text = "Choose profile", ClientSize = new Size(500, 125), StartPosition = FormStartPosition.CenterParent };
        var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 15, Top = 15, Width = 465 };
        foreach (var p in profiles) list.Items.Add(p!["name"]!.GetValue<string>());
        list.SelectedIndex = 0;
        var ok = new Button { Text = "Import", DialogResult = DialogResult.OK, Left = 370, Top = 65, Width = 110 };
        choose.Controls.AddRange([list, ok]); choose.AcceptButton = ok;
        if (choose.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        var profile = profiles[list.SelectedIndex]!;
        _import = new JsonObject { ["source"] = pick.FileName, ["adapter"] = doc["adapter_id"]!.DeepClone(), ["vbios"] = doc["vbios_id"]!.DeepClone(), ["profile"] = profile.DeepClone() };
        _title.Text = "Imported: " + profile["name"]!.GetValue<string>() + "\n" + doc["gpu_name"] + " · VBIOS " + doc["vbios_id"] + "\nApplies only when requested. No startup profile is configured.";
        _grid.Rows.Clear();
        foreach (var entry in profile["controls"]!.AsObject())
        {
            var values = entry.Value!.DeepClone().AsObject();
            bool enabled = values["enabled"]?.GetValue<bool>() ?? false; values.Remove("enabled");
            _grid.Rows.Add(entry.Key.Replace('_', ' '), enabled ? "Yes" : "No", values.ToJsonString());
        }
        Log("Profile imported in memory. Original file unchanged."); return Task.CompletedTask;
    }
    private async Task Apply()
    {
        if (_import == null) throw new InvalidOperationException("Import a profile first.");
        var store = JsonNode.Parse(File.ReadAllText(_import["source"]!.GetValue<string>()))!;
        var profile = _import["profile"]!;
        string id = profile["id"]!.GetValue<string>();
        var saved = store["profiles"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == id);
        if (!JsonNode.DeepEquals(saved, profile)) throw new InvalidOperationException("The saved profile changed. Import it again before applying.");
        string adapter = _import["adapter"]!.GetValue<string>();
        var status = JsonNode.Parse(await Execute("--status", "--gpu-id", adapter))!;
        if (status["adapter_id"]?.GetValue<string>() != adapter || !String.Equals(status["vbios"]?.GetValue<string>(), _import["vbios"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live GPU/VBIOS does not match the imported profile.");
        var controls = profile["controls"]!;
        bool changesPower = profile["full_snapshot"]?.GetValue<bool>() == true || controls["power"]?["enabled"]?.GetValue<bool>() == true || controls["power_cap"]?["enabled"]?.GetValue<bool>() == true;
        Log(await Task.Run(() => {
            using var guard = changesPower ? HpPlatform.BeforeCurrentPower() : null;
            return Execute("--gpu-id", adapter, "--profile", id).GetAwaiter().GetResult();
        }));
        Log(await Execute("--status", "--gpu-id", adapter));
    }
    private void RequireEngine()
    {
        if (String.IsNullOrEmpty(_engine) || !File.Exists(_engine)) throw new InvalidOperationException("Select the official mVolt executable first. The optional setup-mvolt.ps1 downloads the latest release.");
    }
    private async Task<string> Execute(params string[] args)
    {
        RequireEngine();
        var start = new ProcessStartInfo(_engine!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start) ?? throw new InvalidOperationException("Could not start mVolt.");
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); throw new InvalidOperationException("mVolt timed out. Settings may be partially applied; read tuning before retrying."); }
        string text = (await output) + (await error);
        if (p.ExitCode != 0) throw new InvalidOperationException("mVolt failed; settings may be partially applied. " + text);
        return text;
    }
    private void AddButton(string text, Func<Task> action)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 34 };
        b.Click += async (_, _) => { if (Busy) return; Busy = true; _buttons.Enabled = false;
            try { await action(); } catch (Exception ex) { Log("ERROR: " + ex.Message); }
            finally { Busy = false; _buttons.Enabled = true; } };
        _buttons.Controls.Add(b);
    }
    private void Log(string text) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
}
