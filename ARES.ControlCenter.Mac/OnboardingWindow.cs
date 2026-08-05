using ARES.Shared.Modelos;
using ARES.Shared.Servicios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;
using System.Diagnostics;

namespace ARES.ControlCenter.Mac;

internal sealed class OnboardingWindow : Window
{
    private readonly AresApiClient api;
    private readonly StackPanel content = new() { Spacing = 12 };
    private List<GroupPolicy> groups = [new() { Grupo = "General" }];

    public OnboardingWindow(AresApiClient api, OrganizationSetupInfo organization)
    {
        this.api = api; Title = "Configurar ARES"; Width = 650; Height = 570; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new Border { Background = Brush.Parse("#F1F5F9"), Padding = new Thickness(34), Child = content };
        ShowIntro(organization.Name);
    }

    private void Base(string step, string title, string description)
    {
        content.Children.Clear();
        content.Children.Add(new TextBlock { Text = step, Foreground = Brush.Parse("#2563EB"), FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBlock { Text = title, FontSize = 27, FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#475569"), Margin = new Thickness(0, 0, 0, 14) });
    }

    private void ShowIntro(string organization)
    {
        Base("PASO 1 DE 3", $"Bienvenido a {organization}", "ARES mantiene separadas las computadoras, usuarios, horarios y registros de cada organización.");
        content.Children.Add(new TextBlock { Text = "Los grupos son totalmente libres. Podés usarlos para representar áreas, sedes, turnos o cualquier clasificación que necesites. Luego cada computadora se vincula mediante un código temporal, sin compartir claves del servidor.", TextWrapping = TextWrapping.Wrap, FontSize = 16 });
        var next = Primary("Comenzar"); next.Click += (_, _) => ShowGroups(); content.Children.Add(next);
    }

    private void ShowGroups()
    {
        Base("PASO 2 DE 3", "Definí los grupos", "Escribí un grupo por línea. Podrás modificarlos más adelante desde la configuración del panel.");
        var names = new TextBox { Text = string.Join(Environment.NewLine, groups.Select(x => x.Grupo)), AcceptsReturn = true, Height = 220, PlaceholderText = "Administración\nVentas\nSucursal Centro" };
        var message = new TextBlock { Foreground = Brush.Parse("#B91C1C"), TextWrapping = TextWrapping.Wrap };
        var save = Primary("Guardar y continuar");
        save.Click += async (_, _) =>
        {
            string[] values = (names.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length is < 1 or > 50 || values.Any(x => x.Length > 60) || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            { message.Text = "Ingresá entre 1 y 50 nombres únicos."; return; }
            try { groups = values.Select(x => new GroupPolicy { Grupo = x }).ToList(); await api.SaveGroupPoliciesAsync(groups); ShowPairing(); }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        content.Children.Add(names); content.Children.Add(save); content.Children.Add(message);
    }

    private void ShowPairing()
    {
        Base("PASO 3 DE 3", "Vinculá la primera computadora", "Generá un código temporal y usalo en el instalador de ARES Agent. Este paso puede hacerse más adelante.");
        var group = new ComboBox { ItemsSource = groups.Select(x => x.Grupo).ToArray(), SelectedIndex = 0 };
        var code = new TextBox { IsReadOnly = true, IsVisible = false, FontFamily = FontFamily.Parse("Menlo") };
        var generate = Primary("Generar código");
        generate.Click += async (_, _) =>
        {
            CreatedDeviceEnrollment result = await api.CreateDeviceEnrollmentAsync(group.SelectedItem?.ToString() ?? groups[0].Grupo);
            code.Text = result.Code; code.IsVisible = true;
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(result.Code);
            generate.Content = "Código copiado";
        };
        var download = new Button { Content = "Abrir descarga del instalador", Padding = new Thickness(16, 10) };
        download.Click += (_, _) => Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "https://github.com/valenge22/ARES/releases" }, UseShellExecute = false });
        var finish = Primary("Finalizar configuración"); finish.Click += async (_, _) => { await api.CompleteOrganizationSetupAsync(); Close(); };
        content.Children.Add(new TextBlock { Text = "Grupo inicial" }); content.Children.Add(group); content.Children.Add(generate); content.Children.Add(code); content.Children.Add(download); content.Children.Add(finish);
    }

    private static Button Primary(string text) => new() { Content = text, Padding = new Thickness(18, 11), Background = Brush.Parse("#2563EB"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
}
