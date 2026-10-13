#nullable disable

namespace IntraTeams.WinForms
{
    partial class MainForm
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                _boldFont?.Dispose();
                _boldFont = null;
                _iconFont?.Dispose();
                _iconFont = null;
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
            components = new System.ComponentModel.Container();
            lblBanner = new System.Windows.Forms.Label();
            splitMain = new System.Windows.Forms.SplitContainer();
            lstPeople = new System.Windows.Forms.ListBox();
            pnlCallButtons = new System.Windows.Forms.FlowLayoutPanel();
            btnCancelCall = new System.Windows.Forms.Button();
            lblPeopleHeader = new System.Windows.Forms.Label();
            lblMe = new System.Windows.Forms.Label();
            splitCallChat = new System.Windows.Forms.SplitContainer();
            callControl = new IntraTeams.WinForms.Controls.CallControl();
            rtbChat = new System.Windows.Forms.RichTextBox();
            pnlChatInput = new System.Windows.Forms.Panel();
            txtChat = new System.Windows.Forms.TextBox();
            btnSend = new System.Windows.Forms.Button();
            lblChatHeader = new System.Windows.Forms.Label();
            pnlLog = new System.Windows.Forms.Panel();
            lstLog = new System.Windows.Forms.ListBox();
            statusStrip = new System.Windows.Forms.StatusStrip();
            lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            lblToggleLog = new System.Windows.Forms.ToolStripStatusLabel();
            ringTimer = new System.Windows.Forms.Timer(components);
            peopleToolTip = new System.Windows.Forms.ToolTip(components);
            ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
            splitMain.Panel1.SuspendLayout();
            splitMain.Panel2.SuspendLayout();
            splitMain.SuspendLayout();
            pnlCallButtons.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitCallChat).BeginInit();
            splitCallChat.Panel1.SuspendLayout();
            splitCallChat.Panel2.SuspendLayout();
            splitCallChat.SuspendLayout();
            pnlChatInput.SuspendLayout();
            pnlLog.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            //
            // lblBanner
            //
            lblBanner.BackColor = System.Drawing.Color.FromArgb(196, 56, 75);
            lblBanner.Cursor = System.Windows.Forms.Cursors.Hand;
            lblBanner.Dock = System.Windows.Forms.DockStyle.Top;
            lblBanner.ForeColor = System.Drawing.Color.White;
            lblBanner.Location = new System.Drawing.Point(0, 0);
            lblBanner.Name = "lblBanner";
            lblBanner.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            lblBanner.Size = new System.Drawing.Size(1280, 30);
            lblBanner.TabIndex = 0;
            lblBanner.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblBanner.Visible = false;
            lblBanner.Click += lblBanner_Click;
            //
            // splitMain
            //
            splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            splitMain.Location = new System.Drawing.Point(0, 0);
            splitMain.Name = "splitMain";
            //
            // splitMain.Panel1  (people)
            //
            splitMain.Panel1.Controls.Add(lstPeople);
            splitMain.Panel1.Controls.Add(pnlCallButtons);
            splitMain.Panel1.Controls.Add(lblPeopleHeader);
            splitMain.Panel1.Controls.Add(lblMe);
            splitMain.Panel1MinSize = 200;
            //
            // splitMain.Panel2  (call + chat)
            //
            splitMain.Panel2.Controls.Add(splitCallChat);
            splitMain.Size = new System.Drawing.Size(1280, 738);
            splitMain.SplitterDistance = 250;
            splitMain.TabIndex = 1;
            //
            // lstPeople
            //
            lstPeople.BorderStyle = System.Windows.Forms.BorderStyle.None;
            lstPeople.Dock = System.Windows.Forms.DockStyle.Fill;
            lstPeople.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            lstPeople.IntegralHeight = false;
            lstPeople.ItemHeight = 46;
            lstPeople.Location = new System.Drawing.Point(0, 64);
            lstPeople.Name = "lstPeople";
            lstPeople.Size = new System.Drawing.Size(250, 624);
            lstPeople.TabIndex = 2;
            lstPeople.DrawItem += lstPeople_DrawItem;
            lstPeople.MouseLeave += lstPeople_MouseLeave;
            lstPeople.MouseMove += lstPeople_MouseMove;
            lstPeople.MouseUp += lstPeople_MouseUp;
            lstPeople.SelectedIndexChanged += lstPeople_SelectedIndexChanged;
            //
            // pnlCallButtons
            //
            pnlCallButtons.Controls.Add(btnCancelCall);
            pnlCallButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlCallButtons.Location = new System.Drawing.Point(0, 688);
            pnlCallButtons.Name = "pnlCallButtons";
            pnlCallButtons.Padding = new System.Windows.Forms.Padding(8);
            pnlCallButtons.Size = new System.Drawing.Size(250, 50);
            pnlCallButtons.TabIndex = 3;
            pnlCallButtons.Visible = false;
            pnlCallButtons.WrapContents = false;
            //
            // btnCancelCall  (its panel is shown only while an outgoing call rings)
            //
            btnCancelCall.BackColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnCancelCall.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(196, 56, 75);
            btnCancelCall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnCancelCall.ForeColor = System.Drawing.Color.White;
            btnCancelCall.Location = new System.Drawing.Point(8, 8);
            btnCancelCall.Margin = new System.Windows.Forms.Padding(0);
            btnCancelCall.Name = "btnCancelCall";
            btnCancelCall.Size = new System.Drawing.Size(228, 32);
            btnCancelCall.TabIndex = 0;
            btnCancelCall.Text = "Cancel call";
            btnCancelCall.UseVisualStyleBackColor = false;
            btnCancelCall.Click += btnCancelCall_Click;
            //
            // ringTimer  (outgoing call gives up when unanswered; interval set in code)
            //
            ringTimer.Interval = 45000;
            ringTimer.Tick += ringTimer_Tick;
            //
            // peopleToolTip  (text set per call icon under the mouse)
            //
            peopleToolTip.InitialDelay = 300;
            peopleToolTip.ReshowDelay = 100;
            //
            // lblPeopleHeader
            //
            lblPeopleHeader.Dock = System.Windows.Forms.DockStyle.Top;
            lblPeopleHeader.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold);
            lblPeopleHeader.ForeColor = System.Drawing.SystemColors.GrayText;
            lblPeopleHeader.Location = new System.Drawing.Point(0, 36);
            lblPeopleHeader.Name = "lblPeopleHeader";
            lblPeopleHeader.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            lblPeopleHeader.Size = new System.Drawing.Size(250, 28);
            lblPeopleHeader.TabIndex = 1;
            lblPeopleHeader.Text = "PEOPLE";
            lblPeopleHeader.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblMe
            //
            lblMe.AutoEllipsis = true;
            lblMe.BackColor = System.Drawing.SystemColors.ControlLight;
            lblMe.Dock = System.Windows.Forms.DockStyle.Top;
            lblMe.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblMe.Location = new System.Drawing.Point(0, 0);
            lblMe.Name = "lblMe";
            lblMe.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            lblMe.Size = new System.Drawing.Size(250, 36);
            lblMe.TabIndex = 0;
            lblMe.Text = "user (MACHINE)";
            lblMe.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // splitCallChat
            //
            splitCallChat.Dock = System.Windows.Forms.DockStyle.Fill;
            splitCallChat.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitCallChat.Location = new System.Drawing.Point(0, 0);
            splitCallChat.Name = "splitCallChat";
            //
            // splitCallChat.Panel1  (chat - always visible, fills the space)
            //
            splitCallChat.Panel1.Controls.Add(rtbChat);
            splitCallChat.Panel1.Controls.Add(pnlChatInput);
            splitCallChat.Panel1.Controls.Add(lblChatHeader);
            splitCallChat.Panel1MinSize = 260;
            //
            // splitCallChat.Panel2  (the media user control - collapsed until a call
            // starts; MainForm.ShowCallPane opens it to the right of the chat)
            //
            splitCallChat.Panel2.Controls.Add(callControl);
            splitCallChat.Panel2Collapsed = true;
            splitCallChat.Panel2MinSize = 420;
            splitCallChat.Size = new System.Drawing.Size(1026, 738);
            splitCallChat.SplitterDistance = 340;
            splitCallChat.TabIndex = 0;
            //
            // callControl
            //
            callControl.Dock = System.Windows.Forms.DockStyle.Fill;
            callControl.Location = new System.Drawing.Point(0, 0);
            callControl.Name = "callControl";
            callControl.Size = new System.Drawing.Size(682, 738);
            callControl.TabIndex = 0;
            //
            // rtbChat
            //
            rtbChat.BackColor = System.Drawing.SystemColors.Window;
            rtbChat.BorderStyle = System.Windows.Forms.BorderStyle.None;
            rtbChat.Dock = System.Windows.Forms.DockStyle.Fill;
            rtbChat.Location = new System.Drawing.Point(0, 36);
            rtbChat.Name = "rtbChat";
            rtbChat.ReadOnly = true;
            rtbChat.Size = new System.Drawing.Size(342, 662);
            rtbChat.TabIndex = 1;
            rtbChat.TabStop = false;
            rtbChat.Text = "";
            //
            // pnlChatInput
            //
            pnlChatInput.Controls.Add(txtChat);
            pnlChatInput.Controls.Add(btnSend);
            pnlChatInput.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlChatInput.Location = new System.Drawing.Point(0, 698);
            pnlChatInput.Name = "pnlChatInput";
            pnlChatInput.Padding = new System.Windows.Forms.Padding(6, 8, 6, 6);
            pnlChatInput.Size = new System.Drawing.Size(342, 40);
            pnlChatInput.TabIndex = 2;
            //
            // txtChat
            //
            txtChat.Dock = System.Windows.Forms.DockStyle.Fill;
            txtChat.Location = new System.Drawing.Point(6, 8);
            txtChat.Name = "txtChat";
            txtChat.PlaceholderText = "Type a message and press Enter";
            txtChat.Size = new System.Drawing.Size(250, 23);
            txtChat.TabIndex = 0;
            txtChat.TextChanged += txtChat_TextChanged;
            txtChat.KeyDown += txtChat_KeyDown;
            //
            // btnSend
            //
            btnSend.Dock = System.Windows.Forms.DockStyle.Right;
            btnSend.Location = new System.Drawing.Point(256, 8);
            btnSend.Name = "btnSend";
            btnSend.Size = new System.Drawing.Size(80, 26);
            btnSend.TabIndex = 1;
            btnSend.Text = "Send";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;
            //
            // lblChatHeader
            //
            lblChatHeader.AutoEllipsis = true;
            lblChatHeader.BackColor = System.Drawing.SystemColors.ControlLight;
            lblChatHeader.Dock = System.Windows.Forms.DockStyle.Top;
            lblChatHeader.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblChatHeader.Location = new System.Drawing.Point(0, 0);
            lblChatHeader.Name = "lblChatHeader";
            lblChatHeader.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            lblChatHeader.Size = new System.Drawing.Size(342, 36);
            lblChatHeader.TabIndex = 0;
            lblChatHeader.Text = "Chat";
            lblChatHeader.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // pnlLog
            //
            pnlLog.Controls.Add(lstLog);
            pnlLog.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlLog.Location = new System.Drawing.Point(0, 588);
            pnlLog.Name = "pnlLog";
            pnlLog.Size = new System.Drawing.Size(1280, 150);
            pnlLog.TabIndex = 2;
            pnlLog.Visible = false;
            //
            // lstLog
            //
            lstLog.Dock = System.Windows.Forms.DockStyle.Fill;
            lstLog.Font = new System.Drawing.Font("Consolas", 9F);
            lstLog.HorizontalScrollbar = true;
            lstLog.IntegralHeight = false;
            lstLog.Location = new System.Drawing.Point(0, 0);
            lstLog.Name = "lstLog";
            lstLog.Size = new System.Drawing.Size(1280, 150);
            lstLog.TabIndex = 0;
            //
            // statusStrip
            //
            statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { lblStatus, lblToggleLog });
            statusStrip.Location = new System.Drawing.Point(0, 738);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new System.Drawing.Size(1280, 22);
            statusStrip.TabIndex = 3;
            //
            // lblStatus
            //
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(1145, 17);
            lblStatus.Spring = true;
            lblStatus.Text = "Starting...";
            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblToggleLog
            //
            lblToggleLog.IsLink = true;
            lblToggleLog.Name = "lblToggleLog";
            lblToggleLog.Size = new System.Drawing.Size(120, 17);
            lblToggleLog.Text = "Show connection log";
            lblToggleLog.Click += lblToggleLog_Click;
            //
            // MainForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1280, 760);
            // Docking order: the last control added is laid out first.
            Controls.Add(splitMain);
            Controls.Add(pnlLog);
            Controls.Add(lblBanner);
            Controls.Add(statusStrip);
            MinimumSize = new System.Drawing.Size(1000, 620);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "IntraTeams";
            FormClosing += MainForm_FormClosing;
            Shown += MainForm_Shown;
            splitMain.Panel1.ResumeLayout(false);
            splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
            splitMain.ResumeLayout(false);
            pnlCallButtons.ResumeLayout(false);
            splitCallChat.Panel1.ResumeLayout(false);
            splitCallChat.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitCallChat).EndInit();
            splitCallChat.ResumeLayout(false);
            pnlChatInput.ResumeLayout(false);
            pnlChatInput.PerformLayout();
            pnlLog.ResumeLayout(false);
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblBanner;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.ListBox lstPeople;
        private System.Windows.Forms.FlowLayoutPanel pnlCallButtons;
        private System.Windows.Forms.Button btnCancelCall;
        private System.Windows.Forms.Timer ringTimer;
        private System.Windows.Forms.ToolTip peopleToolTip;
        private System.Windows.Forms.Label lblPeopleHeader;
        private System.Windows.Forms.Label lblMe;
        private System.Windows.Forms.SplitContainer splitCallChat;
        private IntraTeams.WinForms.Controls.CallControl callControl;
        private System.Windows.Forms.RichTextBox rtbChat;
        private System.Windows.Forms.Panel pnlChatInput;
        private System.Windows.Forms.TextBox txtChat;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Label lblChatHeader;
        private System.Windows.Forms.Panel pnlLog;
        private System.Windows.Forms.ListBox lstLog;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblToggleLog;
    }
}
