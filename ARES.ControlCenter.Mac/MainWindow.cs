using ARES.Shared.Modelos;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;

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
        nav.Children.Add(NavButton("📅  Horarios", ShowScheduleAsync));
        nav.Children.Add(NavButton("👤  Sesiones del panel", ShowControlSessionsAsync));
        nav.Children.Add(NavButton("⚙  Configuración", ShowSettingsAsync));

        var refresh = new Button { Content = "↻ Actualizar", Padding = new Thickness(18, 9), Background = Brush.Parse("#2563EB"), Foreground = Brushes.White };
        refresh.Click += async (_, _) => await ShowAgentsAsync();
        var clear = new Button { Content = "Borrar lista", Padding = new Thickness(18, 9), Background = Brush.Parse("#DC2626"), Foreground = Brushes.White };
        clear.Click += async (_, _) =>
        {
            if (!await ConfirmClearAsync()) return;
            try { await api.ClearAgentsAsync(); await ShowAgentsAsync(); }
            catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        };
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { clear, refresh } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new StackPanel { Children = { title, status } });
        Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions);

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
            ControlSessionHeartbeatResponse panelPolicy = await api.HeartbeatControlSessionAsync();
            var agents = await api.AgentsAsync(); content.Children.Clear();
            foreach (var agent in agents) content.Children.Add(AgentCard(agent));
            status.Text = $"{agents.Count(a => a.EstaEnLinea)} de {agents.Count} equipos conectados · {panelPolicy.Activas} paneles activos · {DateTime.Now:HH:mm:ss}";
            if (agents.Count == 0) content.Children.Add(new TextBlock { Text = "No hay equipos registrados.", Margin = new Thickness(12), Foreground = Brush.Parse("#64748B") });
        }
        catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        finally { loading = false; }
    }

    private Control AgentCard(AgentStatus agent)
    {
        var state = new TextBlock { Text = agent.EstaEnLinea ? "● En línea" : "● Sin conexión", Foreground = Brush.Parse(agent.EstaEnLinea ? "#16A34A" : "#DC2626"), FontWeight = FontWeight.Bold };
        var details = new StackPanel { Spacing = 4 };
        string displayName = string.IsNullOrWhiteSpace(agent.NombrePersonalizado) ? agent.Equipo : agent.NombrePersonalizado;
        details.Children.Add(new TextBlock { Text = displayName, FontSize = 17, FontWeight = FontWeight.Bold });
        details.Children.Add(new TextBlock { Text = $"{agent.Usuario} · {agent.Sistema}", Foreground = Brush.Parse("#64748B") });
        details.Children.Add(new TextBlock { Text = $"{agent.Grupo} · {agent.MotivoBloqueo}" + (agent.ProximoCambioUtc.HasValue ? $" · Próximo: {agent.ProximoCambioUtc.Value.ToLocalTime():dd/MM HH:mm}" : ""), Foreground = Brush.Parse("#475569") });
        if (agent.SolicitudDesbloqueoPendiente) details.Children.Add(new TextBlock { Text = "🔔 Solicitud de desbloqueo pendiente", Foreground = Brush.Parse("#EA580C"), FontWeight = FontWeight.Bold });
        var button = new Button { Content = agent.BloqueadoAdministrativamente ? "Desbloquear" : "Bloquear", Background = Brush.Parse(agent.BloqueadoAdministrativamente ? "#16A34A" : "#DC2626"), Foreground = Brushes.White, Padding = new Thickness(16, 9) };
        var rename = new Button { Content = "Cambiar nombre", Padding = new Thickness(12, 7) };
        var group = new Button { Content = "Mover de grupo", Padding = new Thickness(12, 7) };
        group.Click += async (_, _) => { string next = agent.Grupo == "Grupo 1" ? "Grupo 2" : agent.Grupo == "Grupo 2" ? "Grupo 3" : "Grupo 1"; await api.SetGroupAsync(agent.Id, next); await ShowAgentsAsync(); };
        var exception = new Button { Content = "Permitir 2 horas", Padding = new Thickness(12, 7) };
        exception.Click += async (_, _) => { await api.OverrideAsync(agent.Id, DateTimeOffset.UtcNow.AddHours(2)); await ShowAgentsAsync(); };
        var update = new Button { Content = agent.ActualizacionDisponible ? $"Actualizar a {agent.UltimaVersion}" : $"v{agent.Version}", Padding = new Thickness(12, 7), IsEnabled = agent.ActualizacionDisponible };
        update.Click += async (_, _) => { await api.UpdateAgentAsync(agent.Id); status.Text = "Actualización solicitada."; };
        rename.Click += async (_, _) => await RenameAsync(agent, displayName);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await api.RestrictAsync(agent.Id, !agent.BloqueadoAdministrativamente); await ShowAgentsAsync(); } catch (Exception ex) { status.Text = $"Error: {ex.Message}"; } finally { button.IsEnabled = true; } };
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { state, group, exception, update, rename, button } };
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

    private async Task ShowScheduleAsync()
    {
        if (loading) return; loading = true;
        try
        {
            title.Text = "Horarios"; status.Text = "Actualizando…"; ScheduleState schedule = await api.ScheduleAsync(); content.Children.Clear();
            foreach (var day in schedule.Horarios.GroupBy(x => x.InicioUtc.ToLocalTime().Date).OrderBy(x => x.Key))
                content.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Child = new TextBlock { Text = $"{day.Key:dddd dd/MM}\n" + string.Join("\n", day.OrderBy(x => x.InicioUtc).Select(x => $"{x.InicioUtc.ToLocalTime():HH:mm}-{x.FinUtc.ToLocalTime():HH:mm}  {x.Empleado}")) } });
            status.Text = $"{schedule.Horarios.Count} turnos · versión {schedule.PublicadoUtc.ToLocalTime():dd/MM HH:mm}";
        }
        catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        finally { loading = false; }
    }

    private async Task ShowControlSessionsAsync()
    {
        if (loading) return; loading = true;
        try
        {
            await api.HeartbeatControlSessionAsync(); List<ControlSessionStatus> sessions = await api.ControlSessionsAsync();
            title.Text = "Sesiones del panel"; content.Children.Clear(); status.Text = $"{sessions.Count} sesiones activas";
            foreach (ControlSessionStatus session in sessions)
            {
                var rename = new Button { Content = "Cambiar nombre", Padding = new Thickness(12, 7) };
                rename.Click += async (_, _) => await RenameControlSessionAsync(session);
                var update = new Button { Content = session.ActualizacionDisponible ? $"Actualizar a {session.UltimaVersion}" : session.EstadoActualizacion, IsEnabled = session.ActualizacionDisponible, Padding = new Thickness(12, 7) };
                update.Click += async (_, _) => await UpdateControlSessionAsync(session);
                var details = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = session.Nombre, FontSize = 17, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = $"{session.Usuario} · {session.Equipo} · {session.Plataforma} · v{session.Version}", Foreground = Brush.Parse("#64748B") },
                    new TextBlock { Text = $"Última conexión: {session.UltimaConexionUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}", Foreground = Brush.Parse("#64748B") },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { rename, update } } } };
                content.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Child = details });
            }
        }
        catch (Exception ex) { status.Text = $"Error: {ex.Message}"; }
        finally { loading = false; }
    }

    private async Task RenameControlSessionAsync(ControlSessionStatus session)
    {
        var input = new TextBox { Text = session.Nombre, MaxLength = 60 }; var save = new Button { Content = "Guardar" };
        var dialog = new Window { Title = "Nombre de la sesión", Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nombre visible de esta sesión" }, input, save } };
        save.Click += async (_, _) => { string name = input.Text?.Trim() ?? ""; if (name.Length == 0) return; await api.RenameControlSessionAsync(session.Id, name); dialog.Close(); };
        await dialog.ShowDialog(this); await ShowControlSessionsAsync();
    }

    private async Task UpdateControlSessionAsync(ControlSessionStatus session)
    {
        bool mac = session.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = mac ? "Seleccionar instalador macOS" : "Seleccionar paquete Windows", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(mac ? "Instalador PKG" : "Paquete ZIP") { Patterns = mac ? ["*.pkg"] : ["*.zip"] }]
        });
        IStorageFile? file = files.FirstOrDefault(); if (file is null) return;
        await using Stream stream = await file.OpenReadAsync(); await api.UploadControlPackageAsync(mac ? "macos" : "windows", stream, file.Name);
        await api.RequestControlUpdatesAsync([session.Id]); status.Text = $"Actualización enviada a {session.Nombre}.";
    }

    private async Task ShowSettingsAsync()
    {
        var url = new TextBox { Text = settings.ServerUrl, PlaceholderText = "Servidor HTTPS" };
        var save = new Button { Content = "Guardar", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 9) };
        var logout = new Button { Content = "Cerrar sesión", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 9) };
        var dialog = new Window { Title = "Configuración de ARES", Width = 520, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        string account = MacControlAuth.Client.User is null ? "" : $"{MacControlAuth.Client.User.DisplayName} · {MacControlAuth.Client.User.Email} · {MacControlAuth.Client.User.Role}";
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { new TextBlock { Text = $"Sesión: {account}" }, new TextBlock { Text = "Dirección del servidor" }, url, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { logout, save } } } };
        save.Click += (_, _) => { settings.ServerUrl = url.Text?.Trim() ?? ""; settings.Save(); api.Update(settings); dialog.Close(); };
        logout.Click += (_, _) => { MacControlAuth.Client.Logout(); if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(); };
        await dialog.ShowDialog(this); await ShowAgentsAsync();
    }

    private async Task<bool> ConfirmClearAsync()
    {
        bool confirmed = false;
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(16, 8) };
        var accept = new Button { Content = "Borrar equipos", Padding = new Thickness(16, 8), Background = Brush.Parse("#DC2626"), Foreground = Brushes.White };
        var dialog = new Window { Title = "Confirmar limpieza", Width = 460, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = {
            new TextBlock { Text = "¿Borrar todos los equipos registrados?", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Los agentes conectados volverán a aparecer automáticamente. Los registros se conservarán.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, accept } }
        }};
        cancel.Click += (_, _) => dialog.Close();
        accept.Click += (_, _) => { confirmed = true; dialog.Close(); };
        await dialog.ShowDialog(this); return confirmed;
    }

    private async Task RenameAsync(AgentStatus agent, string currentName)
    {
        var input = new TextBox { Text = currentName, MaxLength = 50 };
        var cancel = new Button { Content = "Cancelar" };
        var save = new Button { Content = "Guardar", Background = Brush.Parse("#2563EB"), Foreground = Brushes.White };
        var dialog = new Window { Title = "Cambiar nombre", Width = 430, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nombre visible del equipo" }, input, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } } } };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) => { string name = input.Text?.Trim() ?? ""; if (name.Length == 0) return; await api.RenameAgentAsync(agent.Id, name); dialog.Close(); };
        await dialog.ShowDialog(this); await ShowAgentsAsync();
    }
}
