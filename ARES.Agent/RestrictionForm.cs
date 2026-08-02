namespace ARES.Agent;

internal sealed class RestrictionForm : Form
{
    private bool permitirCierre;

    public RestrictionForm(Screen pantalla, Func<Task<bool>> solicitarDesbloqueo)
    {
        Bounds = pantalla.Bounds;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.White;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;

        var contenido = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        contenido.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        contenido.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        contenido.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        contenido.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        contenido.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        contenido.Controls.Add(new Label
        {
            Text = "🛡",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter,
            Font = new Font("Segoe UI Emoji", 45F),
            ForeColor = Color.FromArgb(56, 189, 248)
        }, 0, 0);
        contenido.Controls.Add(new Label
        {
            Text = "EQUIPO RESTRINGIDO POR ARES",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 25F, FontStyle.Bold)
        }, 0, 1);
        contenido.Controls.Add(new Label
        {
            Text = "Contactá al administrador. El acceso se restablecerá cuando se retire la restricción.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 12F),
            ForeColor = Color.FromArgb(203, 213, 225)
        }, 0, 2);
        var solicitud = new Button
        {
            Text = "Solicitar desbloqueo",
            Anchor = AnchorStyles.None,
            Size = new Size(230, 46),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        solicitud.FlatAppearance.BorderSize = 0;
        solicitud.Click += async (_, _) =>
        {
            solicitud.Enabled = false;
            solicitud.Text = "Enviando solicitud…";
            bool enviada = await solicitarDesbloqueo();
            solicitud.Text = enviada ? "Solicitud enviada ✓" : "Sin conexión · Reintentar";
            solicitud.BackColor = enviada ? Color.FromArgb(22, 163, 74) : Color.FromArgb(220, 38, 38);
            solicitud.Enabled = !enviada;
        };
        contenido.Controls.Add(solicitud, 0, 3);
        Controls.Add(contenido);

        FormClosing += (_, e) => { if (!permitirCierre) e.Cancel = true; };
        KeyDown += (_, e) =>
        {
            if (e.Alt && e.KeyCode == Keys.F4)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
    }

    public void RetirarRestriccion()
    {
        permitirCierre = true;
        Close();
    }
}
