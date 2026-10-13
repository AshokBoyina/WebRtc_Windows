#nullable disable

namespace IntraTeams.WinForms.Controls
{
    partial class CallControl
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        // Dispose(bool) lives in CallControl.cs because it also stops media devices.

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblStatus = new System.Windows.Forms.Label();
            remoteVideo = new IntraTeams.WinForms.Controls.VideoView();
            localVideo = new IntraTeams.WinForms.Controls.VideoView();
            pnlButtons = new System.Windows.Forms.FlowLayoutPanel();
            btnMute = new System.Windows.Forms.Button();
            btnCamera = new System.Windows.Forms.Button();
            btnShare = new System.Windows.Forms.Button();
            btnHangUp = new System.Windows.Forms.Button();
            pnlButtons.SuspendLayout();
            SuspendLayout();
            //
            // lblStatus
            //
            lblStatus.BackColor = System.Drawing.Color.FromArgb(42, 42, 49);
            lblStatus.Dock = System.Windows.Forms.DockStyle.Top;
            lblStatus.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblStatus.ForeColor = System.Drawing.Color.FromArgb(242, 242, 245);
            lblStatus.Location = new System.Drawing.Point(0, 0);
            lblStatus.Name = "lblStatus";
            lblStatus.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            lblStatus.Size = new System.Drawing.Size(640, 32);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "No active call.";
            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // remoteVideo
            //
            remoteVideo.BackColor = System.Drawing.Color.Black;
            remoteVideo.Dock = System.Windows.Forms.DockStyle.Fill;
            remoteVideo.ForeColor = System.Drawing.Color.FromArgb(154, 154, 168);
            remoteVideo.Location = new System.Drawing.Point(0, 32);
            remoteVideo.Name = "remoteVideo";
            remoteVideo.PlaceholderText = "No incoming video";
            remoteVideo.Size = new System.Drawing.Size(640, 396);
            remoteVideo.TabIndex = 1;
            //
            // localVideo  (picture-in-picture preview, floats over remoteVideo)
            //
            localVideo.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            localVideo.BackColor = System.Drawing.Color.FromArgb(20, 20, 24);
            localVideo.ForeColor = System.Drawing.Color.FromArgb(154, 154, 168);
            localVideo.Location = new System.Drawing.Point(424, 266);
            localVideo.Name = "localVideo";
            localVideo.PlaceholderText = "You";
            localVideo.Size = new System.Drawing.Size(200, 150);
            localVideo.TabIndex = 2;
            localVideo.Visible = false;
            //
            // pnlButtons
            //
            pnlButtons.BackColor = System.Drawing.Color.FromArgb(42, 42, 49);
            pnlButtons.Controls.Add(btnMute);
            pnlButtons.Controls.Add(btnCamera);
            pnlButtons.Controls.Add(btnShare);
            pnlButtons.Controls.Add(btnHangUp);
            pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlButtons.Location = new System.Drawing.Point(0, 428);
            pnlButtons.Name = "pnlButtons";
            pnlButtons.Padding = new System.Windows.Forms.Padding(8);
            pnlButtons.Size = new System.Drawing.Size(640, 52);
            pnlButtons.TabIndex = 3;
            pnlButtons.WrapContents = false;
            //
            // btnMute
            //
            btnMute.BackColor = System.Drawing.Color.FromArgb(51, 51, 60);
            btnMute.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(63, 63, 74);
            btnMute.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnMute.ForeColor = System.Drawing.Color.White;
            btnMute.Location = new System.Drawing.Point(8, 8);
            btnMute.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            btnMute.Name = "btnMute";
            btnMute.Size = new System.Drawing.Size(110, 34);
            btnMute.TabIndex = 0;
            btnMute.Text = "Mute";
            btnMute.UseVisualStyleBackColor = false;
            btnMute.Click += btnMute_Click;
            //
            // btnCamera
            //
            btnCamera.BackColor = System.Drawing.Color.FromArgb(51, 51, 60);
            btnCamera.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(63, 63, 74);
            btnCamera.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnCamera.ForeColor = System.Drawing.Color.White;
            btnCamera.Location = new System.Drawing.Point(126, 8);
            btnCamera.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            btnCamera.Name = "btnCamera";
            btnCamera.Size = new System.Drawing.Size(110, 34);
            btnCamera.TabIndex = 1;
            btnCamera.Text = "Start video";
            btnCamera.UseVisualStyleBackColor = false;
            btnCamera.Click += btnCamera_Click;
            //
            // btnShare
            //
            btnShare.BackColor = System.Drawing.Color.FromArgb(51, 51, 60);
            btnShare.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(63, 63, 74);
            btnShare.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnShare.ForeColor = System.Drawing.Color.White;
            btnShare.Location = new System.Drawing.Point(244, 8);
            btnShare.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            btnShare.Name = "btnShare";
            btnShare.Size = new System.Drawing.Size(110, 34);
            btnShare.TabIndex = 2;
            btnShare.Text = "Share screen";
            btnShare.UseVisualStyleBackColor = false;
            btnShare.Click += btnShare_Click;
            //
            // btnHangUp
            //
            btnHangUp.BackColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnHangUp.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnHangUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnHangUp.ForeColor = System.Drawing.Color.White;
            btnHangUp.Location = new System.Drawing.Point(362, 8);
            btnHangUp.Margin = new System.Windows.Forms.Padding(0);
            btnHangUp.Name = "btnHangUp";
            btnHangUp.Size = new System.Drawing.Size(110, 34);
            btnHangUp.TabIndex = 3;
            btnHangUp.Text = "Hang up";
            btnHangUp.UseVisualStyleBackColor = false;
            btnHangUp.Click += btnHangUp_Click;
            //
            // CallControl
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.Color.FromArgb(31, 31, 35);
            // Order matters for docking: localVideo is top of the z-order so it
            // floats over remoteVideo; the edge-docked panels are laid out first.
            Controls.Add(localVideo);
            Controls.Add(remoteVideo);
            Controls.Add(pnlButtons);
            Controls.Add(lblStatus);
            MinimumSize = new System.Drawing.Size(400, 300);
            Name = "CallControl";
            Size = new System.Drawing.Size(640, 480);
            pnlButtons.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label lblStatus;
        private IntraTeams.WinForms.Controls.VideoView remoteVideo;
        private IntraTeams.WinForms.Controls.VideoView localVideo;
        private System.Windows.Forms.FlowLayoutPanel pnlButtons;
        private System.Windows.Forms.Button btnMute;
        private System.Windows.Forms.Button btnCamera;
        private System.Windows.Forms.Button btnShare;
        private System.Windows.Forms.Button btnHangUp;
    }
}
