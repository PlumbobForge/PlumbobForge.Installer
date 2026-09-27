using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlumbobForge.Installer.ViewModels;
using PlumbobForge.Installer.Views;

namespace PlumbobForge.Installer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new InstallerViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
