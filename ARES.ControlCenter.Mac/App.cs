using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ARES.ControlCenter.Mac;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var login = new LoginWindow(() =>
            {
                var main = new MainWindow();
                desktop.MainWindow = main;
                main.Show();
            });
            desktop.MainWindow = login;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
