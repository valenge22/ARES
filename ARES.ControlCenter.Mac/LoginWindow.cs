using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ARES.ControlCenter.Mac;

internal sealed class LoginWindow : Window
{
    private readonly TextBox email = new() { PlaceholderText = "Correo electrónico" };
    private readonly TextBox password = new() { PlaceholderText = "Contraseña", PasswordChar = '●' };
    private readonly Button login = new() { Content = "Iniciar sesión", HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 11), Background = Brush.Parse("#2563EB"), Foreground = Brushes.White };
    private readonly TextBlock status = new() { Foreground = Brush.Parse("#B91C1C"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly Action authenticated;

    public LoginWindow(Action authenticated)
    {
        this.authenticated = authenticated;
        Title = "ARES · Iniciar sesión"; Width = 430; Height = 390; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new Border { Background = Brush.Parse("#F1F5F9"), Padding = new Thickness(44), Child = new StackPanel { Spacing = 14, Children = {
            new TextBlock { Text = "🛡  ARES", FontSize = 30, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = "Centro de Control", FontSize = 16, Foreground = Brush.Parse("#64748B"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,18) },
            email, password, login, status
        } } };
        login.Click += async (_, _) => await LoginAsync();
        Opened += async (_, _) =>
        {
            status.Foreground = Brush.Parse("#64748B"); status.Text = "Restaurando sesión…";
            try { if (await MacControlAuth.Client.RestoreAsync()) { authenticated(); Close(); return; } } catch { }
            status.Text = "";
        };
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrEmpty(password.Text)) { status.Text = "Ingresá correo y contraseña."; return; }
        login.IsEnabled = false; status.Foreground = Brush.Parse("#64748B"); status.Text = "Verificando…";
        try
        {
            if (!await MacControlAuth.Client.LoginAsync(email.Text.Trim(), password.Text))
            { status.Foreground = Brush.Parse("#B91C1C"); status.Text = "Correo, contraseña o permisos inválidos."; return; }
            authenticated(); Close();
        }
        catch (Exception ex) { status.Foreground = Brush.Parse("#B91C1C"); status.Text = $"No se pudo conectar: {ex.Message}"; }
        finally { login.IsEnabled = true; password.Text = ""; }
    }
}
