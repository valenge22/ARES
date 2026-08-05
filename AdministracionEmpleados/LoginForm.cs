namespace AdministracionEmpleados;

internal sealed class LoginForm : Form
{
    private readonly TextBox email = new() { PlaceholderText = "Correo electrónico", Width = 330 };
    private readonly TextBox password = new() { PlaceholderText = "Contraseña", Width = 330, UseSystemPasswordChar = true };
    private readonly Button login = new() { Text = "Iniciar sesión", Width = 330, Height = 42, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button register = new() { Text = "Crear cuenta", Width = 160, Height = 34, FlatStyle = FlatStyle.Flat };
    private readonly Button recover = new() { Text = "Olvidé mi contraseña", Width = 165, Height = 34, FlatStyle = FlatStyle.Flat };
    private readonly Label status = new() { AutoSize = false, Width = 330, Height = 42, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter };

    public LoginForm()
    {
        Text = "ARES · Iniciar sesión"; Width = 430; Height = 440; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Color.FromArgb(241, 245, 249);
        var title = new Label { Text = "🛡  ARES", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(15, 23, 42) };
        var subtitle = new Label { Text = "Centro de Control", Font = new Font("Segoe UI", 12), AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(40, 35) };
        panel.Controls.Add(title); panel.Controls.Add(subtitle); panel.SetFlowBreak(subtitle, true);
        panel.Controls.Add(new Label { Height = 20, Width = 1 }); panel.Controls.Add(email); panel.Controls.Add(new Label { Height = 8, Width = 1 });
        panel.Controls.Add(password); panel.Controls.Add(new Label { Height = 12, Width = 1 }); panel.Controls.Add(login);
        panel.Controls.Add(new FlowLayoutPanel { Width = 330, Height = 42, Controls = { register, recover } }); panel.Controls.Add(status);
        Controls.Add(panel); AcceptButton = login;
        login.Click += async (_, _) => await LoginAsync();
        register.Click += async (_, _) => await RegisterAsync();
        recover.Click += async (_, _) => await RecoverAsync();
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

    private async Task RegisterAsync()
    {
        using var dialog = new Form { Text = "Crear cuenta ARES", Width = 440, Height = 520, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var mode = new ComboBox { Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange(["Crear una organización nueva", "Unirme con un código de invitación"]); mode.SelectedIndex = 0;
        var name = new TextBox { PlaceholderText = "Nombre", Width = 340 }; var mail = new TextBox { PlaceholderText = "Correo", Width = 340 };
        var pass = new TextBox { PlaceholderText = "Contraseña (mínimo 8 caracteres)", UseSystemPasswordChar = true, Width = 340 };
        var confirm = new TextBox { PlaceholderText = "Repetir contraseña", UseSystemPasswordChar = true, Width = 340 };
        var organization = new TextBox { PlaceholderText = "Nombre de la empresa u organización", Width = 340 };
        var code = new TextBox { PlaceholderText = "Código de invitación", UseSystemPasswordChar = true, Width = 340, Visible = false };
        var message = new Label { Width = 340, Height = 45, TextAlign = ContentAlignment.MiddleCenter };
        var create = new Button { Text = "Crear cuenta", Width = 340, Height = 40, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(40, 25) };
        foreach (Control item in new Control[] { mode, organization, name, mail, pass, confirm, code, create, message }) { item.Margin = new Padding(0, 5, 0, 5); stack.Controls.Add(item); }
        dialog.Controls.Add(stack);
        mode.SelectedIndexChanged += (_, _) => { bool joining = mode.SelectedIndex == 1; organization.Visible = !joining; code.Visible = joining; create.Text = joining ? "Solicitar acceso" : "Crear organización"; };
        create.Click += async (_, _) =>
        {
            if (pass.Text != confirm.Text) { message.Text = "Las contraseñas no coinciden."; return; }
            create.Enabled = false;
            try
            {
                bool joining = mode.SelectedIndex == 1;
                var result = await AresControlAuth.Client.RegisterAsync(name.Text.Trim(), mail.Text.Trim(), pass.Text,
                    joining ? code.Text : "", joining ? "" : organization.Text.Trim());
                message.Text = result.Message;
                if (result.Success) { message.ForeColor = Color.Green; create.Text = joining ? "Solicitud enviada" : "Organización creada"; }
            }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { create.Enabled = true; }
        };
        dialog.ShowDialog(this);
    }

    private async Task RecoverAsync()
    {
        string value = Microsoft.VisualBasic.Interaction.InputBox("Ingresá el correo de tu cuenta. Te enviaremos un enlace para cambiar la contraseña.", "Recuperar contraseña", email.Text);
        if (string.IsNullOrWhiteSpace(value)) return;
        try { await AresControlAuth.Client.RecoverAsync(value.Trim()); MessageBox.Show("Si el correo existe, recibirás un enlace de recuperación.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
