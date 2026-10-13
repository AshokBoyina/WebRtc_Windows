#nullable disable

namespace IntraTeams.WinForms
{
    partial class IncomingCallForm
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblCaller = new System.Windows.Forms.Label();
            lblKind = new System.Windows.Forms.Label();
            btnAccept = new System.Windows.Forms.Button();
            btnDecline = new System.Windows.Forms.Button();
            SuspendLayout();
            //
            // lblCaller
            //
            lblCaller.AutoEllipsis = true;
            lblCaller.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            lblCaller.Location = new System.Drawing.Point(20, 20);
            lblCaller.Name = "lblCaller";
            lblCaller.Size = new System.Drawing.Size(340, 32);
            lblCaller.TabIndex = 0;
            lblCaller.Text = "Someone";
            lblCaller.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblKind
            //
            lblKind.ForeColor = System.Drawing.SystemColors.GrayText;
            lblKind.Location = new System.Drawing.Point(20, 54);
            lblKind.Name = "lblKind";
            lblKind.Size = new System.Drawing.Size(340, 22);
            lblKind.TabIndex = 1;
            lblKind.Text = "is calling you";
            lblKind.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnAccept
            //
            btnAccept.BackColor = System.Drawing.Color.FromArgb(46, 158, 91);
            btnAccept.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnAccept.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(46, 158, 91);
            btnAccept.ForeColor = System.Drawing.Color.White;
            btnAccept.Location = new System.Drawing.Point(64, 96);
            btnAccept.Name = "btnAccept";
            btnAccept.Size = new System.Drawing.Size(120, 38);
            btnAccept.TabIndex = 2;
            btnAccept.Text = "Accept";
            btnAccept.UseVisualStyleBackColor = false;
            btnAccept.Click += btnAccept_Click;
            //
            // btnDecline
            //
            btnDecline.BackColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnDecline.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnDecline.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnDecline.ForeColor = System.Drawing.Color.White;
            btnDecline.Location = new System.Drawing.Point(196, 96);
            btnDecline.Name = "btnDecline";
            btnDecline.Size = new System.Drawing.Size(120, 38);
            btnDecline.TabIndex = 3;
            btnDecline.Text = "Decline";
            btnDecline.UseVisualStyleBackColor = false;
            btnDecline.Click += btnDecline_Click;
            //
            // IncomingCallForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = btnDecline;
            ClientSize = new System.Drawing.Size(380, 156);
            Controls.Add(lblCaller);
            Controls.Add(lblKind);
            Controls.Add(btnAccept);
            Controls.Add(btnDecline);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "IncomingCallForm";
            ShowInTaskbar = true;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Incoming call";
            TopMost = true;
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label lblCaller;
        private System.Windows.Forms.Label lblKind;
        private System.Windows.Forms.Button btnAccept;
        private System.Windows.Forms.Button btnDecline;
    }
}
