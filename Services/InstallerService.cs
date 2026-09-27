using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Installer.Services;

public static class InstallerService
{
    public static async Task<bool> InstallAsync(InstallationOptions options, IProgress<(double Progress, string Status)>? progress = null)
    {
        try
        {
            var targetDir = string.IsNullOrWhiteSpace(options.InstallDirectory)
                ? InstallerConstants.DefaultInstallDirectory
                : options.InstallDirectory;

            // Step 1: Terminate running instances
            progress?.Report((5, "Checking and stopping running PlumbobForge processes..."));
            await ProcessManager.TerminateRunningAppInstancesAsync(targetDir);

            // Step 2: Ensure target directory exists
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Step 3: Extract payload files
            progress?.Report((10, "Preparing to extract application files..."));
            await ExtractPayloadAsync(targetDir, progress, 10, 75);

            // Step 4: Cleanup legacy Electron version if requested
            if (options.CleanupLegacyElectron)
            {
                progress?.Report((80, "Cleaning legacy Electron version files..."));
                ElectronMigrator.CleanupLegacyElectronFiles(status =>
                {
                    progress?.Report((82, status));
                });
            }

            // Step 5: Create Shortcuts
            var exePath = Path.Combine(targetDir, InstallerConstants.ExecutableName);
            if (options.CreateDesktopShortcut)
            {
                progress?.Report((88, "Creating Desktop shortcut..."));
                ShortcutManager.CreateShortcut(
                    InstallerConstants.DesktopShortcutPath,
                    exePath,
                    targetDir,
                    "PlumbobForge - Sims 3 CC Manager & Cache Builder"
                );
            }

            if (options.CreateStartMenuShortcut)
            {
                progress?.Report((92, "Creating Start Menu shortcut..."));
                ShortcutManager.CreateShortcut(
                    InstallerConstants.StartMenuShortcutPath,
                    exePath,
                    targetDir,
                    "PlumbobForge - Sims 3 CC Manager & Cache Builder"
                );
            }

            // Step 6: Register Windows Uninstall Registry Key
            progress?.Report((96, "Registering Windows application entries..."));
            RegistryManager.RegisterInstallation(targetDir);

            // Step 7: Launch if requested
            if (options.LaunchAfterInstall && File.Exists(exePath))
            {
                progress?.Report((99, "Launching PlumbobForge..."));
                LaunchApplication(exePath);
            }

            progress?.Report((100, "Installation completed successfully!"));
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report((100, $"Installation failed: {ex.Message}"));
            return false;
        }
    }

    public static void LaunchApplication(string? exePath = null)
    {
        try
        {
            var targetExe = exePath;
            if (string.IsNullOrEmpty(targetExe) || !File.Exists(targetExe))
            {
                var dir = RegistryManager.GetInstalledLocation() ?? InstallerConstants.DefaultInstallDirectory;
                targetExe = Path.Combine(dir, InstallerConstants.ExecutableName);
            }

            if (File.Exists(targetExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = Path.GetDirectoryName(targetExe) ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstallerService] Failed to launch application: {ex.Message}");
        }
    }

    private static async Task ExtractPayloadAsync(
        string targetDir,
        IProgress<(double Progress, string Status)>? progress,
        double startPercentage,
        double endPercentage)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

        Stream? zipStream = null;
        if (resourceName != null)
        {
            zipStream = assembly.GetManifestResourceStream(resourceName);
        }

        // Fallback: Check if payload.zip exists in the same folder as setup.exe
        if (zipStream == null)
        {
            var localZip = Path.Combine(AppContext.BaseDirectory, "payload.zip");
            if (File.Exists(localZip))
            {
                zipStream = File.OpenRead(localZip);
            }
        }

        if (zipStream == null)
        {
            // If no payload is embedded, create a stub executable for testing/development if needed
            var stubExe = Path.Combine(targetDir, InstallerConstants.ExecutableName);
            if (!File.Exists(stubExe))
            {
                await File.WriteAllTextAsync(stubExe, "PlumbobForge Development Stub Executable");
            }
            return;
        }

        using (zipStream)
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            var total = entries.Count;
            if (total == 0) return;

            for (int i = 0; i < total; i++)
            {
                var entry = entries[i];
                var destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

                // Guard against Zip Slip
                if (!destinationPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var entryDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                var currentPct = startPercentage + ((double)(i + 1) / total) * (endPercentage - startPercentage);
                progress?.Report((currentPct, $"Extracting {entry.Name}..."));

                await Task.Run(() =>
                {
                    entry.ExtractToFile(destinationPath, overwrite: true);
                });
            }
        }
    }
}
