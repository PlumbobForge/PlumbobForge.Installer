using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using PlumbobForge.Installer.Services;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Installer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var isSilent = args.Any(a => string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a, "/SILENT", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        if (isSilent)
        {
            return RunSilentInstall(args).GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static async Task<int> RunSilentInstall(string[] args)
    {
        try
        {
            var dirArg = args.FirstOrDefault(a => a.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase) ||
                                                  a.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase));
            string targetDir;
            if (dirArg != null)
            {
                targetDir = dirArg.Substring(dirArg.IndexOf('=') + 1).Trim('"');
            }
            else
            {
                // Auto-detect existing custom install directory from registry for seamless in-app / silent updates
                var existingDir = RegistryManager.GetInstalledLocation();
                if (!string.IsNullOrWhiteSpace(existingDir) && (Directory.Exists(existingDir) || File.Exists(Path.Combine(existingDir, InstallerConstants.ExecutableName))))
                {
                    targetDir = existingDir;
                }
                else
                {
                    targetDir = InstallerConstants.DefaultInstallDirectory;
                }
            }

            var launchAfter = args.Any(a => string.Equals(a, "/RUN", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "--run", StringComparison.OrdinalIgnoreCase));

            var noDesktop = args.Any(a => string.Equals(a, "/NODESKTOP", StringComparison.OrdinalIgnoreCase));
            var noStartMenu = args.Any(a => string.Equals(a, "/NOSTARTMENU", StringComparison.OrdinalIgnoreCase));
            var noCleanup = args.Any(a => string.Equals(a, "/NOCLEANUP", StringComparison.OrdinalIgnoreCase));

            var options = new InstallationOptions
            {
                InstallDirectory = targetDir,
                CreateDesktopShortcut = !noDesktop,
                CreateStartMenuShortcut = !noStartMenu,
                CleanupLegacyElectron = !noCleanup && ElectronMigrator.HasLegacyElectronInstallation(),
                LaunchAfterInstall = launchAfter
            };

            var success = await InstallerService.InstallAsync(options);
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PlumbobForge Setup Error] {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
