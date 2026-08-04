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
    private readonly Button register = new() { Content = "Crear cuenta" };
    private readonly Button recover = new() { Content = "Olvidé mi contraseña" };
    private readonly TextBlock status = new() { Foreground = Brush.Parse("#B91C1C"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly Action authenticated;

    public LoginWindow(Action authenticated)
    {
        this.authenticated = authenticated;
        Title = "ARES · Iniciar sesión"; Width = 430; Height = 390; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new Border { Background = Brush.Parse("#F1F5F9"), Padding = new Thickness(44), Child = new StackPanel { Spacing = 14, Children = {
            new TextBlock { Text = "🛡  ARES", FontSize = 30, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = "Centro de Control", FontSize = 16, Foreground = Brush.Parse("#64748B"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,18) },
            email, password, login, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Children = { register, recover } }, status
        } } };
        login.Click += async (_, _) => await LoginAsync();
        register.Click += async (_, _) => await RegisterAsync();
        recover.Click += async (_, _) => await RecoverAsync();
        Opened += async (_, _) =>
        {
            status.Foreground = Brush.Parse("#64748B"); status.Text = "Restaurando sesión…";
            try { if (await MacControlAuth.Client.RestoreAsync()) { authenticated(); Close(); return; } } catch { }
            status.Text = "";
        };
    }

    private async Task RegisterAsync()
    {
        var name = new TextBox { PlaceholderText = "Nombre" }; var mail = new TextBox { PlaceholderText = "Correo" };
        var pass = new TextBox { PlaceholderText = "Contraseña (mínimo 8)", PasswordChar = '●' }; var confirm = new TextBox { PlaceholderText = "Repetir contraseña", PasswordChar = '●' };
        var code = new TextBox { PlaceholderText = "Código de invitación", PasswordChar = '●' }; var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var create = new Button { Content = "Crear cuenta", Background = Brush.Parse("#2563EB"), Foreground = Brushes.White };
        var dialog = new Window { Title = "Crear cuenta ARES", Width = 450, Height = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(30), Spacing = 12, Children = { name, mail, pass, confirm, code, create, message } } };
        create.Click += async (_, _) =>
        {
            if (pass.Text != confirm.Text) { message.Text = "Las contraseñas no coinciden."; return; }
            create.IsEnabled = false;
            try { var result = await MacControlAuth.Client.RegisterAsync(name.Text?.Trim() ?? "", mail.Text?.Trim() ?? "", pass.Text ?? "", code.Text ?? ""); message.Text = result.Message; if (result.Success) message.Foreground = Brush.Parse("#15803D"); }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { create.IsEnabled = true; }
        };
        await dialog.ShowDialog(this);
    }

    private async Task RecoverAsync()
    {
        var mail = new TextBox { Text = email.Text, PlaceholderText = "Correo" }; var send = new Button { Content = "Enviar enlace" }; var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new Window { Title = "Recuperar contraseña", Width = 430, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(26), Spacing = 12, Children = { new TextBlock { Text = "Te enviaremos un enlace para cambiar la contraseña." }, mail, send, message } } };
        send.Click += async (_, _) => { try { await MacControlAuth.Client.RecoverAsync(mail.Text?.Trim() ?? ""); message.Text = "Si el correo existe, recibirás un enlace."; } catch (Exception ex) { message.Text = ex.Message; } };
        await dialog.ShowDialog(this);
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
