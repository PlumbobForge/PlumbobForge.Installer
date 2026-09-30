using System;
using System.IO;
using System.Threading;
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
    [NotifyPropertyChangedFor(nameof(IsWelcomeStep))]
    [NotifyPropertyChangedFor(nameof(IsInstallingStep))]
    [NotifyPropertyChangedFor(nameof(IsCompletedStep))]
    [NotifyPropertyChangedFor(nameof(IsFailedStep))]
    private InstallStep _currentStep = InstallStep.Welcome;

    public bool IsWelcomeStep => CurrentStep == InstallStep.Welcome;
    public bool IsInstallingStep => CurrentStep == InstallStep.Installing;
    public bool IsCompletedStep => CurrentStep == InstallStep.Completed;
    public bool IsFailedStep => CurrentStep == InstallStep.Failed;

    [ObservableProperty]
    private string _installDirectory = string.Empty;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    [ObservableProperty]
    private bool _cleanupLegacyElectron = false;

    [ObservableProperty]
    private bool _hasLegacyElectron = false;

    [ObservableProperty]
    private bool _isUpgrade = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private double _progressPercentage = 0;

    public string ProgressPercentText => $"{(int)Math.Round(ProgressPercentage)}%";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _titleText = "Welcome to PlumbobForge";

    [ObservableProperty]
    private string _installButtonText = "Install";

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private bool _showCancelConfirmation = false;

    [ObservableProperty]
    private bool _isCancelling = false;

    private CancellationTokenSource? _installCts;
    private bool _closeRequestedAfterRollback = false;

    public Func<Task<string?>>? RequestBrowseFolder { get; set; }
    public Action? RequestClose { get; set; }

    public InstallerViewModel()
    {
        var existingLocation = RegistryManager.GetInstalledLocation();
        if (!string.IsNullOrWhiteSpace(existingLocation) && (Directory.Exists(existingLocation) || File.Exists(Path.Combine(existingLocation, InstallerConstants.ExecutableName))))
        {
            if (PathSafety.IsForbiddenDirectory(existingLocation) || !PathSafety.IsDedicatedAppDirectory(existingLocation))
            {
                InstallDirectory = PathSafety.SanitizeInstallDirectory(existingLocation);
            }
            else
            {
                InstallDirectory = existingLocation;
            }
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

    public void RequestCloseOrCancel()
    {
        if (CurrentStep == InstallStep.Installing && !IsCancelling)
        {
            _closeRequestedAfterRollback = true;
            ShowCancelConfirmation = true;
        }
        else if (CurrentStep != InstallStep.Installing)
        {
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    private void RequestCancel()
    {
        if (CurrentStep == InstallStep.Installing && !IsCancelling)
        {
            _closeRequestedAfterRollback = false;
            ShowCancelConfirmation = true;
        }
    }

    [RelayCommand]
    private void DismissCancelConfirmation()
    {
        ShowCancelConfirmation = false;
        _closeRequestedAfterRollback = false;
    }

    [RelayCommand]
    private void ConfirmCancelInstallation()
    {
        ShowCancelConfirmation = false;
        IsCancelling = true;
        StatusMessage = "Cancelling installation and rolling back changes...";
        _installCts?.Cancel();
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (RequestBrowseFolder != null)
        {
            var selectedPath = await RequestBrowseFolder.Invoke();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                InstallDirectory = PathSafety.SanitizeInstallDirectory(selectedPath);
            }
        }
    }

    [RelayCommand]
    private async Task StartInstallAsync()
    {
        if (IsBusy) return;

        var targetDir = PathSafety.SanitizeInstallDirectory(InstallDirectory);
        if (PathSafety.IsForbiddenDirectory(targetDir))
        {
            StatusMessage = "Cannot install directly into a protected system folder. Please choose a dedicated directory.";
            CurrentStep = InstallStep.Failed;
            return;
        }

        InstallDirectory = targetDir;
        IsBusy = true;
        IsCancelling = false;
        _closeRequestedAfterRollback = false;
        _installCts = new CancellationTokenSource();
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

        var result = await InstallerService.InstallAsync(options, progressReporter, _installCts.Token);
        IsBusy = false;
        IsCancelling = false;

        if (result == InstallResult.Success)
        {
            CurrentStep = InstallStep.Completed;
            StatusMessage = IsUpgrade
                ? "PlumbobForge has been updated successfully!"
                : "PlumbobForge has been installed successfully!";
        }
        else if (result == InstallResult.Cancelled)
        {
            if (_closeRequestedAfterRollback)
            {
                RequestClose?.Invoke();
            }
            else
            {
                CurrentStep = InstallStep.Welcome;
                ProgressPercentage = 0;
                StatusMessage = "Installation was cancelled. All changes have been reverted.";
            }
        }
        else
        {
            CurrentStep = InstallStep.Failed;
            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("Extracting"))
            {
                StatusMessage = "Installation encountered an unexpected error.";
            }
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
