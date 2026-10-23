using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace VsmApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // Files passed on the command line (double-clicked .vsmap, "Open with") are imported on start.
            window.PendingOpenPaths = desktop.Args ?? Array.Empty<string>();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
