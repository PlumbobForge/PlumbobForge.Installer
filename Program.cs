using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Fonts.Inter;
using PlumbobForge.Installer.Services;
using PlumbobForge.Installer.Shared;
using PlumbobForge.Installer.Views;

namespace PlumbobForge.Installer;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var isSilent = args.Any(a => string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a, "/SILENT", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));

        if (isSilent)
        {
            var exitCode = RunSilentInstallAsync(args).GetAwaiter().GetResult();
            Environment.Exit(exitCode);
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static async Task<int> RunSilentInstallAsync(string[] args)
    {
        try
        {
            var targetDir = InstallerConstants.DefaultInstallDirectory;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
                {
                    targetDir = arg.Substring(5).Trim('"');
                }
                else if (arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                {
                    targetDir = arg.Substring(6).Trim('"');
                }
                else if ((string.Equals(arg, "/DIR", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(arg, "--dir", StringComparison.OrdinalIgnoreCase)) &&
                         i + 1 < args.Length)
                {
                    targetDir = args[++i].Trim('"');
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

            var result = await InstallerService.InstallAsync(options);
            return result == InstallResult.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PlumbobForge Setup Error] {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
