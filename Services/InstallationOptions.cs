namespace PlumbobForge.Installer.Services;

public class InstallationOptions
{
    public string InstallDirectory { get; set; } = string.Empty;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool CleanupLegacyElectron { get; set; }
    public bool LaunchAfterInstall { get; set; }
}
