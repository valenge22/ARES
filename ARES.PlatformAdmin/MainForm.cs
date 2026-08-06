using System.Net.Http.Json;
using System.Text.Json;

namespace ARES.PlatformAdmin;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label summary = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), TextAlign = ContentAlignment.MiddleLeft };
    public MainForm()
    {
        Text = "ARES · Administración de plataforma"; WindowState = FormWindowState.Maximized; MinimumSize = new Size(1050, 650); BackColor = Color.FromArgb(241, 245, 249);
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(8, 38, 70), Padding = new Padding(24, 14, 24, 14) };
        header.Controls.Add(new Label { Text = "ARES · ADMINISTRACIÓN", Dock = DockStyle.Left, Width = 370, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        var logout = HeaderButton("Cerrar sesión"); logout.Click += (_, _) => { PlatformAuth.Client.Logout(); Application.Restart(); }; header.Controls.Add(logout);
        var web = HeaderButton("Abrir respaldo web"); web.Click += (_, _) => OpenWeb(); header.Controls.Add(web);
        var refresh = HeaderButton("Actualizar"); refresh.Click += async (_, _) => await LoadAsync(); header.Controls.Add(refresh);
        var metrics = new Panel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(24, 10, 24, 8), BackColor = Color.White }; metrics.Controls.Add(summary);
        ConfigureGrid();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = Color.White };
        var remove = ActionButton("Eliminar cliente", Color.FromArgb(220, 38, 38)); remove.Click += async (_, _) => await DeleteSelectedAsync();
        var edit = ActionButton("Editar licencia", Color.FromArgb(37, 99, 235)); edit.Click += async (_, _) => await EditSelectedAsync();
        var details = ActionButton("Ver detalles", Color.FromArgb(2, 132, 199)); details.Click += (_, _) => ShowDetails();
        actions.Controls.Add(remove); actions.Controls.Add(edit); actions.Controls.Add(details);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24) }; body.Controls.Add(grid); body.Controls.Add(actions);
        Controls.Add(body); Controls.Add(metrics); Controls.Add(header); Shown += async (_, _) => await LoadAsync();
    }
    private static Button HeaderButton(string text) => new() { Text = text, Dock = DockStyle.Right, Width = 165, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), Margin = new Padding(5) };
    private static Button ActionButton(string text, Color color) => new() { Text = text, Width = 150, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = Color.White, Margin = new Padding(6, 0, 0, 0) };
    private OrganizationLicense? Selected => grid.CurrentRow?.DataBoundItem as OrganizationLicense;
    private void ConfigureGrid()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Organización", DataPropertyName = nameof(OrganizationLicense.OrganizationName), FillWeight = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Identificador", DataPropertyName = nameof(OrganizationLicense.Slug) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Plan", DataPropertyName = nameof(OrganizationLicense.PlanName) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", DataPropertyName = nameof(OrganizationLicense.AccessStatusName) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipos", DataPropertyName = nameof(OrganizationLicense.DevicesText) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usuarios", DataPropertyName = nameof(OrganizationLicense.UsersText) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vencimiento", DataPropertyName = nameof(OrganizationLicense.ExpirationText) });
        grid.CellDoubleClick += async (_, _) => await EditSelectedAsync();
    }
    private async Task LoadAsync()
    {
        summary.Text = "Consultando clientes…";
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            List<OrganizationLicense> items = await http.GetFromJsonAsync<List<OrganizationLicense>>($"{PlatformAuth.ServerUrl}/api/platform/organizations") ?? [];
            grid.DataSource = items; summary.Text = $"Organizaciones: {items.Count}     Equipos activos: {items.Sum(x => x.UsedDevices)}     Pruebas activas: {items.Count(x => x.Plan == "Trial" && x.AccessStatus == "Active")}";
        }
        catch (Exception ex) { summary.Text = $"No se pudo actualizar: {ex.Message}"; }
    }
    private void ShowDetails()
    {
        OrganizationLicense? item = Selected; if (item is null) return;
        MessageBox.Show($"Organización: {item.OrganizationName}\nIdentificador: {item.Slug}\nPlan: {item.PlanName}\nEstado: {item.AccessStatusName}\nEquipos: {item.DevicesText}\nUsuarios: {item.UsersText}\nPrecio mensual: USD {item.MonthlyPriceUsd:N2}\nVencimiento: {item.ExpirationText}\nGracia: {item.GraceDays} días", "Detalles del cliente", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private async Task EditSelectedAsync()
    {
        OrganizationLicense? item = Selected; if (item is null) return;
        using var dialog = new Form { Text = $"Licencia · {item.OrganizationName}", Width = 470, Height = 610, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var plan = Choice(["Trial", "Basic", "Professional", "Business", "Enterprise"], item.Plan);
        var status = Choice(["Active", "Suspended", "Expired", "Canceled", "PastDue"], item.Status);
        var maxDevices = Number(item.MaxDevices, 1, 100000); var extraDevices = Number(item.AdditionalDevices, 0, 100000);
        var maxUsers = Number(item.MaxPanelUsers, 1, 10000); var extraUsers = Number(item.AdditionalPanelUsers, 0, 10000); var grace = Number(item.GraceDays, 0, 30);
        var expiration = new DateTimePicker { Width = 360, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = item.ExpiresAt.HasValue };
        if (item.ExpiresAt.HasValue) expiration.Value = item.ExpiresAt.Value.LocalDateTime;
        var save = ActionButton("Guardar cambios", Color.FromArgb(37, 99, 235)); save.Width = 360;
        var message = new Label { Width = 360, Height = 38, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter };
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(38, 22, 20, 20) };
        AddField(stack, "Plan", plan); AddField(stack, "Estado", status); AddField(stack, "Equipos incluidos", maxDevices); AddField(stack, "Equipos adicionales", extraDevices); AddField(stack, "Usuarios incluidos", maxUsers); AddField(stack, "Usuarios adicionales", extraUsers); AddField(stack, "Días de gracia", grace); AddField(stack, "Vencimiento (desmarcar para dejarlo abierto)", expiration); stack.Controls.Add(save); stack.Controls.Add(message); dialog.Controls.Add(stack);
        save.Click += async (_, _) =>
        {
            save.Enabled = false; message.Text = "Guardando…";
            try
            {
                using HttpClient http = PlatformAuth.Client.CreateHttpClient();
                using HttpResponseMessage response = await http.PutAsJsonAsync($"{PlatformAuth.ServerUrl}/api/platform/organizations/{item.OrganizationId}/license", new { plan = plan.Text, status = status.Text, maxDevices = (int)maxDevices.Value, additionalDevices = (int)extraDevices.Value, maxPanelUsers = (int)maxUsers.Value, additionalPanelUsers = (int)extraUsers.Value, graceDays = (int)grace.Value, expiresAt = expiration.Checked ? new DateTimeOffset(expiration.Value.Date.AddHours(23).AddMinutes(59), TimeZoneInfo.Local.GetUtcOffset(expiration.Value)).ToUniversalTime() : (DateTimeOffset?)null });
                await EnsureSuccessAsync(response); dialog.Close(); await LoadAsync();
            }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { save.Enabled = true; }
        };
        dialog.ShowDialog(this);
    }
    private async Task DeleteSelectedAsync()
    {
        OrganizationLicense? item = Selected; if (item is null) return;
        if (MessageBox.Show($"¿Eliminar {item.OrganizationName}?\n\nSe revocarán sus accesos y equipos.", "Eliminar cliente", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        string confirmation = Microsoft.VisualBasic.Interaction.InputBox($"Escribí ELIMINAR para confirmar la eliminación de {item.OrganizationName}.", "Confirmación definitiva", "");
        if (confirmation != "ELIMINAR") return;
        try { using HttpClient http = PlatformAuth.Client.CreateHttpClient(); using HttpResponseMessage response = await http.DeleteAsync($"{PlatformAuth.ServerUrl}/api/platform/organizations/{item.OrganizationId}"); await EnsureSuccessAsync(response); await LoadAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private static ComboBox Choice(string[] values, string selected) { var result = new ComboBox { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList }; result.Items.AddRange(values); result.SelectedItem = values.Contains(selected) ? selected : values[0]; return result; }
    private static NumericUpDown Number(int value, int min, int max) => new() { Width = 360, Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) };
    private static void AddField(FlowLayoutPanel panel, string label, Control control) { panel.Controls.Add(new Label { Text = label, Width = 360, Height = 20, Margin = new Padding(0, 6, 0, 0) }); panel.Controls.Add(control); }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response) { if (response.IsSuccessStatusCode) return; string text = await response.Content.ReadAsStringAsync(); try { string? error = JsonDocument.Parse(text).RootElement.GetProperty("error").GetString(); throw new InvalidOperationException(error ?? $"Error {response.StatusCode}"); } catch (JsonException) { throw new InvalidOperationException($"Error {response.StatusCode}"); } }
    private static void OpenWeb() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"{PlatformAuth.ServerUrl}/admin-ares") { UseShellExecute = true });
}

internal sealed class OrganizationLicense
{
    public Guid OrganizationId { get; set; } public string OrganizationName { get; set; } = ""; public string Slug { get; set; } = ""; public string Plan { get; set; } = ""; public string PlanName { get; set; } = ""; public string Status { get; set; } = ""; public string AccessStatus { get; set; } = ""; public string AccessStatusName { get; set; } = "";
    public int MaxDevices { get; set; } public int AdditionalDevices { get; set; } public int TotalDevices { get; set; } public long UsedDevices { get; set; } public int MaxPanelUsers { get; set; } public int AdditionalPanelUsers { get; set; } public int TotalPanelUsers { get; set; } public long UsedPanelUsers { get; set; }
    public decimal MonthlyPriceUsd { get; set; } public int GraceDays { get; set; } public DateTimeOffset? ExpiresAt { get; set; } public DateTimeOffset? AccessEndsAt { get; set; }
    public string DevicesText => $"{UsedDevices}/{TotalDevices}"; public string UsersText => $"{UsedPanelUsers}/{TotalPanelUsers}"; public string ExpirationText => AccessEndsAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "Sin vencimiento";
}
