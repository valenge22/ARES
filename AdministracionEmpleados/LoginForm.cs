namespace AdministracionEmpleados;

internal sealed class LoginForm : Form
{
    private readonly TextBox email = new() { PlaceholderText = "Correo electrónico", Width = 330 };
    private readonly TextBox password = new() { PlaceholderText = "Contraseña", Width = 330, UseSystemPasswordChar = true };
    private readonly Button login = new() { Text = "Iniciar sesión", Width = 330, Height = 42, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Label status = new() { AutoSize = false, Width = 330, Height = 42, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter };

    public LoginForm()
    {
        Text = "ARES · Iniciar sesión"; Width = 430; Height = 390; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Color.FromArgb(241, 245, 249);
        var title = new Label { Text = "🛡  ARES", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(15, 23, 42) };
        var subtitle = new Label { Text = "Centro de Control", Font = new Font("Segoe UI", 12), AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(40, 35) };
        panel.Controls.Add(title); panel.Controls.Add(subtitle); panel.SetFlowBreak(subtitle, true);
        panel.Controls.Add(new Label { Height = 20, Width = 1 }); panel.Controls.Add(email); panel.Controls.Add(new Label { Height = 8, Width = 1 });
        panel.Controls.Add(password); panel.Controls.Add(new Label { Height = 12, Width = 1 }); panel.Controls.Add(login); panel.Controls.Add(status);
        Controls.Add(panel); AcceptButton = login;
        login.Click += async (_, _) => await LoginAsync();
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrEmpty(password.Text)) { status.Text = "Ingresá correo y contraseña."; return; }
        login.Enabled = false; status.ForeColor = Color.FromArgb(71, 85, 105); status.Text = "Verificando…";
        try
        {
            if (!await AresControlAuth.Client.LoginAsync(email.Text.Trim(), password.Text))
            { status.ForeColor = Color.Firebrick; status.Text = "Correo, contraseña o permisos inválidos."; return; }
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { status.ForeColor = Color.Firebrick; status.Text = $"No se pudo conectar: {ex.Message}"; }
        finally { login.Enabled = true; password.Clear(); }
    }
}
