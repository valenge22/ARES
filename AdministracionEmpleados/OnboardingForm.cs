using AdministracionEmpleados.Servicios;
using ARES.Shared.Modelos;
using ARES.Shared.Servicios;
using System.Diagnostics;

namespace AdministracionEmpleados;

internal sealed class OnboardingForm : Form
{
    private readonly AgenteDiscoveryService service;
    private readonly OrganizationSetupInfo organization;
    private readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(34) };
    private readonly Label step = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Color.FromArgb(37, 99, 235), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
    private readonly Label title = new() { Dock = DockStyle.Top, Height = 52, Font = new Font("Segoe UI", 21F, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
    private readonly Label description = new() { Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 10F), ForeColor = Color.FromArgb(71, 85, 105) };
    private List<GroupPolicy> groups = [new() { Grupo = "General" }];

    public OnboardingForm(AgenteDiscoveryService service, OrganizationSetupInfo organization)
    {
        this.service = service; this.organization = organization;
        Text = "Configurar ARES"; Width = 760; Height = 650; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Color.FromArgb(241, 245, 249);
        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(32, 18, 20, 10) };
        header.Controls.Add(new Label { Text = "ARES", Dock = DockStyle.Top, Height = 34, ForeColor = Color.White, Font = new Font("Segoe UI", 22F, FontStyle.Bold) });
        header.Controls.Add(new Label { Text = organization.Name, Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.FromArgb(148, 163, 184) });
        Controls.Add(body); Controls.Add(header);
        ShowIntro();
    }

    private void Base(string stepText, string titleText, string descriptionText)
    {
        body.Controls.Clear(); step.Text = stepText; title.Text = titleText; description.Text = descriptionText;
        body.Controls.Add(description); body.Controls.Add(title); body.Controls.Add(step);
    }

    private void ShowIntro()
    {
        Base("PASO 1 DE 3", "Bienvenido a ARES", "Esta guía explica el proceso inicial. Primero organizás las computadoras en los grupos que tengan sentido para tu empresa; después vinculás los equipos mediante códigos temporales.");
        var info = new Label { Dock = DockStyle.Top, Height = 180, Padding = new Padding(18), BackColor = Color.White, ForeColor = Color.FromArgb(51, 65, 85),
            Text = "• Cada organización mantiene sus usuarios, equipos, horarios y registros separados.\n\n• Los grupos son libres: podés usar áreas, sedes, turnos o cualquier clasificación.\n\n• Los empleados nunca reciben la clave del servidor. Cada computadora obtiene una credencial individual." };
        var next = Primary("Comenzar", (_, _) => ShowGroups());
        body.Controls.Add(next); body.Controls.Add(info); next.BringToFront();
    }

    private void ShowGroups()
    {
        Base("PASO 2 DE 3", "Definí los grupos", "Creá entre 1 y 50 grupos. Los márgenes permiten adelantar el desbloqueo o retrasar el bloqueo respecto del horario asignado.");
        var table = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "NOMBRE DEL GRUPO", FillWeight = 60 });
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "Entry", HeaderText = "MARGEN ENTRADA (MIN)", FillWeight = 25 });
        table.Columns.Add(new DataGridViewTextBoxColumn { Name = "Exit", HeaderText = "MARGEN SALIDA (MIN)", FillWeight = 25 });
        foreach (GroupPolicy group in groups) table.Rows.Add(group.Grupo, group.MargenEntradaMinutos, group.MargenSalidaMinutos);
        var add = new Button { Text = "+ Agregar grupo", Width = 140, Height = 36 };
        add.Click += (_, _) => { if (table.Rows.Count < 50) table.Rows.Add($"Grupo {table.Rows.Count + 1}", 0, 0); };
        var remove = new Button { Text = "Eliminar seleccionado", Width = 160, Height = 36 };
        remove.Click += (_, _) => { if (table.Rows.Count > 1 && table.CurrentRow is not null) table.Rows.Remove(table.CurrentRow); };
        var save = Primary("Guardar y continuar", async (_, _) =>
        {
            try
            {
                var parsed = new List<GroupPolicy>();
                foreach (DataGridViewRow row in table.Rows)
                {
                    string name = row.Cells[0].Value?.ToString()?.Trim() ?? "";
                    if (name.Length is < 1 or > 60 || !int.TryParse(row.Cells[1].Value?.ToString(), out int entry) || !int.TryParse(row.Cells[2].Value?.ToString(), out int exit))
                        throw new InvalidDataException("Revisá los nombres y márgenes ingresados.");
                    parsed.Add(new GroupPolicy { Grupo = name, MargenEntradaMinutos = entry, MargenSalidaMinutos = exit });
                }
                if (parsed.Select(x => x.Grupo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parsed.Count) throw new InvalidDataException("Los nombres de los grupos no pueden repetirse.");
                await service.GuardarPoliticasGrupoAsync(parsed); groups = parsed; ShowPairing();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Controls = { add, remove, save } };
        body.Controls.Add(table); body.Controls.Add(actions); table.BringToFront();
    }

    private void ShowPairing()
    {
        Base("PASO 3 DE 3", "Vinculá la primera computadora", "Generá un código temporal, descargá el instalador oficial y ejecutalo como administrador en la PC del empleado. También podés omitirlo y hacerlo luego desde Equipos.");
        var group = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        group.Items.AddRange(groups.Select(x => x.Grupo).Cast<object>().ToArray()); group.SelectedIndex = 0;
        var code = new TextBox { Width = 420, ReadOnly = true, Font = new Font("Consolas", 12F), Visible = false };
        var generate = new Button { Text = "Generar código para 1 equipo", Width = 220, Height = 38, BackColor = Color.FromArgb(14, 116, 144), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        generate.Click += async (_, _) =>
        {
            try { CreatedDeviceEnrollment result = await service.CrearVinculacionEquipoAsync(1, 24, group.Text); code.Text = result.Code; code.Visible = true; Clipboard.SetText(result.Code); generate.Text = "Código copiado"; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var download = new Button { Text = "Abrir descarga del instalador", Width = 220, Height = 38 };
        download.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/valenge22/ARES/releases") { UseShellExecute = true });
        var finish = Primary("Finalizar configuración", async (_, _) =>
        {
            try { await service.CompletarConfiguracionInicialAsync(); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 15, 0, 0),
            Controls = { new Label { Text = "Grupo inicial de la computadora", AutoSize = true }, group, generate, code, download, new Label { Text = "El código vence en 24 horas y solo puede utilizarse una vez.", AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139) }, finish } };
        body.Controls.Add(stack); stack.BringToFront();
    }

    private static Button Primary(string text, EventHandler click)
    {
        var button = new Button { Text = text, Width = 190, Height = 40, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 14, 0, 0) };
        button.FlatAppearance.BorderSize = 0; button.Click += click; return button;
    }
}
