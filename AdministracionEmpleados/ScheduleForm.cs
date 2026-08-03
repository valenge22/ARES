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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10), WrapContents = false };
        top.Controls.AddRange([new Label { Text = "Mes:", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }, month,
            new Label { Text = "Año:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, year, import, load, add, publish, status]);
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
                int rowIndex = grid.Rows.Add(shift.Employee, employee?.Computadora.Nombre ?? "", shift.Date.ToString("dd/MM/yyyy"), shift.Start.ToString("HH:mm"), shift.End.ToString("HH:mm"));
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
            var publication = new SchedulePublication { Mes = (int)month.Value, Anio = (int)year.Value };
            foreach (DataGridViewRow row in grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow))
            {
                string employee = Convert.ToString(row.Cells["Empleado"].Value)?.Trim() ?? "";
                string equipment = Convert.ToString(row.Cells["Equipo"].Value)?.Trim() ?? "";
                Empleado? assigned = empleados.FirstOrDefault(e => e.Computadora.Nombre.Equals(equipment, StringComparison.OrdinalIgnoreCase));
                if (assigned is null) throw new InvalidOperationException($"Asigná un equipo al turno de {employee}.");
                if (!DateTime.TryParseExact(Convert.ToString(row.Cells["Fecha"].Value), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) ||
                    !TimeSpan.TryParse(Convert.ToString(row.Cells["Inicio"].Value), out TimeSpan start) || !TimeSpan.TryParse(Convert.ToString(row.Cells["Fin"].Value), out TimeSpan end))
                    throw new InvalidOperationException($"Revisá la fecha y horas de {employee}.");
                DateTime localStart = date.Date + start; DateTime localEnd = date.Date + end;
                if (localEnd <= localStart) localEnd = localEnd.AddDays(1);
                TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
                publication.Horarios.Add(new ScheduleInterval { AgentId = assigned.Computadora.AgentId, Empleado = employee,
                    InicioUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), zone), TimeSpan.Zero),
                    FinUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), zone), TimeSpan.Zero) });
            }
            await api.PublicarHorariosAsync(publication); status.Text = $"Publicado: {DateTime.Now:dd/MM/yyyy HH:mm}";
            MessageBox.Show("Los horarios fueron publicados. Los agentes los recibirán en su próximo heartbeat.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

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
