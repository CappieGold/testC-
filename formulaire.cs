using System;
using System.Windows.Forms;

namespace WinKiosk.CustomSetup
{
    public partial class ScriptPathForm : Form
    {
        public string ScriptPath { get; private set; }

        public ScriptPathForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.textBoxScriptPath = new System.Windows.Forms.TextBox();
            this.buttonBrowse = new System.Windows.Forms.Button();
            this.buttonOK = new System.Windows.Forms.Button();
            this.SuspendLayout();

            // 
            // textBoxScriptPath
            // 
            this.textBoxScriptPath.Location = new System.Drawing.Point(12, 12);
            this.textBoxScriptPath.Name = "textBoxScriptPath";
            this.textBoxScriptPath.Size = new System.Drawing.Size(260, 20);
            this.textBoxScriptPath.TabIndex = 0;

            // 
            // buttonBrowse
            // 
            this.buttonBrowse.Location = new System.Drawing.Point(278, 10);
            this.buttonBrowse.Name = "buttonBrowse";
            this.buttonBrowse.Size = new System.Drawing.Size(75, 23);
            this.buttonBrowse.TabIndex = 1;
            this.buttonBrowse.Text = "Parcourir...";
            this.buttonBrowse.UseVisualStyleBackColor = true;
            this.buttonBrowse.Click += new System.EventHandler(this.buttonBrowse_Click);

            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(278, 39);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(75, 23);
            this.buttonOK.TabIndex = 2;
            this.buttonOK.Text = "OK";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);

            // 
            // ScriptPathForm
            // 
            this.ClientSize = new System.Drawing.Size(365, 74);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.buttonBrowse);
            this.Controls.Add(this.textBoxScriptPath);
            this.Name = "ScriptPathForm";
            this.Text = "Sélectionner le script";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Script Files (*.bat;*.cmd)|*.bat;*.cmd|All Files (*.*)|*.*";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBoxScriptPath.Text = openFileDialog.FileName;
                }
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            ScriptPath = textBoxScriptPath.Text;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private System.Windows.Forms.TextBox textBoxScriptPath;
        private System.Windows.Forms.Button buttonBrowse;
        private System.Windows.Forms.Button buttonOK;
    }
}
