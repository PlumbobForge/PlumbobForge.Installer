using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlumbobForge.Installer.ViewModels;

namespace PlumbobForge.Installer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is InstallerViewModel vm)
        {
            vm.RequestBrowseFolder += OnRequestBrowseFolderAsync;
            vm.RequestClose += Close;
        }
    }

    private async Task<string?> OnRequestBrowseFolderAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel?.StorageProvider != null)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select PlumbobForge Installation Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                return folders[0].Path.LocalPath;
            }
        }

        return null;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InstallerViewModel vm)
        {
            if (vm.CurrentStep == InstallStep.Installing)
            {
                if (!vm.IsCancelling)
                {
                    vm.RequestCloseOrCancel();
                }
                return;
            }
        }
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is InstallerViewModel vm && vm.CurrentStep == InstallStep.Installing && !vm.IsCancelling)
        {
            e.Cancel = true;
            vm.RequestCloseOrCancel();
            return;
        }
        base.OnClosing(e);
    }
}
