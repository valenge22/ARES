namespace AdministracionEmpleados
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            pnlMenu = new Panel();
            btnEmpleados = new Button();
            pnlSuperior = new Panel();
            lblARES = new Label();
            pnlContenido = new Panel();
            pnlMenu.SuspendLayout();
            pnlSuperior.SuspendLayout();
            SuspendLayout();
            // 
            // pnlMenu
            // 
            pnlMenu.BackColor = Color.DimGray;
            pnlMenu.Controls.Add(btnEmpleados);
            pnlMenu.Controls.Add(pnlSuperior);
            pnlMenu.Dock = DockStyle.Left;
            pnlMenu.Location = new Point(0, 0);
            pnlMenu.Name = "pnlMenu";
            pnlMenu.Size = new Size(200, 450);
            pnlMenu.TabIndex = 0;
            // 
            // btnEmpleados
            // 
            btnEmpleados.Dock = DockStyle.Top;
            btnEmpleados.FlatStyle = FlatStyle.Flat;
            btnEmpleados.Location = new Point(0, 60);
            btnEmpleados.Name = "btnEmpleados";
            btnEmpleados.Size = new Size(200, 55);
            btnEmpleados.TabIndex = 1;
            btnEmpleados.Text = "Empleados";
            btnEmpleados.UseVisualStyleBackColor = true;
            // 
            // pnlSuperior
            // 
            pnlSuperior.Controls.Add(lblARES);
            pnlSuperior.Dock = DockStyle.Top;
            pnlSuperior.Location = new Point(0, 0);
            pnlSuperior.Name = "pnlSuperior";
            pnlSuperior.Size = new Size(200, 60);
            pnlSuperior.TabIndex = 0;
            // 
            // lblARES
            // 
            lblARES.AutoSize = true;
            lblARES.Font = new Font("Segoe UI", 24F, FontStyle.Bold, GraphicsUnit.Point, 0);
            lblARES.ForeColor = Color.FromArgb(31, 41, 55);
            lblARES.Location = new Point(12, 9);
            lblARES.Name = "lblARES";
            lblARES.RightToLeft = RightToLeft.No;
            lblARES.Size = new Size(99, 45);
            lblARES.TabIndex = 0;
            lblARES.Text = "ARES";
            lblARES.Click += lblARES_Click;
            // 
            // pnlContenido
            // 
            pnlContenido.BackColor = Color.White;
            pnlContenido.Dock = DockStyle.Fill;
            pnlContenido.Location = new Point(200, 0);
            pnlContenido.Name = "pnlContenido";
            pnlContenido.Size = new Size(600, 450);
            pnlContenido.TabIndex = 1;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(pnlContenido);
            Controls.Add(pnlMenu);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ARES";
            WindowState = FormWindowState.Maximized;
            Load += MainForm_Load;
            pnlMenu.ResumeLayout(false);
            pnlSuperior.ResumeLayout(false);
            pnlSuperior.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlMenu;
        private Panel pnlSuperior;
        private Panel pnlContenido;
        private Button btnEmpleados;
        private Label lblARES;
    }
}