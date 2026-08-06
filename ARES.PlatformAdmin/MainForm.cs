using System.Net.Http.Json;

namespace ARES.PlatformAdmin;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label summary = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), TextAlign = ContentAlignment.MiddleLeft };
    public MainForm()
    {
        Text = "ARES · Administración de plataforma"; WindowState = FormWindowState.Maximized; MinimumSize = new Size(980, 620); BackColor = Color.FromArgb(241, 245, 249);
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(8, 38, 70), Padding = new Padding(24, 14, 24, 14) };
        header.Controls.Add(new Label { Text = "ARES · ADMINISTRACIÓN", Dock = DockStyle.Left, Width = 370, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        var logout = HeaderButton("Cerrar sesión"); logout.Click += (_, _) => { PlatformAuth.Client.Logout(); Application.Restart(); }; header.Controls.Add(logout);
        var web = HeaderButton("Administración avanzada"); web.Click += (_, _) => OpenWeb(); header.Controls.Add(web);
        var refresh = HeaderButton("Actualizar"); refresh.Click += async (_, _) => await LoadAsync(); header.Controls.Add(refresh);
        var metrics = new Panel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(24, 10, 24, 8), BackColor = Color.White }; metrics.Controls.Add(summary);
        ConfigureGrid();
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) }; body.Controls.Add(grid);
        Controls.Add(body); Controls.Add(metrics); Controls.Add(header); Shown += async (_, _) => await LoadAsync();
    }
    private static Button HeaderButton(string text) => new() { Text = text, Dock = DockStyle.Right, Width = 170, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), Margin = new Padding(5) };
    private void ConfigureGrid()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Organización", DataPropertyName = nameof(OrganizationLicense.OrganizationName), FillWeight = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Identificador", DataPropertyName = nameof(OrganizationLicense.Slug) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Plan", DataPropertyName = nameof(OrganizationLicense.PlanName) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", DataPropertyName = nameof(OrganizationLicense.AccessStatusName) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipos", DataPropertyName = nameof(OrganizationLicense.DevicesText) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usuarios", DataPropertyName = nameof(OrganizationLicense.UsersText) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vencimiento", DataPropertyName = nameof(OrganizationLicense.ExpirationText) });
        grid.CellDoubleClick += (_, _) => OpenWeb();
    }
    private async Task LoadAsync()
    {
        summary.Text = "Consultando clientes…";
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            List<OrganizationLicense> items = await http.GetFromJsonAsync<List<OrganizationLicense>>($"{PlatformAuth.ServerUrl}/api/platform/organizations") ?? [];
            grid.DataSource = items; summary.Text = $"Organizaciones: {items.Count}     Equipos licenciados: {items.Sum(x => x.UsedDevices)}     Pruebas activas: {items.Count(x => x.Plan == "Trial" && x.AccessStatus == "Active")}";
        }
        catch (Exception ex) { summary.Text = $"No se pudo actualizar: {ex.Message}"; }
    }
    private static void OpenWeb() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{PlatformAuth.ServerUrl}/admin-ares") { UseShellExecute = true });
}

internal sealed class OrganizationLicense
{
    public string OrganizationName { get; set; } = ""; public string Slug { get; set; } = ""; public string Plan { get; set; } = ""; public string PlanName { get; set; } = ""; public string AccessStatus { get; set; } = ""; public string AccessStatusName { get; set; } = "";
    public int TotalDevices { get; set; } public long UsedDevices { get; set; } public int TotalPanelUsers { get; set; } public long UsedPanelUsers { get; set; } public DateTimeOffset? AccessEndsAt { get; set; }
    public string DevicesText => $"{UsedDevices}/{TotalDevices}"; public string UsersText => $"{UsedPanelUsers}/{TotalPanelUsers}"; public string ExpirationText => AccessEndsAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "Sin vencimiento";
}
