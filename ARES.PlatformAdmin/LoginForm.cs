using System.Net.Http.Json;

namespace ARES.PlatformAdmin;

internal sealed class LoginForm : Form
{
    private readonly TextBox email = new() { PlaceholderText = "Correo de administración", Width = 330 };
    private readonly TextBox password = new() { PlaceholderText = "Contraseña", UseSystemPasswordChar = true, Width = 330 };
    private readonly Label message = new() { Width = 330, Height = 48, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Firebrick };
    private readonly Button login = new() { Text = "Ingresar", Width = 330, Height = 42, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    public LoginForm()
    {
        Text = "ARES · Administración"; Width = 430; Height = 370; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Color.FromArgb(241, 245, 249);
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(40, 32) };
        stack.Controls.Add(new Label { Text = "ARES", AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) });
        stack.Controls.Add(new Label { Text = "Administración de plataforma", AutoSize = true, Font = new Font("Segoe UI", 11), ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 0, 0, 22) });
        foreach (Control control in new Control[] { email, password, login, message }) { control.Margin = new Padding(0, 5, 0, 5); stack.Controls.Add(control); }
        Controls.Add(stack); AcceptButton = login; login.Click += async (_, _) => await SignInAsync();
    }
    private async Task SignInAsync()
    {
        login.Enabled = false; message.Text = "Verificando acceso…";
        try
        {
            bool ok = await PlatformAuth.Client.LoginAsync(email.Text.Trim(), password.Text);
            if (!ok && PlatformAuth.Client.MfaRequired)
            {
                string code = Microsoft.VisualBasic.Interaction.InputBox("Ingresá el código de seis dígitos.", "Verificación en dos pasos", "");
                ok = await PlatformAuth.Client.CompleteMfaAsync(code);
            }
            if (!ok) { message.Text = "Correo, contraseña o código incorrectos."; return; }
            using HttpClient http = PlatformAuth.Client.CreateHttpClient();
            PlatformLicense? access = await http.GetFromJsonAsync<PlatformLicense>($"{PlatformAuth.ServerUrl}/api/license");
            if (access?.CanManagePlatform != true) { PlatformAuth.Client.Logout(); message.Text = "Esta cuenta no administra la plataforma ARES."; return; }
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { message.Text = ex.Message; }
        finally { login.Enabled = true; password.Clear(); }
    }
}

internal sealed class PlatformLicense { public bool CanManagePlatform { get; set; } }
