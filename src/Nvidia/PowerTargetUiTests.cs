namespace NvpwrControlBlackwell;
internal sealed partial class MainForm
{
    internal static void TestPowerTargetRetention()
    {
        // Preview has fresh in-memory settings and no polling/hardware initialization.
        using var form = new MainForm(preview: true);
        var profile = GpuProfiles.All().First(p => p.Id == "5060");
        form._compat.Profile = profile;
        form._settings.MaxSelection = 140;
        form._settings.CurrentSelection = 140;
        form.PopulatePowerTargets(profile, 140);
        form.UpdatePowerUi(new PowerState { CurrentW = 115, MaxW = 115 }, 140);
        if (form._currentSlider.Value != 115 || form._settings.CurrentSelection != 140 || form._settings.MaxSelection != 140)
            throw new Exception("A driver power-policy drop overwrote the user's saved recovery targets.");
        form.PopulatePowerTargets(profile, 115);
        if (form._settings.CurrentSelection != 140)
            throw new Exception("Refreshing the GPU profile replaced the saved CURRENT with its clamped display value.");
        form.UpdatePowerUi(new PowerState { CurrentW = 140, MaxW = 140 }, 140);
        if (form._currentSlider.Value != 140)
            throw new Exception("Recovered live MAX did not restore the saved target display.");
        form._currentSlider.Value = 80;
        if (form._settings.CurrentSelection != 80)
            throw new Exception("User CURRENT edits were lost while suppressing programmatic display updates.");
    }
}
