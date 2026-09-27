using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Installer.Services;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Installer.ViewModels;

public enum InstallStep
{
    Welcome,
    Installing,
    Completed,
    Failed
}

public partial class InstallerViewModel : ObservableObject
{
    [ObservableProperty]
    private InstallStep _currentStep = InstallStep.Welcome;

    [ObservableProperty]
    private string _installDirectory = InstallerConstants.DefaultInstallDirectory;

    [ObservableProperty]
    private bool _isUpgrade;

    [ObservableProperty]
    private string _titleText = "Welcome to PlumbobForge";

    [ObservableProperty]
    private string _installButtonText = "Install";

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    [ObservableProperty]
    private bool _cleanupLegacyElectron;

    [ObservableProperty]
    private bool _hasLegacyElectron;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _statusMessage = "Ready to install PlumbobForge.";

    [ObservableProperty]
    private bool _isBusy;

    public event Func<Task<string?>>? RequestBrowseFolder;
    public event Action? RequestClose;

    public InstallerViewModel()
    {
        var existingLocation = RegistryManager.GetInstalledLocation();
        if (!string.IsNullOrWhiteSpace(existingLocation) && (Directory.Exists(existingLocation) || File.Exists(Path.Combine(existingLocation, InstallerConstants.ExecutableName))))
        {
            InstallDirectory = existingLocation;
            IsUpgrade = true;
            TitleText = "Update PlumbobForge";
            InstallButtonText = "Update";
            StatusMessage = "Ready to update PlumbobForge.";
        }
        else
        {
            InstallDirectory = InstallerConstants.DefaultInstallDirectory;
            IsUpgrade = false;
            TitleText = "Welcome to PlumbobForge";
            InstallButtonText = "Install";
            StatusMessage = "Ready to install PlumbobForge.";
        }

        HasLegacyElectron = ElectronMigrator.HasLegacyElectronInstallation();
        CleanupLegacyElectron = HasLegacyElectron;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (RequestBrowseFolder != null)
        {
            var selectedPath = await RequestBrowseFolder.Invoke();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                InstallDirectory = selectedPath;
            }
        }
    }

    [RelayCommand]
    private async Task StartInstallAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        CurrentStep = InstallStep.Installing;

        var options = new InstallationOptions
        {
            InstallDirectory = InstallDirectory,
            CreateDesktopShortcut = CreateDesktopShortcut,
            CreateStartMenuShortcut = CreateStartMenuShortcut,
            CleanupLegacyElectron = CleanupLegacyElectron,
            LaunchAfterInstall = false
        };

        var progressReporter = new Progress<(double Progress, string Status)>(report =>
        {
            ProgressPercentage = report.Progress;
            StatusMessage = report.Status;
        });

        var success = await InstallerService.InstallAsync(options, progressReporter);
        IsBusy = false;

        if (success)
        {
            CurrentStep = InstallStep.Completed;
            StatusMessage = IsUpgrade 
                ? "PlumbobForge has been updated successfully!" 
                : "PlumbobForge has been installed successfully!";
        }
        else
        {
            CurrentStep = InstallStep.Failed;
            StatusMessage = "Installation encountered an unexpected error.";
        }
    }

    [RelayCommand]
    private void LaunchApp()
    {
        var exePath = Path.Combine(InstallDirectory, InstallerConstants.ExecutableName);
        InstallerService.LaunchApplication(exePath);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Finish()
    {
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }
}
