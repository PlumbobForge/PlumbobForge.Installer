using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Installer.Services;

public enum InstallResult
{
    Success,
    Cancelled,
    Failed
}

public static class InstallerService
{
    public static async Task<InstallResult> InstallAsync(
        InstallationOptions options,
        IProgress<(double Progress, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targetDir = PathSafety.SanitizeInstallDirectory(options.InstallDirectory);
        if (PathSafety.IsForbiddenDirectory(targetDir))
        {
            progress?.Report((100, $"Cannot install directly into protected system directory '{targetDir}'."));
            return InstallResult.Failed;
        }

        bool targetDirExistedBefore = Directory.Exists(targetDir);
        var createdFiles = new List<string>();

        try
        {
            // Step 1: Terminate running instances
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((5, "Checking and stopping running PlumbobForge processes..."));
            await ProcessManager.TerminateRunningAppInstancesAsync(targetDir);

            // Step 2: Ensure target directory exists
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Step 3: Fast sequential payload extraction
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((10, "Extracting application files..."));
            await ExtractPayloadAsync(targetDir, createdFiles, progress, 10, 85, cancellationToken);

            // Step 4: Cleanup legacy Electron version if requested
            cancellationToken.ThrowIfCancellationRequested();
            if (options.CleanupLegacyElectron)
            {
                progress?.Report((88, "Cleaning legacy Electron version files..."));
                ElectronMigrator.CleanupLegacyElectronFiles(status =>
                {
                    progress?.Report((89, status));
                });
            }

            // Step 5: Create Shortcuts
            cancellationToken.ThrowIfCancellationRequested();
            var exePath = Path.Combine(targetDir, InstallerConstants.ExecutableName);
            if (options.CreateDesktopShortcut)
            {
                progress?.Report((92, "Creating Desktop shortcut..."));
                ShortcutManager.CreateShortcut(
                    InstallerConstants.DesktopShortcutPath,
                    exePath,
                    targetDir,
                    "PlumbobForge - Sims 3 CC Manager & Cache Builder"
                );
            }

            if (options.CreateStartMenuShortcut)
            {
                progress?.Report((95, "Creating Start Menu shortcut..."));
                ShortcutManager.CreateShortcut(
                    InstallerConstants.StartMenuShortcutPath,
                    exePath,
                    targetDir,
                    "PlumbobForge - Sims 3 CC Manager & Cache Builder"
                );
            }

            // Step 6: Register Windows Uninstall Registry Key
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((98, "Registering Windows application entries..."));
            RegistryManager.RegisterInstallation(targetDir);

            // Step 7: Launch if requested
            if (options.LaunchAfterInstall && File.Exists(exePath))
            {
                progress?.Report((99, "Launching PlumbobForge..."));
                LaunchApplication(exePath);
            }

            progress?.Report((100, "Installation completed successfully!"));
            return InstallResult.Success;
        }
        catch (OperationCanceledException)
        {
            progress?.Report((0, "Cancelling installation and reverting changes..."));
            RollbackInstallation(targetDir, createdFiles, targetDirExistedBefore);
            return InstallResult.Cancelled;
        }
        catch (Exception ex)
        {
            progress?.Report((100, $"Installation failed: {ex.Message}"));
            RollbackInstallation(targetDir, createdFiles, targetDirExistedBefore);
            return InstallResult.Failed;
        }
    }

    private static void RollbackInstallation(string targetDir, List<string> createdFiles, bool targetDirExistedBefore)
    {
        try
        {
            // 1. Delete all extracted files
            foreach (var file in createdFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }

            // 2. Remove shortcuts
            try { ShortcutManager.DeleteShortcut(InstallerConstants.DesktopShortcutPath); } catch { }
            try { ShortcutManager.DeleteShortcut(InstallerConstants.StartMenuShortcutPath); } catch { }

            // 3. Remove registry entry
            try { RegistryManager.UnregisterInstallation(); } catch { }

            // 4. Remove manifest
            try
            {
                var manifestPath = Path.Combine(targetDir, InstallerConstants.InstallManifestFileName);
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
            }
            catch { }

            // 5. Clean up newly created directories
            if (!targetDirExistedBefore && Directory.Exists(targetDir))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(targetDir).Any())
                    {
                        Directory.Delete(targetDir, recursive: true);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Rollback] Exception during rollback: {ex.Message}");
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
            Debug.WriteLine($"[InstallerService] Failed to launch application: {ex.Message}");
        }
    }

    private static async Task ExtractPayloadAsync(
        string targetDir,
        List<string> createdFiles,
        IProgress<(double Progress, string Status)>? progress,
        double startPercentage,
        double endPercentage,
        CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        string? matchedResource = resourceNames.FirstOrDefault(r => r.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                               ?? resourceNames.FirstOrDefault(r => r.EndsWith("payload.7z", StringComparison.OrdinalIgnoreCase));

        Stream? payloadStream = null;

        if (matchedResource != null)
        {
            payloadStream = assembly.GetManifestResourceStream(matchedResource);
        }

        // Fallback: Check if payload.zip or payload.7z exists in the same folder as setup.exe
        if (payloadStream == null)
        {
            var localZip = Path.Combine(AppContext.BaseDirectory, "payload.zip");
            var local7z = Path.Combine(AppContext.BaseDirectory, "payload.7z");

            if (File.Exists(localZip))
            {
                payloadStream = File.OpenRead(localZip);
            }
            else if (File.Exists(local7z))
            {
                payloadStream = File.OpenRead(local7z);
            }
        }

        if (payloadStream == null)
        {
            // Dev stub if no payload present
            var stubExe = Path.Combine(targetDir, InstallerConstants.ExecutableName);
            if (!File.Exists(stubExe))
            {
                await File.WriteAllTextAsync(stubExe, "PlumbobForge Development Stub Executable", cancellationToken);
                createdFiles.Add(stubExe);
            }
            await InstallManifest.SaveAsync(targetDir, [InstallerConstants.ExecutableName, InstallerConstants.InstallManifestFileName]);
            return;
        }

        var installedFiles = new List<string>();
        var normalizedTarget = Path.GetFullPath(targetDir);

        await Task.Run(() =>
        {
            using (payloadStream)
            using (var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
            {
                var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                var total = entries.Count;
                if (total == 0) return;

                var stopwatch = Stopwatch.StartNew();

                for (int i = 0; i < total; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = entries[i];
                    var destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

                    // Guard against Zip Slip
                    if (!destinationPath.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var entryDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                    createdFiles.Add(destinationPath);
                    installedFiles.Add(entry.FullName.Replace('\\', '/'));

                    // Throttle UI updates to ~40ms to keep extraction at full native speed
                    if (stopwatch.ElapsedMilliseconds > 40 || i == total - 1)
                    {
                        stopwatch.Restart();
                        double currentPct = startPercentage + ((double)(i + 1) / total) * (endPercentage - startPercentage);
                        progress?.Report((currentPct, $"Extracting {entry.Name}..."));
                    }
                }
            }
        }, cancellationToken);

        if (!installedFiles.Contains(InstallerConstants.InstallManifestFileName, StringComparer.OrdinalIgnoreCase))
        {
            installedFiles.Add(InstallerConstants.InstallManifestFileName);
        }

        await InstallManifest.SaveAsync(targetDir, installedFiles);
        createdFiles.Add(Path.Combine(targetDir, InstallerConstants.InstallManifestFileName));
    }
}
