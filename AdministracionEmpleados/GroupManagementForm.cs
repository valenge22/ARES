using AdministracionEmpleados.Servicios;
using ARES.Shared.Modelos;

namespace AdministracionEmpleados;

internal sealed class GroupManagementForm : Form
{
    private readonly AgenteDiscoveryService service;
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    public GroupManagementForm(AgenteDiscoveryService service)
    {
        this.service = service; Text = "Grupos de la organización"; Width = 680; Height = 520; StartPosition = FormStartPosition.CenterParent;
        table.Columns.Add("Name", "NOMBRE"); table.Columns.Add("Entry", "MARGEN ENTRADA"); table.Columns.Add("Exit", "MARGEN SALIDA");
        var add = new Button { Text = "+ Agregar", Width = 100 }; add.Click += (_, _) => { if (table.Rows.Count < 50) table.Rows.Add($"Grupo {table.Rows.Count + 1}", 0, 0); };
        var remove = new Button { Text = "Eliminar", Width = 100 }; remove.Click += (_, _) => { if (table.Rows.Count > 1 && table.CurrentRow is not null) table.Rows.Remove(table.CurrentRow); };
        var save = new Button { Text = "Guardar cambios", Width = 140, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        save.Click += async (_, _) => await SaveAsync();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(12), Controls = { add, remove, save } };
        Controls.Add(table); Controls.Add(actions); Shown += async (_, _) => await LoadAsync();
    }
    private async Task LoadAsync()
    {
        try { table.Rows.Clear(); foreach (GroupPolicy item in await service.ObtenerPoliticasGrupoAsync()) table.Rows.Add(item.Grupo, item.MargenEntradaMinutos, item.MargenSalidaMinutos); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task SaveAsync()
    {
        try
        {
            var groups = new List<GroupPolicy>();
            foreach (DataGridViewRow row in table.Rows)
            {
                string name = row.Cells[0].Value?.ToString()?.Trim() ?? "";
                if (name.Length is < 1 or > 60 || !int.TryParse(row.Cells[1].Value?.ToString(), out int entry) || !int.TryParse(row.Cells[2].Value?.ToString(), out int exit)) throw new InvalidDataException("Revisá los datos ingresados.");
                groups.Add(new GroupPolicy { Grupo = name, MargenEntradaMinutos = entry, MargenSalidaMinutos = exit });
            }
            await service.GuardarPoliticasGrupoAsync(groups); DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
