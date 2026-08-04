using AdministracionEmpleados.Servicios;
using ARES.Shared.Modelos;
using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AdministracionEmpleados;

internal sealed class ScheduleForm : Form
{
    private readonly AgenteDiscoveryService api;
    private readonly List<Empleado> empleados;
    private readonly DataGridView grid = new();
    private readonly NumericUpDown month = new() { Minimum = 1, Maximum = 12, Value = DateTime.Now.Month, Width = 55 };
    private readonly NumericUpDown year = new() { Minimum = 2020, Maximum = 2200, Value = DateTime.Now.Year, Width = 75 };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };

    public ScheduleForm(AgenteDiscoveryService api, List<Empleado> empleados)
    {
        this.api = api; this.empleados = empleados;
        Text = "ARES - Horarios"; Width = 1050; Height = 680; StartPosition = FormStartPosition.CenterParent;
        grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = true; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("Empleado", "EMPLEADO");
        var equipos = new DataGridViewComboBoxColumn { Name = "Equipo", HeaderText = "EQUIPO", FlatStyle = FlatStyle.Flat };
        foreach (Empleado e in empleados.Where(e => !string.IsNullOrWhiteSpace(e.Computadora.AgentId))) equipos.Items.Add(e.Computadora.Nombre);
        grid.Columns.Add(equipos);
        grid.Columns.Add("Fecha", "FECHA (dd/MM/yyyy)"); grid.Columns.Add("Inicio", "INICIO"); grid.Columns.Add("Fin", "FIN");
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Eliminar", HeaderText = "", Text = "Eliminar", UseColumnTextForButtonValue = true, FillWeight = 45 });
        grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Eliminar" && !grid.Rows[e.RowIndex].IsNewRow) grid.Rows.RemoveAt(e.RowIndex); };

        var import = Button("Importar Excel", async (_, _) => await ImportAsync());
        var load = Button("Cargar publicados", async (_, _) => await LoadAsync());
        var publish = Button("Publicar cambios", async (_, _) => await PublishAsync(), Color.FromArgb(22, 163, 74));
        var add = Button("Agregar turno", (_, _) => grid.Rows.Add("", "", DateTime.Today.ToString("dd/MM/yyyy"), "06:00", "12:00"));
        var validate = Button("Validar / Simular", (_, _) => ValidateAndSimulate());
        var calendar = Button("Calendario", (_, _) => ShowCalendar());
        var copyWeek = Button("Copiar +7 dias", (_, _) => CopyWeek());
        var margins = Button("Margenes", async (_, _) => await EditMarginsAsync());
        var history = Button("Historial", async (_, _) => await ShowHistoryAsync());
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(10), WrapContents = true };
        top.Controls.AddRange([new Label { Text = "Mes:", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }, month,
            new Label { Text = "Año:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, year, import, load, add,
            copyWeek, validate, calendar, margins, history, publish, status]);
        Controls.Add(grid); Controls.Add(top);
    }

    private static Button Button(string text, EventHandler click, Color? color = null)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
            BackColor = color ?? Color.FromArgb(37, 99, 235), ForeColor = Color.White, Margin = new Padding(8, 0, 0, 0) };
        b.FlatAppearance.BorderSize = 0; b.Click += click; return b;
    }

    private async Task LoadAsync()
    {
        try
        {
            ScheduleState state = await api.ObtenerHorariosAsync(); grid.Rows.Clear(); month.Value = state.Mes is >= 1 and <= 12 ? state.Mes : month.Value; year.Value = state.Anio >= 2020 ? state.Anio : year.Value;
            foreach (ScheduleInterval item in state.Horarios.OrderBy(x => x.InicioUtc)) AddRow(item);
            status.Text = $"Versión publicada: {state.PublicadoUtc.ToLocalTime():dd/MM HH:mm}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task ImportAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm", Title = "Seleccionar horarios" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            List<ImportedShift> shifts = await Task.Run(() => ParseExcel(dialog.FileName, (int)month.Value, (int)year.Value));
            grid.Rows.Clear();
            foreach (ImportedShift shift in shifts)
            {
                Empleado? employee = FindEmployee(shift.Employee);
                int rowIndex = grid.Rows.Add(shift.Employee, employee?.Computadora.Nombre ?? "", shift.Date.ToString("dd/MM/yyyy"), shift.Start.ToString(@"hh\:mm"), shift.End.ToString(@"hh\:mm"));
                if (employee is null) grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
            }
            status.Text = $"{shifts.Count} turnos importados. Revisalos y publicá los cambios.";
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo leer el Excel.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task PublishAsync()
    {
        try
        {
            SchedulePublication publication = BuildPublication();
            List<string> issues = Validate(publication);
            if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues.Take(12)));
            if (MessageBox.Show(BuildSimulation(publication), "Simulacion antes de publicar", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            await api.PublicarHorariosAsync(publication); status.Text = $"Publicado: {DateTime.Now:dd/MM/yyyy HH:mm}";
            MessageBox.Show("Los horarios fueron publicados. Los agentes los recibirán en su próximo heartbeat.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private SchedulePublication BuildPublication()
    {
        var publication = new SchedulePublication { Mes = (int)month.Value, Anio = (int)year.Value };
        int number = 0;
        foreach (DataGridViewRow row in grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow))
        {
            number++;
            string employee = Convert.ToString(row.Cells["Empleado"].Value)?.Trim() ?? "";
            string equipment = Convert.ToString(row.Cells["Equipo"].Value)?.Trim() ?? "";
            if (employee.Length == 0) throw new InvalidOperationException($"Fila {number}: falta el empleado.");
            Empleado? assigned = empleados.FirstOrDefault(e => e.Computadora.Nombre.Equals(equipment, StringComparison.OrdinalIgnoreCase));
            if (assigned is null) throw new InvalidOperationException($"Fila {number}: asigna un equipo a {employee}.");
            if (!DateTime.TryParseExact(Convert.ToString(row.Cells["Fecha"].Value), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) ||
                !TimeSpan.TryParse(Convert.ToString(row.Cells["Inicio"].Value), out TimeSpan start) || !TimeSpan.TryParse(Convert.ToString(row.Cells["Fin"].Value), out TimeSpan end))
                throw new InvalidOperationException($"Fila {number}: fecha u hora invalida para {employee}.");
            DateTime localStart = date.Date + start; DateTime localEnd = date.Date + end; if (localEnd <= localStart) localEnd = localEnd.AddDays(1);
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
            publication.Horarios.Add(new ScheduleInterval { AgentId = assigned.Computadora.AgentId, Empleado = employee,
                InicioUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), zone), TimeSpan.Zero),
                FinUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), zone), TimeSpan.Zero) });
        }
        return publication;
    }

    private static List<string> Validate(SchedulePublication publication)
    {
        var issues = new List<string>();
        foreach (var group in publication.Horarios.GroupBy(x => x.AgentId))
        {
            List<ScheduleInterval> ordered = group.OrderBy(x => x.InicioUtc).ToList();
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i].InicioUtc < ordered[i - 1].FinUtc)
                    issues.Add($"Turnos superpuestos: {ordered[i - 1].Empleado} y {ordered[i].Empleado} ({ordered[i].InicioUtc.ToLocalTime():dd/MM HH:mm}).");
            foreach (var duplicate in ordered.GroupBy(x => (x.Empleado, x.InicioUtc, x.FinUtc)).Where(x => x.Count() > 1))
                issues.Add($"Turno duplicado: {duplicate.Key.Empleado}, {duplicate.Key.InicioUtc.ToLocalTime():dd/MM HH:mm}.");
        }
        if (publication.Horarios.Count == 0) issues.Add("No hay turnos para publicar.");
        return issues;
    }

    private void ValidateAndSimulate()
    {
        try
        {
            SchedulePublication publication = BuildPublication(); List<string> issues = Validate(publication);
            MessageBox.Show(issues.Count == 0 ? "Validacion correcta.\n\n" + BuildSimulation(publication) : string.Join("\n", issues.Take(15)),
                "ARES - Validacion", MessageBoxButtons.OK, issues.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES - Validacion", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static string BuildSimulation(SchedulePublication publication)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int enabled = publication.Horarios.Select(x => x.AgentId).Distinct().Count(id => publication.Horarios.Any(x => x.AgentId == id && now >= x.InicioUtc && now < x.FinUtc));
        int total = publication.Horarios.Select(x => x.AgentId).Distinct().Count();
        return $"Turnos: {publication.Horarios.Count}\nEquipos incluidos: {total}\nSe desbloquearian ahora: {enabled}\nSe bloquearian ahora: {total - enabled}\n\nAceptar para publicar esta programacion.";
    }

    private void CopyWeek()
    {
        List<DataGridViewRow> source = grid.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
        if (source.Count == 0) source = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
        foreach (DataGridViewRow row in source)
        {
            if (!DateTime.TryParseExact(Convert.ToString(row.Cells["Fecha"].Value), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)) continue;
            grid.Rows.Add(Convert.ToString(row.Cells["Empleado"].Value) ?? "", Convert.ToString(row.Cells["Equipo"].Value) ?? "",
                date.AddDays(7).ToString("dd/MM/yyyy"), Convert.ToString(row.Cells["Inicio"].Value) ?? "", Convert.ToString(row.Cells["Fin"].Value) ?? "");
        }
    }

    private void ShowCalendar()
    {
        try
        {
            SchedulePublication publication = BuildPublication();
            using var form = new Form { Text = "Calendario mensual ARES", Width = 1000, Height = 650, StartPosition = FormStartPosition.CenterParent };
            var view = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            view.Columns.Add("Fecha", "FECHA"); view.Columns.Add("Turnos", "TURNOS PROGRAMADOS");
            foreach (var day in publication.Horarios.GroupBy(x => x.InicioUtc.ToLocalTime().Date).OrderBy(x => x.Key))
                view.Rows.Add(day.Key.ToString("dddd dd/MM", new CultureInfo("es-AR")), string.Join(" | ", day.OrderBy(x => x.InicioUtc).Select(x => $"{x.InicioUtc.ToLocalTime():HH:mm}-{x.FinUtc.ToLocalTime():HH:mm} {x.Empleado}")));
            form.Controls.Add(view); form.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task EditMarginsAsync()
    {
        List<GroupPolicy> policies = await api.ObtenerPoliticasGrupoAsync();
        using var form = new Form { Text = "Margenes por grupo", Width = 520, Height = 300, StartPosition = FormStartPosition.CenterParent };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 190, ColumnCount = 3, RowCount = 4, Padding = new Padding(16) };
        table.Controls.Add(new Label { Text = "Grupo" }, 0, 0); table.Controls.Add(new Label { Text = "Antes (min)" }, 1, 0); table.Controls.Add(new Label { Text = "Despues (min)" }, 2, 0);
        var controls = new List<(GroupPolicy Policy, NumericUpDown Early, NumericUpDown Late)>();
        for (int i = 0; i < policies.Count; i++)
        {
            GroupPolicy p = policies[i]; var early = new NumericUpDown { Minimum = 0, Maximum = 180, Value = p.MargenEntradaMinutos }; var late = new NumericUpDown { Minimum = 0, Maximum = 180, Value = p.MargenSalidaMinutos };
            table.Controls.Add(new Label { Text = p.Grupo }, 0, i + 1); table.Controls.Add(early, 1, i + 1); table.Controls.Add(late, 2, i + 1); controls.Add((p, early, late));
        }
        var save = new Button { Text = "Guardar", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK }; form.Controls.Add(table); form.Controls.Add(save);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        foreach (var item in controls) { item.Policy.MargenEntradaMinutos = (int)item.Early.Value; item.Policy.MargenSalidaMinutos = (int)item.Late.Value; }
        await api.GuardarPoliticasGrupoAsync(policies);
    }

    private async Task ShowHistoryAsync()
    {
        List<ScheduleRevision> history = await api.ObtenerHistorialHorariosAsync();
        using var form = new Form { Text = "Historial de horarios", Width = 760, Height = 500, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (ScheduleRevision revision in history) list.Items.Add(new RevisionItem(revision, $"{revision.FechaUtc.ToLocalTime():dd/MM/yyyy HH:mm} - {revision.Accion} - {revision.Estado.Horarios.Count} turnos"));
        var restore = new Button { Text = "Restaurar seleccion", Dock = DockStyle.Bottom, Height = 44 };
        restore.Click += async (_, _) => { if (list.SelectedItem is not RevisionItem item) return; await api.RestaurarHorarioAsync(item.Revision.Id); form.DialogResult = DialogResult.OK; form.Close(); };
        form.Controls.Add(list); form.Controls.Add(restore); if (form.ShowDialog(this) == DialogResult.OK) await LoadAsync();
    }

    private sealed record RevisionItem(ScheduleRevision Revision, string Text) { public override string ToString() => Text; }

    private void AddRow(ScheduleInterval item)
    {
        Empleado? employee = empleados.FirstOrDefault(e => e.Computadora.AgentId.Equals(item.AgentId, StringComparison.OrdinalIgnoreCase));
        grid.Rows.Add(item.Empleado, employee?.Computadora.Nombre ?? "", item.InicioUtc.ToLocalTime().ToString("dd/MM/yyyy"), item.InicioUtc.ToLocalTime().ToString("HH:mm"), item.FinUtc.ToLocalTime().ToString("HH:mm"));
    }

    private Empleado? FindEmployee(string name)
    {
        string normalized = Normalize(name);
        return empleados.FirstOrDefault(e => Normalize(e.Nombre) == normalized || Normalize(e.Nombre).Contains(normalized) || normalized.Contains(Normalize(e.Nombre)));
    }
    private static string Normalize(string value) => new(value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    private static List<ImportedShift> ParseExcel(string path, int month, int year)
    {
        using var book = new XLWorkbook(path); var result = new List<ImportedShift>();
        foreach (IXLWorksheet sheet in book.Worksheets)
        {
            IXLRange? used = sheet.RangeUsed(); if (used is null) continue;
            for (int row = used.RangeAddress.FirstAddress.RowNumber; row <= used.RangeAddress.LastAddress.RowNumber; row++)
            for (int col = used.RangeAddress.FirstAddress.ColumnNumber; col <= used.RangeAddress.LastAddress.ColumnNumber; col++)
            {
                string shiftText = sheet.Cell(row, col).GetFormattedString().Trim();
                Match times = Regex.Match(shiftText, @"^(\d{1,2}):?(\d{2})\s*[-–]\s*(\d{1,2}):?(\d{2})$"); if (!times.Success) continue;
                TimeSpan start = new(int.Parse(times.Groups[1].Value), int.Parse(times.Groups[2].Value), 0);
                TimeSpan end = new(int.Parse(times.Groups[3].Value) % 24, int.Parse(times.Groups[4].Value), 0);
                int firstDayOneColumn = -1;
                for (int probe = col + 1; probe <= used.RangeAddress.LastAddress.ColumnNumber; probe++)
                    if (FindDay(sheet, row, probe) == 1) { firstDayOneColumn = probe; break; }
                for (int c = col + 1; c <= used.RangeAddress.LastAddress.ColumnNumber; c++)
                {
                    string employee = sheet.Cell(row, c).GetFormattedString().Trim(); if (string.IsNullOrWhiteSpace(employee)) continue;
                    int? day = FindDay(sheet, row, c); if (day is null || day > DateTime.DaysInMonth(year, month)) continue;
                    // En la primera semana suelen aparecer al comienzo los últimos días
                    // del mes anterior (29, 30, 31). El mes elegido empieza en la columna 1.
                    if (firstDayOneColumn > 0 && c < firstDayOneColumn) continue;
                    result.Add(new ImportedShift(employee, new DateTime(year, month, day.Value), start, end));
                }
            }
        }
        if (result.Count == 0) throw new InvalidOperationException("No encontré filas de turnos con el formato 06:00-12:00.");
        return result.OrderBy(x => x.Date).ThenBy(x => x.Start).ToList();
    }
    private static int? FindDay(IXLWorksheet sheet, int shiftRow, int col)
    {
        for (int r = shiftRow - 1; r >= Math.Max(1, shiftRow - 6); r--)
        {
            string header = sheet.Cell(r, col).GetFormattedString(); Match m = Regex.Match(header, @"\b([0-3]?\d)\s*$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int day) && day is >= 1 and <= 31) return day;
        }
        return null;
    }
    private sealed record ImportedShift(string Employee, DateTime Date, TimeSpan Start, TimeSpan End);
}
