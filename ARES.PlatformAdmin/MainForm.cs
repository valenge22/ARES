using System.Net.Http.Json;
using System.Text.Json;

namespace ARES.PlatformAdmin;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label summary = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox search = new() { PlaceholderText = "Buscar cliente, plan o estado…", Width = 280, Dock = DockStyle.Right, Margin = new Padding(8) };
    private List<OrganizationLicense> organizations = [];
    public MainForm()
    {
        Text = "ARES · Administración de plataforma"; WindowState = FormWindowState.Maximized; MinimumSize = new Size(1050, 650); BackColor = Color.FromArgb(241, 245, 249);
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(8, 38, 70), Padding = new Padding(24, 14, 24, 14) };
        header.Controls.Add(new Label { Text = "ARES · ADMINISTRACIÓN", Dock = DockStyle.Left, Width = 370, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        var logout = HeaderButton("Cerrar sesión"); logout.Click += (_, _) => { PlatformAuth.Client.Logout(); Application.Restart(); }; header.Controls.Add(logout);
        var web = HeaderButton("Abrir respaldo web"); web.Click += (_, _) => OpenWeb(); header.Controls.Add(web);
        var alerts = HeaderButton("Alertas"); alerts.Click += (_, _) => ShowAlerts(); header.Controls.Add(alerts);
        var auditButton = HeaderButton("Auditoría"); auditButton.Click += async (_, _) => await ShowAuditAsync(); header.Controls.Add(auditButton);
        var billing = HeaderButton("Facturación"); billing.Click += async (_, _) => await ShowBillingAsync(); header.Controls.Add(billing);
        var metricsButton = HeaderButton("Métricas"); metricsButton.Click += async (_, _) => await ShowMetricsAsync(); header.Controls.Add(metricsButton);
        var testButton = HeaderButton("Prueba integral"); testButton.Click += async (_, _) => await RunSystemTestAsync(); header.Controls.Add(testButton);
        var refresh = HeaderButton("Actualizar"); refresh.Click += async (_, _) => await LoadAsync(); header.Controls.Add(refresh);
        var metrics = new Panel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(24, 10, 24, 8), BackColor = Color.White }; metrics.Controls.Add(search); metrics.Controls.Add(summary);
        search.TextChanged += (_, _) => ApplySearch();
        ConfigureGrid();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = Color.White };
        var remove = ActionButton("Eliminar cliente", Color.FromArgb(220, 38, 38)); remove.Click += async (_, _) => await DeleteSelectedAsync();
        var edit = ActionButton("Editar licencia", Color.FromArgb(37, 99, 235)); edit.Click += async (_, _) => await EditSelectedAsync();
        var details = ActionButton("Ver detalles", Color.FromArgb(2, 132, 199)); details.Click += (_, _) => ShowDetails();
        var support = ActionButton("Soporte", Color.FromArgb(14, 116, 144)); support.Click += async (_, _) => await ShowSupportAsync();
        actions.Controls.Add(remove); actions.Controls.Add(edit); actions.Controls.Add(details); actions.Controls.Add(support);
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
            organizations = await http.GetFromJsonAsync<List<OrganizationLicense>>($"{PlatformAuth.ServerUrl}/api/platform/organizations") ?? [];
            ApplySearch(); summary.Text = $"Organizaciones: {organizations.Count}     Equipos activos: {organizations.Sum(x => x.UsedDevices)}     Alertas: {organizations.Count(IsAlert)}";
        }
        catch (Exception ex) { summary.Text = $"No se pudo actualizar: {ex.Message}"; }
    }
    private void ApplySearch()
    {
        string value = search.Text.Trim();
        grid.DataSource = organizations.Where(x => value.Length == 0 || $"{x.OrganizationName} {x.Slug} {x.PlanName} {x.AccessStatusName}".Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
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
    private async Task ShowSupportAsync()
    {
        OrganizationLicense? item = Selected; if (item is null) return;
        using var dialog = new Form { Text = $"Soporte · {item.OrganizationName}", Width = 980, Height = 620, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(241, 245, 249) };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var devicesTab = new TabPage("Equipos"); var eventsTab = new TabPage("Eventos recientes"); tabs.TabPages.AddRange([devicesTab, eventsTab]); dialog.Controls.Add(tabs);
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            using HttpResponseMessage response = await http.GetAsync($"{PlatformAuth.ServerUrl}/api/platform/organizations/{item.OrganizationId}/support"); await EnsureSuccessAsync(response);
            using JsonDocument data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            List<PlatformSupportDevice> devices = JsonSerializer.Deserialize<List<PlatformSupportDevice>>(data.RootElement.GetProperty("devices").GetRawText()) ?? [];
            var devicesGrid = CreateReadOnlyGrid(); devicesGrid.AutoGenerateColumns = false;
            devicesGrid.Columns.AddRange(new DataGridViewTextBoxColumn { HeaderText = "Equipo", DataPropertyName = nameof(PlatformSupportDevice.Equipo) }, new DataGridViewTextBoxColumn { HeaderText = "Usuario", DataPropertyName = nameof(PlatformSupportDevice.Usuario) }, new DataGridViewTextBoxColumn { HeaderText = "Versión", DataPropertyName = nameof(PlatformSupportDevice.Version) }, new DataGridViewTextBoxColumn { HeaderText = "Estado", DataPropertyName = nameof(PlatformSupportDevice.StateText) }, new DataGridViewTextBoxColumn { HeaderText = "Solicitud", DataPropertyName = nameof(PlatformSupportDevice.RequestText) }, new DataGridViewButtonColumn { HeaderText = "Emergencia", Text = "Revocar", UseColumnTextForButtonValue = true, Name = "Revoke" });
            devicesGrid.DataSource = devices;
            devicesGrid.CellContentClick += async (_, e) => { if (e.RowIndex < 0 || devicesGrid.Columns[e.ColumnIndex].Name != "Revoke" || devicesGrid.Rows[e.RowIndex].DataBoundItem is not PlatformSupportDevice device) return; if (MessageBox.Show($"¿Revocar la credencial de {device.Equipo}? El agente deberá vincularse nuevamente.", "Acción de emergencia", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; try { using HttpClient revoke = PlatformAuth.Client.CreateHttpClient(); using HttpResponseMessage revokeResponse = await revoke.PostAsync($"{PlatformAuth.ServerUrl}/api/platform/organizations/{item.OrganizationId}/devices/{Uri.EscapeDataString(device.Id)}/revoke", null); await EnsureSuccessAsync(revokeResponse); MessageBox.Show("Credencial revocada.", "ARES"); dialog.Close(); } catch (Exception ex) { MessageBox.Show(ex.Message, "ARES"); } };
            devicesTab.Controls.Add(devicesGrid);
            var eventsGrid = CreateReadOnlyGrid(); eventsGrid.DataSource = data.RootElement.GetProperty("events").EnumerateArray().Select(x => new { Fecha = x.GetProperty("fechaUtc").GetDateTimeOffset().ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Evento = x.GetProperty("tipo").GetString(), Detalle = x.GetProperty("detalle").GetString() }).ToList(); eventsTab.Controls.Add(eventsGrid);
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Soporte ARES", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task ShowAuditAsync()
    {
        using var dialog = new Form { Text = "Auditoría de plataforma", Width = 1050, Height = 620, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(241, 245, 249) };
        var table = CreateReadOnlyGrid(); dialog.Controls.Add(table);
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            PlatformOverview? overview = await http.GetFromJsonAsync<PlatformOverview>($"{PlatformAuth.ServerUrl}/api/platform/overview");
            table.DataSource = overview?.Audit.Select(x => new { Fecha = x.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Administrador = x.ActorName, Acción = x.Action, Detalle = x.Detail }).ToList() ?? [];
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Auditoría ARES", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static DataGridView CreateReadOnlyGrid() => new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private async Task ShowBillingAsync()
    {
        using var dialog = new Form { Text = "Facturación global de ARES", Width = 1180, Height = 680, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(241, 245, 249) };
        var search = new TextBox { PlaceholderText = "Buscar cliente u operación", Width = 280 };
        var status = Choice(["Todos", "approved", "pending", "in_process", "rejected"], "Todos"); status.Width = 170;
        var total = new Label { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), Margin = new Padding(20, 10, 0, 0) };
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(18, 12, 18, 8), BackColor = Color.White, Controls = { search, status, total } };
        var paymentsGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", DataPropertyName = nameof(PlatformPayment.DateText) }); paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Organización", DataPropertyName = nameof(PlatformPayment.OrganizationName), FillWeight = 145 });
        paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Operación", DataPropertyName = nameof(PlatformPayment.ProviderPaymentId), FillWeight = 125 }); paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Plan", DataPropertyName = nameof(PlatformPayment.PlanName) });
        paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Importe", DataPropertyName = nameof(PlatformPayment.AmountText) }); paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado", DataPropertyName = nameof(PlatformPayment.StatusName) });
        paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Período", DataPropertyName = nameof(PlatformPayment.PeriodText), FillWeight = 135 }); paymentsGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "Comprobante", Text = "Abrir", UseColumnTextForButtonValue = true, FillWeight = 75 });
        dialog.Controls.Add(paymentsGrid); dialog.Controls.Add(filters);
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            List<PlatformPayment> payments = await http.GetFromJsonAsync<List<PlatformPayment>>($"{PlatformAuth.ServerUrl}/api/platform/billing/history") ?? [];
            void ApplyFilter()
            {
                string term = search.Text.Trim(); string selectedStatus = status.Text;
                List<PlatformPayment> visible = payments.Where(x => (selectedStatus == "Todos" || x.Status == selectedStatus) && (term.Length == 0 || x.OrganizationName.Contains(term, StringComparison.OrdinalIgnoreCase) || x.ProviderPaymentId.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
                paymentsGrid.DataSource = visible; DateTime start = new(DateTime.Today.Year, DateTime.Today.Month, 1); decimal monthly = payments.Where(x => x.Status == "approved" && x.OccurredAt.LocalDateTime >= start).Sum(x => x.AmountArs); total.Text = $"Aprobado este mes: ARS {monthly:N2} · Movimientos: {visible.Count}";
            }
            search.TextChanged += (_, _) => ApplyFilter(); status.SelectedIndexChanged += (_, _) => ApplyFilter();
            paymentsGrid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 7 && paymentsGrid.Rows[e.RowIndex].DataBoundItem is PlatformPayment payment && !string.IsNullOrWhiteSpace(payment.ReceiptUrl)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(payment.ReceiptUrl) { UseShellExecute = true }); };
            ApplyFilter(); dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Facturación", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void ShowAlerts()
    {
        List<string> alerts = organizations.SelectMany(x => AlertMessages(x)).ToList();
        using var dialog = new Form { Text = "Alertas comerciales", Width = 760, Height = 540, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White };
        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11), IntegralHeight = false };
        list.Items.AddRange((alerts.Count == 0 ? ["No hay alertas comerciales activas."] : alerts).Cast<object>().ToArray()); dialog.Controls.Add(list); dialog.ShowDialog(this);
    }
    private async Task ShowMetricsAsync()
    {
        try
        {
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            List<PlatformPayment> payments = await http.GetFromJsonAsync<List<PlatformPayment>>($"{PlatformAuth.ServerUrl}/api/platform/billing/history") ?? [];
            List<PlatformPayment> approved = payments.Where(x => x.Status == "approved").ToList();
            DateTime monthStart = new(DateTime.Today.Year, DateTime.Today.Month, 1);
            decimal monthlyRevenue = approved.Where(x => x.OccurredAt.LocalDateTime >= monthStart).Sum(x => x.AmountArs);
            int active = organizations.Count(x => x.AccessStatus is "Active" or "PastDue"), canceled = organizations.Count(x => x.AccessStatus is "Canceled" or "Expired" or "Suspended");
            decimal cancellationRate = organizations.Count == 0 ? 0 : decimal.Round(canceled * 100m / organizations.Count, 1);
            var months = Enumerable.Range(0, 6).Select(offset => monthStart.AddMonths(-5 + offset)).Select(start => new MetricRow { Label = start.ToString("MMMM yyyy"), Value = approved.Where(x => x.OccurredAt.LocalDateTime >= start && x.OccurredAt.LocalDateTime < start.AddMonths(1)).Sum(x => x.AmountArs) }).ToList();
            var plans = organizations.GroupBy(x => x.PlanName).Select(x => new MetricRow { Label = x.Key, Value = x.Count() }).OrderByDescending(x => x.Value).ToList();
            var upcoming = organizations.Where(x => x.AccessEndsAt.HasValue && x.AccessEndsAt.Value >= DateTimeOffset.UtcNow).OrderBy(x => x.AccessEndsAt).Take(20).Select(x => new ExpirationRow { Organization = x.OrganizationName, Plan = x.PlanName, Date = x.AccessEndsAt!.Value.ToLocalTime().ToString("dd/MM/yyyy"), Days = Math.Max(0, (int)Math.Ceiling((x.AccessEndsAt.Value - DateTimeOffset.UtcNow).TotalDays)) }).ToList();
            using var dialog = new Form { Text = "Métricas comerciales de ARES", Width = 1120, Height = 720, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(241, 245, 249) };
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 112, ColumnCount = 5, Padding = new Padding(14) }; for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            cards.Controls.Add(MetricCard("Ingresos del mes", $"ARS {monthlyRevenue:N2}"), 0, 0); cards.Controls.Add(MetricCard("Clientes activos", active.ToString()), 1, 0); cards.Controls.Add(MetricCard("Cancelados/vencidos", canceled.ToString()), 2, 0); cards.Controls.Add(MetricCard("Cancelación", $"{cancellationRate:N1}%"), 3, 0); cards.Controls.Add(MetricCard("Equipos licenciados", organizations.Sum(x => x.TotalDevices).ToString()), 4, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(GridTab("Ingresos mensuales", months, ("Mes", nameof(MetricRow.Label)), ("Ingresos ARS", nameof(MetricRow.ValueText))));
            tabs.TabPages.Add(GridTab("Planes contratados", plans, ("Plan", nameof(MetricRow.Label)), ("Organizaciones", nameof(MetricRow.ValueText))));
            tabs.TabPages.Add(GridTab("Próximos vencimientos", upcoming, ("Organización", nameof(ExpirationRow.Organization)), ("Plan", nameof(ExpirationRow.Plan)), ("Vencimiento", nameof(ExpirationRow.Date)), ("Días restantes", nameof(ExpirationRow.Days))));
            var export = ActionButton("Exportar informe CSV", Color.FromArgb(2, 132, 199)); export.Width = 190; export.Click += (_, _) => ExportMetrics(approved, months, upcoming);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = Color.White, Controls = { export } };
            dialog.Controls.Add(tabs); dialog.Controls.Add(footer); dialog.Controls.Add(cards); dialog.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Métricas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task RunSystemTestAsync()
    {
        using var dialog = new Form { Text = "Prueba integral de ARES", Width = 820, Height = 610, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White };
        var status = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(18, 14, 18, 8), Font = new Font("Segoe UI", 12, FontStyle.Bold), Text = "Ejecutando verificaciones…" };
        var results = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        results.Columns.Add("Verificación", 260); results.Columns.Add("Resultado", 110); results.Columns.Add("Detalle", 390);
        var manual = new Label { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(18, 10, 18, 10), Text = "Pasos manuales posteriores: 1) registrar un correo nuevo y confirmarlo; 2) contratar el plan de prueba comercial; 3) instalar y vincular ARES Agent en una PC o sesión de prueba.", ForeColor = Color.FromArgb(71, 85, 105) };
        dialog.Controls.Add(results); dialog.Controls.Add(manual); dialog.Controls.Add(status); dialog.Show(this);
        var checks = new List<SystemCheck>();
        async Task Check(string name, Func<Task<string>> action)
        {
            try { checks.Add(new(name, true, await action())); }
            catch (Exception ex) { checks.Add(new(name, false, ex.Message)); }
            SystemCheck item = checks[^1]; var row = new ListViewItem(item.Name); row.SubItems.Add(item.Success ? "Correcto" : "Error"); row.SubItems.Add(item.Detail); row.ForeColor = item.Success ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28); results.Items.Add(row); await Task.Yield();
        }
        await Check("Conexión HTTPS", async () => { if (!PlatformAuth.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La URL no utiliza HTTPS."); using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; using var response = await http.GetAsync($"{PlatformAuth.ServerUrl}/health"); await EnsureSuccessAsync(response); return "Servidor accesible mediante HTTPS."; });
        await Check("Servidor y almacenamiento", async () => { using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; HealthInfo? health = await http.GetFromJsonAsync<HealthInfo>($"{PlatformAuth.ServerUrl}/health"); if (health?.Status != "ok" || health.Storage != "postgresql") throw new InvalidOperationException("El servidor o PostgreSQL no están listos."); return $"{health.Service} · PostgreSQL conectado."; });
        await Check("Autenticación", async () => { using HttpClient http = PlatformAuth.Client.CreateHttpClient(); using var response = await http.GetAsync($"{PlatformAuth.ServerUrl}/api/auth/me"); await EnsureSuccessAsync(response); return $"Sesión válida: {PlatformAuth.Client.User?.Email}."; });
        await Check("Permiso de plataforma", async () => { using HttpClient http = PlatformAuth.Client.CreateHttpClient(); PlatformLicense? access = await http.GetFromJsonAsync<PlatformLicense>($"{PlatformAuth.ServerUrl}/api/license"); if (access?.CanManagePlatform != true) throw new UnauthorizedAccessException("La cuenta no es administradora de plataforma."); return "Administrador de plataforma autorizado."; });
        await Check("Organizaciones", async () => { using HttpClient http = PlatformAuth.Client.CreateHttpClient(); var rows = await http.GetFromJsonAsync<List<OrganizationLicense>>($"{PlatformAuth.ServerUrl}/api/platform/organizations") ?? []; return $"{rows.Count} organizaciones disponibles."; });
        await Check("Facturación y Mercado Pago", async () => { using HttpClient http = PlatformAuth.Client.CreateHttpClient(); var rows = await http.GetFromJsonAsync<List<PlatformPayment>>($"{PlatformAuth.ServerUrl}/api/platform/billing/history") ?? []; return $"Endpoint operativo · {rows.Count} movimientos almacenados."; });
        int passed = checks.Count(x => x.Success); status.Text = passed == checks.Count ? $"Sistema listo: {passed}/{checks.Count} verificaciones correctas" : $"Revisar sistema: {passed}/{checks.Count} verificaciones correctas"; status.ForeColor = passed == checks.Count ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28);
    }
    private static Control MetricCard(string title, string value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(5), Padding = new Padding(12) };
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Color.FromArgb(30, 64, 175), TextAlign = ContentAlignment.BottomLeft }); panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(71, 85, 105) }); return panel;
    }
    private static TabPage GridTab(string title, object data, params (string Header, string Property)[] columns)
    {
        var tab = new TabPage(title); var table = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, DataSource = data };
        foreach (var column in columns) table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Header, DataPropertyName = column.Property }); tab.Controls.Add(table); return tab;
    }
    private void ExportMetrics(List<PlatformPayment> approved, List<MetricRow> months, List<ExpirationRow> upcoming)
    {
        using var dialog = new SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", FileName = $"ARES-Informe-{DateTime.Now:yyyy-MM-dd}.csv" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        static string Csv(object? value) => $"\"{Convert.ToString(value)?.Replace("\"", "\"\"")}\"";
        var lines = new List<string> { "RESUMEN ARES", $"Generado,{DateTime.Now:dd/MM/yyyy HH:mm}", $"Organizaciones,{organizations.Count}", $"Equipos licenciados,{organizations.Sum(x => x.TotalDevices)}", $"Usuarios licenciados,{organizations.Sum(x => x.TotalPanelUsers)}", "", "INGRESOS MENSUALES", "Mes,Importe ARS" };
        lines.AddRange(months.Select(x => $"{Csv(x.Label)},{x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")); lines.AddRange(["", "PAGOS APROBADOS", "Fecha,Organización,Operación,Plan,Importe ARS"]); lines.AddRange(approved.Select(x => $"{x.OccurredAt.LocalDateTime:dd/MM/yyyy HH:mm},{Csv(x.OrganizationName)},{Csv(x.ProviderPaymentId)},{Csv(x.PlanName)},{x.AmountArs.ToString(System.Globalization.CultureInfo.InvariantCulture)}")); lines.AddRange(["", "PRÓXIMOS VENCIMIENTOS", "Organización,Plan,Fecha,Días restantes"]); lines.AddRange(upcoming.Select(x => $"{Csv(x.Organization)},{Csv(x.Plan)},{x.Date},{x.Days}"));
        File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true)); MessageBox.Show("Informe exportado correctamente.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private static bool IsAlert(OrganizationLicense x) => AlertMessages(x).Any();
    private static IEnumerable<string> AlertMessages(OrganizationLicense x)
    {
        if (x.AccessStatus is "PastDue" or "Expired" or "Suspended") yield return $"{x.OrganizationName}: {x.AccessStatusName}.";
        if (x.AccessEndsAt.HasValue && x.AccessEndsAt.Value > DateTimeOffset.UtcNow && x.AccessEndsAt.Value <= DateTimeOffset.UtcNow.AddDays(7)) yield return $"{x.OrganizationName}: vence el {x.AccessEndsAt.Value.ToLocalTime():dd/MM/yyyy}.";
        if (x.TotalDevices > 0 && x.UsedDevices >= x.TotalDevices) yield return $"{x.OrganizationName}: alcanzó el límite de equipos ({x.UsedDevices}/{x.TotalDevices}).";
        if (x.TotalPanelUsers > 0 && x.UsedPanelUsers >= x.TotalPanelUsers) yield return $"{x.OrganizationName}: alcanzó el límite de usuarios ({x.UsedPanelUsers}/{x.TotalPanelUsers}).";
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

internal sealed class PlatformPayment
{
    public string OrganizationName { get; set; } = ""; public string ProviderPaymentId { get; set; } = ""; public string Plan { get; set; } = ""; public decimal AmountArs { get; set; } public string Status { get; set; } = ""; public DateTimeOffset? PeriodStart { get; set; } public DateTimeOffset? PeriodEnd { get; set; } public string ReceiptUrl { get; set; } = ""; public DateTimeOffset OccurredAt { get; set; }
    public string DateText => OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); public string PlanName => Plan switch { "Basic" => "Esencial", "Professional" => "Profesional", "Business" => "Empresa", "Enterprise" => "Corporativo", _ => Plan }; public string AmountText => $"ARS {AmountArs:N2}"; public string StatusName => Status switch { "approved" => "Aprobado", "pending" => "Pendiente", "in_process" => "En proceso", "rejected" => "Rechazado", _ => Status }; public string PeriodText => PeriodStart.HasValue ? $"{PeriodStart.Value.ToLocalTime():dd/MM/yyyy}{(PeriodEnd.HasValue ? $" al {PeriodEnd.Value.ToLocalTime():dd/MM/yyyy}" : "")}" : "—";
}
internal sealed class PlatformSupportDevice
{
    public string Id { get; set; } = ""; public string Equipo { get; set; } = ""; public string Usuario { get; set; } = ""; public string Version { get; set; } = "";
    public bool Online { get; set; } public DateTimeOffset UltimaConexionUtc { get; set; } public bool BloqueadoAdministrativamente { get; set; } public bool SolicitudDesbloqueoPendiente { get; set; }
    public string StateText => Online ? (BloqueadoAdministrativamente ? "En línea · bloqueado" : "En línea") : "Sin conexión";
    public string RequestText => SolicitudDesbloqueoPendiente ? "Pendiente" : "—";
}
internal sealed class PlatformOverview { public List<PlatformAuditItem> Audit { get; set; } = []; }
internal sealed class PlatformAuditItem { public string ActorName { get; set; } = ""; public string Action { get; set; } = ""; public string Detail { get; set; } = ""; public DateTimeOffset OccurredAt { get; set; } }
internal sealed class MetricRow { public string Label { get; set; } = ""; public decimal Value { get; set; } public string ValueText => Value.ToString("N2"); }
internal sealed class ExpirationRow { public string Organization { get; set; } = ""; public string Plan { get; set; } = ""; public string Date { get; set; } = ""; public int Days { get; set; } }
internal sealed record SystemCheck(string Name, bool Success, string Detail);
internal sealed class HealthInfo { public string Service { get; set; } = ""; public string Status { get; set; } = ""; public string Storage { get; set; } = ""; public string Authentication { get; set; } = ""; public string Billing { get; set; } = ""; }
