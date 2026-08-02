namespace AdministracionEmpleados
{
    partial class EmpleadosForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            btnDesbloquear = new Button();
            button1 = new Button();
            pnlSuperior = new Panel();
            lblTitulo = new Label();
            pnlMenu = new FlowLayoutPanel();
            colEstado = new DataGridViewTextBoxColumn();
            colLogueado = new DataGridViewTextBoxColumn();
            colEcendida = new DataGridViewTextBoxColumn();
            colComputadora = new DataGridViewTextBoxColumn();
            colEmpleado = new DataGridViewTextBoxColumn();
            dgvEmpleados = new DataGridView();
            pnlSuperior.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvEmpleados).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label1.Location = new Point(209, 66);
            label1.Name = "label1";
            label1.Size = new Size(355, 32);
            label1.TabIndex = 2;
            label1.Text = "Administración de Empleados";
            label1.Click += label1_Click;
            // 
            // btnDesbloquear
            // 
            btnDesbloquear.Location = new Point(756, 307);
            btnDesbloquear.Name = "btnDesbloquear";
            btnDesbloquear.Size = new Size(90, 23);
            btnDesbloquear.TabIndex = 5;
            btnDesbloquear.Text = "Desbloquear";
            btnDesbloquear.UseVisualStyleBackColor = true;
            btnDesbloquear.Click += btnDesbloquear_Click_1;
            // 
            // button1
            // 
            button1.Location = new Point(675, 307);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 4;
            button1.Text = "Bloquear";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // pnlSuperior
            // 
            pnlSuperior.Controls.Add(lblTitulo);
            pnlSuperior.Dock = DockStyle.Top;
            pnlSuperior.Location = new Point(0, 0);
            pnlSuperior.Name = "pnlSuperior";
            pnlSuperior.Size = new Size(858, 100);
            pnlSuperior.TabIndex = 6;
            pnlSuperior.Paint += panel1_Paint;
            // 
            // lblTitulo
            // 
            lblTitulo.AutoSize = true;
            lblTitulo.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point, 0);
            lblTitulo.Location = new Point(267, 43);
            lblTitulo.Name = "lblTitulo";
            lblTitulo.Size = new Size(355, 32);
            lblTitulo.TabIndex = 0;
            lblTitulo.Text = "Administración de Empleados";
            // 
            // pnlMenu
            // 
            pnlMenu.Dock = DockStyle.Left;
            pnlMenu.Location = new Point(0, 100);
            pnlMenu.Name = "pnlMenu";
            pnlMenu.Size = new Size(190, 389);
            pnlMenu.TabIndex = 7;
            // 
            // colEstado
            // 
            colEstado.HeaderText = "Estado";
            colEstado.Name = "colEstado";
            // 
            // colLogueado
            // 
            colLogueado.HeaderText = "Logueado";
            colLogueado.Name = "colLogueado";
            // 
            // colEcendida
            // 
            colEcendida.HeaderText = "Encendida";
            colEcendida.Name = "colEcendida";
            // 
            // colComputadora
            // 
            colComputadora.HeaderText = "Computadora";
            colComputadora.Name = "colComputadora";
            // 
            // colEmpleado
            // 
            colEmpleado.HeaderText = "Empleados";
            colEmpleado.Name = "colEmpleado";
            // 
            // dgvEmpleados
            // 
            dgvEmpleados.AllowUserToAddRows = false;
            dgvEmpleados.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvEmpleados.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvEmpleados.Columns.AddRange(new DataGridViewColumn[] { colEmpleado, colComputadora, colEcendida, colLogueado, colEstado });
            dgvEmpleados.Location = new Point(186, 117);
            dgvEmpleados.MultiSelect = false;
            dgvEmpleados.Name = "dgvEmpleados";
            dgvEmpleados.RowHeadersVisible = false;
            dgvEmpleados.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvEmpleados.Size = new Size(672, 226);
            dgvEmpleados.TabIndex = 3;
            dgvEmpleados.CellContentClick += dgvEmpleados_CellContentClick;
            // 
            // EmpleadosForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ControlDarkDark;
            ClientSize = new Size(858, 489);
            Controls.Add(pnlMenu);
            Controls.Add(pnlSuperior);
            Controls.Add(btnDesbloquear);
            Controls.Add(button1);
            Controls.Add(dgvEmpleados);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.None;
            Name = "EmpleadosForm";
            Text = "Form1";
            pnlSuperior.ResumeLayout(false);
            pnlSuperior.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvEmpleados).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }


        private void label1_Click(object sender, EventArgs e)
        {
           
        }

        #endregion
        private Label label1;
        private Button btnDesbloquear;
        private Button button1;
        private Panel pnlSuperior;
        private FlowLayoutPanel pnlMenu;
        private DataGridViewTextBoxColumn colEstado;
        private DataGridViewTextBoxColumn colLogueado;
        private DataGridViewTextBoxColumn colEcendida;
        private DataGridViewTextBoxColumn colComputadora;
        private DataGridViewTextBoxColumn colEmpleado;
        private DataGridView dgvEmpleados;
        private Label lblTitulo;
    }
}
