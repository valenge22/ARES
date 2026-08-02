using ARES.Shared.Modelos;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ARES.ControlCenter.Mac;

public sealed class MainWindow : Window
{
    private readonly MacSettings settings = MacSettings.Load();
    private readonly AresApiClient api;
    private readonly StackPanel content = new() { Spacing = 10 };
    private readonly TextBlock status = new() { Foreground = Brush.Parse("#94A3B8") };
    private readonly TextBlock title = new() { Text = "Equipos", FontSize = 28, FontWeight = FontWeight.Bold };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool loading;

    public MainWindow()
    {
        api = new(settings);
        Title = "ARES Centro de Control";
        Width = 1120; Height = 720; MinWidth = 850; MinHeight = 560;
        Background = Brush.Parse("#F1F5F9");

        var nav = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        nav.Children.Add(new TextBlock { Text = "🛡  ARES", FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(4, 8, 4, 25) });
        nav.Children.Add(NavButton("💻  Equipos", () => ShowAgentsAsync()));
        nav.Children.Add(NavButton("📋  Registros", ShowAuditAsync));
        nav.Children.Add(NavButton("⚙  Configuración", ShowSettingsAsync));

        var refresh = new Button { Content = "↻ Actualizar", Padding = new Thickness(18, 9), Background = Brush.Parse("#2563EB"), Foreground = Brushes.White };
        refresh.Click += async (_, _) => await ShowAgentsAsync();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new StackPanel { Children = { title, status } });
        Grid.SetColumn(refresh, 1); header.Children.Add(refresh);

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(28) };
        main.Children.Add(header);
        var scroll = new ScrollViewer { Content = content };
        Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        root.Children.Add(new Border { Background = Brush.Parse("#0F172A"), Child = nav }); Grid.SetColumn(main, 1); root.Children.Add(main); Content = root;

        timer.Tick += async (_, _) => await ShowAgentsAsync(false);
        timer.Start(); Opened += async (_, _) => await ShowAgentsAsync();
        Closed += (_, _) => timer.Stop();
    }

    private Button NavButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 12), Background = Brushes.Transparent, Foreground = Brush.Parse("#CBD5E1") };
        button.Click += async (_, _) => await action(); return button;
    }

    private async Task ShowAgentsAsync(bool showLoading = true)
    {
        if (loading) return; loading = true;
        try
        {
            title.Text = "Equipos"; if (showLoading) status.Text = "Actualizando…";
            var agents = await api.AgentsAsync(); content.Children.Clear();
            foreach (var agent in agents) content.Children.Add(AgentCard(agent));
            status.Text = $"{agents.Count(a => a.EstaEnLinea)} de {agents.Count} equipos conectados · {DateTime.Now:HH:mm:ss}";
            if (agents.Count == 0) content.Children.Add(new TextBlock { Text = "No hay equipos registrados.", Margin = new Thickness(12), Foreground = Brush.Parse("#64748B") });
        }
        catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        finally { loading = false; }
    }

    private Control AgentCard(AgentStatus agent)
    {
        var state = new TextBlock { Text = agent.EstaEnLinea ? "● En línea" : "● Sin conexión", Foreground = Brush.Parse(agent.EstaEnLinea ? "#16A34A" : "#DC2626"), FontWeight = FontWeight.Bold };
        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(new TextBlock { Text = agent.Equipo, FontSize = 17, FontWeight = FontWeight.Bold });
        details.Children.Add(new TextBlock { Text = $"{agent.Usuario} · {agent.Sistema}", Foreground = Brush.Parse("#64748B") });
        if (agent.SolicitudDesbloqueoPendiente) details.Children.Add(new TextBlock { Text = "🔔 Solicitud de desbloqueo pendiente", Foreground = Brush.Parse("#EA580C"), FontWeight = FontWeight.Bold });
        var button = new Button { Content = agent.BloqueadoAdministrativamente ? "Desbloquear" : "Bloquear", Background = Brush.Parse(agent.BloqueadoAdministrativamente ? "#16A34A" : "#DC2626"), Foreground = Brushes.White, Padding = new Thickness(16, 9) };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await api.RestrictAsync(agent.Id, !agent.BloqueadoAdministrativamente); await ShowAgentsAsync(); } catch (Exception ex) { status.Text = $"Error: {ex.Message}"; } finally { button.IsEnabled = true; } };
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { state, button } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; grid.Children.Add(details); Grid.SetColumn(actions, 1); grid.Children.Add(actions);
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = grid };
    }

    private async Task ShowAuditAsync()
    {
        if (loading) return; loading = true;
        try
        {
            title.Text = "Registros"; status.Text = "Actualizando…"; var events = await api.AuditAsync(); content.Children.Clear();
            foreach (var item in events.Take(500)) content.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Child = new TextBlock { Text = $"{item.FechaUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}  ·  {item.Equipo}\n{item.Tipo}: {item.Detalle}", TextWrapping = TextWrapping.Wrap } });
            status.Text = $"{events.Count} eventos recientes";
        }
        catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        finally { loading = false; }
    }

    private async Task ShowSettingsAsync()
    {
        var url = new TextBox { Text = settings.ServerUrl, PlaceholderText = "Servidor HTTPS" };
        var key = new TextBox { Text = settings.ApiKey, PasswordChar = '●', PlaceholderText = "Clave ARES" };
        var save = new Button { Content = "Guardar", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 9) };
        var dialog = new Window { Title = "Configuración de ARES", Width = 520, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Dirección del servidor" }, url, new TextBlock { Text = "Clave ARES" }, key, save } };
        save.Click += (_, _) => { settings.ServerUrl = url.Text?.Trim() ?? ""; settings.ApiKey = key.Text ?? ""; settings.Save(); api.Update(settings); dialog.Close(); };
        await dialog.ShowDialog(this); await ShowAgentsAsync();
    }
}
