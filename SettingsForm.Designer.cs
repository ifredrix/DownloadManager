using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

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
        lblDownloadPath = new Label();
        txtDownloadPath = new TextBox();
        btnBrowse = new Button();
        lblMaxConcurrent = new Label();
        nudMaxConcurrent = new NumericUpDown();
        lblConnections = new Label();
        nudConnections = new NumericUpDown();
        lblSpeedLimit = new Label();
        nudSpeedLimit = new NumericUpDown();
        lblSpeedHint = new Label();
        chkClipboard = new CheckBox();
        chkAutoStart = new CheckBox();
        chkNotifications = new CheckBox();
        chkScheduleStart = new CheckBox();
        dtpScheduleStart = new DateTimePicker();
        chkScheduleStop = new CheckBox();
        dtpScheduleStop = new DateTimePicker();
        lblAfterDone = new Label();
        cmbAfterDone = new ComboBox();
        lblTheme = new Label();
        cmbTheme = new ComboBox();
        lblAccent = new Label();
        cmbAccent = new ComboBox();
        btnCustomizeToolbar = new Button();
        chkTorAll = new CheckBox();
        btnTorSetup = new Button();
        chkStartWindows = new CheckBox();
        chkConfirmDl = new CheckBox();
        btnOK = new Button();
        btnCancel = new Button();
        ((System.ComponentModel.ISupportInitialize)nudMaxConcurrent).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudConnections).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudSpeedLimit).BeginInit();
        SuspendLayout();
        //
        // lblDownloadPath
        //
        lblDownloadPath.AutoSize = true;
        lblDownloadPath.ForeColor = Color.FromArgb(94, 104, 120);
        lblDownloadPath.Location = new Point(16, 16);
        lblDownloadPath.Name = "lblDownloadPath";
        lblDownloadPath.Size = new Size(94, 15);
        lblDownloadPath.TabIndex = 0;
        lblDownloadPath.Text = "Download folder";
        //
        // txtDownloadPath
        //
        txtDownloadPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtDownloadPath.BackColor = Color.White;
        txtDownloadPath.BorderStyle = BorderStyle.FixedSingle;
        txtDownloadPath.ForeColor = Color.FromArgb(28, 34, 44);
        txtDownloadPath.Location = new Point(16, 36);
        txtDownloadPath.Name = "txtDownloadPath";
        txtDownloadPath.Size = new Size(388, 23);
        txtDownloadPath.TabIndex = 1;
        //
        // btnBrowse
        //
        btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowse.BackColor = Color.White;
        btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnBrowse.FlatStyle = FlatStyle.Flat;
        btnBrowse.ForeColor = Color.FromArgb(28, 34, 44);
        btnBrowse.Location = new Point(410, 35);
        btnBrowse.Name = "btnBrowse";
        btnBrowse.Size = new Size(94, 26);
        btnBrowse.TabIndex = 2;
        btnBrowse.Text = "Browse";
        btnBrowse.UseVisualStyleBackColor = false;
        btnBrowse.Click += btnBrowse_Click;
        //
        // lblMaxConcurrent
        //
        lblMaxConcurrent.AutoSize = true;
        lblMaxConcurrent.ForeColor = Color.FromArgb(28, 34, 44);
        lblMaxConcurrent.Location = new Point(16, 80);
        lblMaxConcurrent.Name = "lblMaxConcurrent";
        lblMaxConcurrent.Size = new Size(147, 15);
        lblMaxConcurrent.TabIndex = 3;
        lblMaxConcurrent.Text = "Simultaneous downloads";
        //
        // nudMaxConcurrent
        //
        nudMaxConcurrent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nudMaxConcurrent.BackColor = Color.White;
        nudMaxConcurrent.BorderStyle = BorderStyle.FixedSingle;
        nudMaxConcurrent.ForeColor = Color.FromArgb(28, 34, 44);
        nudMaxConcurrent.Location = new Point(354, 77);
        nudMaxConcurrent.Maximum = new decimal(new int[] { 16, 0, 0, 0 });
        nudMaxConcurrent.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudMaxConcurrent.Name = "nudMaxConcurrent";
        nudMaxConcurrent.Size = new Size(150, 23);
        nudMaxConcurrent.TabIndex = 4;
        nudMaxConcurrent.Value = new decimal(new int[] { 3, 0, 0, 0 });
        //
        // lblConnections
        //
        lblConnections.AutoSize = true;
        lblConnections.ForeColor = Color.FromArgb(28, 34, 44);
        lblConnections.Location = new Point(16, 116);
        lblConnections.Name = "lblConnections";
        lblConnections.Size = new Size(158, 15);
        lblConnections.TabIndex = 5;
        lblConnections.Text = "Connections per download";
        //
        // nudConnections
        //
        nudConnections.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nudConnections.BackColor = Color.White;
        nudConnections.BorderStyle = BorderStyle.FixedSingle;
        nudConnections.ForeColor = Color.FromArgb(28, 34, 44);
        nudConnections.Location = new Point(354, 113);
        nudConnections.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
        nudConnections.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudConnections.Name = "nudConnections";
        nudConnections.Size = new Size(150, 23);
        nudConnections.TabIndex = 6;
        nudConnections.Value = new decimal(new int[] { 20, 0, 0, 0 });
        //
        // lblSpeedLimit
        //
        lblSpeedLimit.AutoSize = true;
        lblSpeedLimit.ForeColor = Color.FromArgb(28, 34, 44);
        lblSpeedLimit.Location = new Point(16, 152);
        lblSpeedLimit.Name = "lblSpeedLimit";
        lblSpeedLimit.Size = new Size(105, 15);
        lblSpeedLimit.TabIndex = 7;
        lblSpeedLimit.Text = "Speed limit (KB/s)";
        //
        // nudSpeedLimit
        //
        nudSpeedLimit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nudSpeedLimit.BackColor = Color.White;
        nudSpeedLimit.BorderStyle = BorderStyle.FixedSingle;
        nudSpeedLimit.ForeColor = Color.FromArgb(28, 34, 44);
        nudSpeedLimit.Increment = new decimal(new int[] { 100, 0, 0, 0 });
        nudSpeedLimit.Location = new Point(354, 149);
        nudSpeedLimit.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
        nudSpeedLimit.Name = "nudSpeedLimit";
        nudSpeedLimit.Size = new Size(150, 23);
        nudSpeedLimit.TabIndex = 8;
        //
        // lblSpeedHint
        //
        lblSpeedHint.AutoSize = true;
        lblSpeedHint.ForeColor = Color.FromArgb(94, 104, 120);
        lblSpeedHint.Location = new Point(16, 180);
        lblSpeedHint.Name = "lblSpeedHint";
        lblSpeedHint.Size = new Size(99, 15);
        lblSpeedHint.TabIndex = 9;
        lblSpeedHint.Text = "0 = no limit";
        //
        // chkClipboard
        //
        chkClipboard.AutoSize = true;
        chkClipboard.ForeColor = Color.FromArgb(28, 34, 44);
        chkClipboard.Location = new Point(16, 210);
        chkClipboard.Name = "chkClipboard";
        chkClipboard.Size = new Size(258, 19);
        chkClipboard.TabIndex = 10;
        chkClipboard.Text = "Watch clipboard and add links automatically";
        chkClipboard.UseVisualStyleBackColor = true;
        //
        // chkAutoStart
        //
        chkAutoStart.AutoSize = true;
        chkAutoStart.ForeColor = Color.FromArgb(28, 34, 44);
        chkAutoStart.Location = new Point(16, 236);
        chkAutoStart.Name = "chkAutoStart";
        chkAutoStart.Size = new Size(147, 19);
        chkAutoStart.TabIndex = 11;
        chkAutoStart.Text = "Start downloads at once";
        chkAutoStart.UseVisualStyleBackColor = true;
        //
        // chkNotifications
        //
        chkNotifications.AutoSize = true;
        chkNotifications.ForeColor = Color.FromArgb(28, 34, 44);
        chkNotifications.Location = new Point(16, 262);
        chkNotifications.Name = "chkNotifications";
        chkNotifications.Size = new Size(230, 19);
        chkNotifications.TabIndex = 12;
        chkNotifications.Text = "Notify me when a download finishes";
        chkNotifications.UseVisualStyleBackColor = true;
        //
        // chkScheduleStart
        //
        chkScheduleStart.AutoSize = true;
        chkScheduleStart.ForeColor = Color.FromArgb(28, 34, 44);
        chkScheduleStart.Location = new Point(16, 292);
        chkScheduleStart.Name = "chkScheduleStart";
        chkScheduleStart.Size = new Size(148, 19);
        chkScheduleStart.TabIndex = 13;
        chkScheduleStart.Text = "Start queued at";
        chkScheduleStart.UseVisualStyleBackColor = true;
        //
        // dtpScheduleStart
        //
        dtpScheduleStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        dtpScheduleStart.CustomFormat = "HH:mm";
        dtpScheduleStart.Format = DateTimePickerFormat.Custom;
        dtpScheduleStart.Location = new Point(354, 289);
        dtpScheduleStart.Name = "dtpScheduleStart";
        dtpScheduleStart.ShowUpDown = true;
        dtpScheduleStart.Size = new Size(150, 23);
        dtpScheduleStart.TabIndex = 14;
        //
        // chkScheduleStop
        //
        chkScheduleStop.AutoSize = true;
        chkScheduleStop.ForeColor = Color.FromArgb(28, 34, 44);
        chkScheduleStop.Location = new Point(16, 322);
        chkScheduleStop.Name = "chkScheduleStop";
        chkScheduleStop.Size = new Size(150, 19);
        chkScheduleStop.TabIndex = 15;
        chkScheduleStop.Text = "Pause all at";
        chkScheduleStop.UseVisualStyleBackColor = true;
        //
        // dtpScheduleStop
        //
        dtpScheduleStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        dtpScheduleStop.CustomFormat = "HH:mm";
        dtpScheduleStop.Format = DateTimePickerFormat.Custom;
        dtpScheduleStop.Location = new Point(354, 319);
        dtpScheduleStop.Name = "dtpScheduleStop";
        dtpScheduleStop.ShowUpDown = true;
        dtpScheduleStop.Size = new Size(150, 23);
        dtpScheduleStop.TabIndex = 16;
        //
        // lblAfterDone
        //
        lblAfterDone.AutoSize = true;
        lblAfterDone.ForeColor = Color.FromArgb(28, 34, 44);
        lblAfterDone.Location = new Point(16, 354);
        lblAfterDone.Name = "lblAfterDone";
        lblAfterDone.Size = new Size(158, 15);
        lblAfterDone.TabIndex = 17;
        lblAfterDone.Text = "When all downloads finish";
        //
        // cmbAfterDone
        //
        cmbAfterDone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cmbAfterDone.BackColor = Color.White;
        cmbAfterDone.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAfterDone.FlatStyle = FlatStyle.Flat;
        cmbAfterDone.ForeColor = Color.FromArgb(28, 34, 44);
        cmbAfterDone.FormattingEnabled = true;
        cmbAfterDone.Items.AddRange(new object[] { "None", "Shutdown", "Sleep", "Hibernate" });
        cmbAfterDone.Location = new Point(354, 351);
        cmbAfterDone.Name = "cmbAfterDone";
        cmbAfterDone.Size = new Size(150, 23);
        cmbAfterDone.TabIndex = 18;
        //
        // lblTheme
        //
        lblTheme.AutoSize = true;
        lblTheme.ForeColor = Color.FromArgb(28, 34, 44);
        lblTheme.Location = new Point(16, 384);
        lblTheme.Name = "lblTheme";
        lblTheme.Size = new Size(32, 15);
        lblTheme.TabIndex = 19;
        lblTheme.Text = "Skin";
        //
        // cmbTheme
        //
        cmbTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cmbTheme.BackColor = Color.White;
        cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbTheme.FlatStyle = FlatStyle.Flat;
        cmbTheme.ForeColor = Color.FromArgb(28, 34, 44);
        cmbTheme.FormattingEnabled = true;
        cmbTheme.Items.AddRange(new object[] { "Light", "Dark" });
        cmbTheme.Location = new Point(354, 381);
        cmbTheme.Name = "cmbTheme";
        cmbTheme.Size = new Size(150, 23);
        cmbTheme.TabIndex = 20;
        //
        // lblAccent
        //
        lblAccent.AutoSize = true;
        lblAccent.ForeColor = Color.FromArgb(28, 34, 44);
        lblAccent.Location = new Point(16, 412);
        lblAccent.Name = "lblAccent";
        lblAccent.Size = new Size(45, 15);
        lblAccent.TabIndex = 21;
        lblAccent.Text = "Accent";
        //
        // cmbAccent
        //
        cmbAccent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cmbAccent.BackColor = Color.White;
        cmbAccent.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAccent.FlatStyle = FlatStyle.Flat;
        cmbAccent.ForeColor = Color.FromArgb(28, 34, 44);
        cmbAccent.FormattingEnabled = true;
        cmbAccent.Items.AddRange(new object[] { "Blue", "Green", "Purple", "Orange" });
        cmbAccent.Location = new Point(354, 409);
        cmbAccent.Name = "cmbAccent";
        cmbAccent.Size = new Size(150, 23);
        cmbAccent.TabIndex = 22;
        //
        // btnCustomizeToolbar
        //
        btnCustomizeToolbar.BackColor = Color.White;
        btnCustomizeToolbar.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnCustomizeToolbar.FlatStyle = FlatStyle.Flat;
        btnCustomizeToolbar.ForeColor = Color.FromArgb(28, 34, 44);
        btnCustomizeToolbar.Location = new Point(16, 496);
        btnCustomizeToolbar.Name = "btnCustomizeToolbar";
        btnCustomizeToolbar.Size = new Size(180, 32);
        btnCustomizeToolbar.TabIndex = 23;
        btnCustomizeToolbar.Text = "Customize toolbar...";
        btnCustomizeToolbar.UseVisualStyleBackColor = false;
        btnCustomizeToolbar.Click += btnCustomizeToolbar_Click;
        //
        // chkTorAll
        //
        chkTorAll.AutoSize = true;
        chkTorAll.ForeColor = Color.FromArgb(28, 34, 44);
        chkTorAll.Location = new Point(16, 444);
        chkTorAll.Name = "chkTorAll";
        chkTorAll.Size = new Size(150, 19);
        chkTorAll.TabIndex = 24;
        chkTorAll.Text = "Route all via Tor";
        chkTorAll.UseVisualStyleBackColor = true;
        //
        // btnTorSetup
        //
        btnTorSetup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnTorSetup.BackColor = Color.White;
        btnTorSetup.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnTorSetup.FlatStyle = FlatStyle.Flat;
        btnTorSetup.ForeColor = Color.FromArgb(28, 34, 44);
        btnTorSetup.Location = new Point(354, 441);
        btnTorSetup.Name = "btnTorSetup";
        btnTorSetup.Size = new Size(150, 23);
        btnTorSetup.TabIndex = 25;
        btnTorSetup.Text = "Tor setup...";
        btnTorSetup.UseVisualStyleBackColor = false;
        btnTorSetup.Click += btnTorSetup_Click;
        //
        // chkStartWindows
        //
        chkStartWindows.AutoSize = true;
        chkStartWindows.ForeColor = Color.FromArgb(28, 34, 44);
        chkStartWindows.Location = new Point(16, 470);
        chkStartWindows.Name = "chkStartWindows";
        chkStartWindows.Size = new Size(150, 19);
        chkStartWindows.TabIndex = 26;
        chkStartWindows.Text = "Start with Windows";
        chkStartWindows.UseVisualStyleBackColor = true;
        //
        // chkConfirmDl
        //
        chkConfirmDl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkConfirmDl.AutoSize = true;
        chkConfirmDl.ForeColor = Color.FromArgb(28, 34, 44);
        chkConfirmDl.Location = new Point(354, 470);
        chkConfirmDl.Name = "chkConfirmDl";
        chkConfirmDl.Size = new Size(150, 19);
        chkConfirmDl.TabIndex = 27;
        chkConfirmDl.Text = "Confirm downloads";
        chkConfirmDl.UseVisualStyleBackColor = true;
        //
        // btnOK
        //
        btnOK.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnOK.BackColor = Color.FromArgb(0, 112, 214);
        btnOK.FlatAppearance.BorderSize = 0;
        btnOK.FlatStyle = FlatStyle.Flat;
        btnOK.ForeColor = Color.White;
        btnOK.Location = new Point(318, 496);
        btnOK.Name = "btnOK";
        btnOK.Size = new Size(90, 32);
        btnOK.TabIndex = 28;
        btnOK.Text = "Save";
        btnOK.UseVisualStyleBackColor = false;
        btnOK.Click += btnOK_Click;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.BackColor = Color.White;
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnCancel.FlatStyle = FlatStyle.Flat;
        btnCancel.ForeColor = Color.FromArgb(28, 34, 44);
        btnCancel.Location = new Point(414, 496);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(90, 32);
        btnCancel.TabIndex = 29;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = false;
        btnCancel.Click += btnCancel_Click;
        //
        // SettingsForm
        //
        AcceptButton = btnOK;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(244, 246, 250);
        CancelButton = btnCancel;
        ClientSize = new Size(520, 544);
        Controls.Add(chkConfirmDl);
        Controls.Add(chkStartWindows);
        Controls.Add(btnTorSetup);
        Controls.Add(chkTorAll);
        Controls.Add(btnCustomizeToolbar);
        Controls.Add(cmbAccent);
        Controls.Add(lblAccent);
        Controls.Add(cmbTheme);
        Controls.Add(lblTheme);
        Controls.Add(cmbAfterDone);
        Controls.Add(lblAfterDone);
        Controls.Add(dtpScheduleStop);
        Controls.Add(chkScheduleStop);
        Controls.Add(dtpScheduleStart);
        Controls.Add(chkScheduleStart);
        Controls.Add(btnCancel);
        Controls.Add(btnOK);
        Controls.Add(chkNotifications);
        Controls.Add(chkAutoStart);
        Controls.Add(chkClipboard);
        Controls.Add(lblSpeedHint);
        Controls.Add(nudSpeedLimit);
        Controls.Add(lblSpeedLimit);
        Controls.Add(nudConnections);
        Controls.Add(lblConnections);
        Controls.Add(nudMaxConcurrent);
        Controls.Add(lblMaxConcurrent);
        Controls.Add(btnBrowse);
        Controls.Add(txtDownloadPath);
        Controls.Add(lblDownloadPath);
        Font = new Font("Segoe UI", 9F);
        ForeColor = Color.FromArgb(28, 34, 44);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "SettingsForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Settings";
        ((System.ComponentModel.ISupportInitialize)nudMaxConcurrent).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudConnections).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudSpeedLimit).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblDownloadPath;
    private TextBox txtDownloadPath;
    private Button btnBrowse;
    private Label lblMaxConcurrent;
    private NumericUpDown nudMaxConcurrent;
    private Label lblConnections;
    private NumericUpDown nudConnections;
    private Label lblSpeedLimit;
    private NumericUpDown nudSpeedLimit;
    private Label lblSpeedHint;
    private CheckBox chkClipboard;
    private CheckBox chkAutoStart;
    private CheckBox chkNotifications;
    private CheckBox chkScheduleStart;
    private DateTimePicker dtpScheduleStart;
    private CheckBox chkScheduleStop;
    private DateTimePicker dtpScheduleStop;
    private Label lblAfterDone;
    private ComboBox cmbAfterDone;
    private Label lblTheme;
    private ComboBox cmbTheme;
    private Label lblAccent;
    private ComboBox cmbAccent;
    private Button btnCustomizeToolbar;
    private CheckBox chkTorAll;
    private Button btnTorSetup;
    private CheckBox chkStartWindows;
    private CheckBox chkConfirmDl;
    private Button btnOK;
    private Button btnCancel;
}
