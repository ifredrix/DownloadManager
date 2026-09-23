using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

partial class MainForm
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
        txtUrl = new TextBox();
        btnAddUrl = new Button();
        btnAddTorrent = new Button();
        btnGrabLinks = new Button();
        btnStream = new Button();
        btnBrowsers = new Button();
        btnSettings = new Button();
        dataGridView = new DataGridView();
        btnStart = new Button();
        btnPause = new Button();
        btnResume = new Button();
        btnCancel = new Button();
        btnRemove = new Button();
        btnOpenFolder = new Button();
        txtSearch = new TextBox();
        lblSearch = new Label();
        cmbCategoryFilter = new ComboBox();
        lblCategoryFilter = new Label();
        lblStatus = new Label();
        lblDownloadCount = new Label();
        statusStrip = new StatusStrip();
        toolStripStatusLabel1 = new ToolStripStatusLabel();
        toolStripStatusLabel2 = new ToolStripStatusLabel();
        toolStripStatusLabel3 = new ToolStripStatusLabel();
        ((System.ComponentModel.ISupportInitialize)dataGridView).BeginInit();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // txtUrl
        //
        txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtUrl.BackColor = Color.White;
        txtUrl.BorderStyle = BorderStyle.FixedSingle;
        txtUrl.Font = new Font("Segoe UI", 10F);
        txtUrl.ForeColor = Color.FromArgb(28, 34, 44);
        txtUrl.Location = new Point(12, 40);
        // Multiline so the box can be as tall as the toolbar buttons; a
        // single-line TextBox would always snap back to its font height.
        txtUrl.Multiline = true;
        txtUrl.WordWrap = false;
        txtUrl.Name = "txtUrl";
        txtUrl.Size = new Size(426, 30);
        txtUrl.TabIndex = 0;
        txtUrl.KeyDown += txtUrl_KeyDown;
        //
        // btnAddUrl
        //
        btnAddUrl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAddUrl.BackColor = Color.FromArgb(0, 112, 214);
        btnAddUrl.FlatAppearance.BorderSize = 0;
        btnAddUrl.FlatStyle = FlatStyle.Flat;
        btnAddUrl.ForeColor = Color.White;
        btnAddUrl.Location = new Point(446, 40);
        btnAddUrl.Name = "btnAddUrl";
        btnAddUrl.Size = new Size(104, 30);
        btnAddUrl.TabIndex = 1;
        btnAddUrl.Text = "Add URL";
        btnAddUrl.UseVisualStyleBackColor = false;
        btnAddUrl.Click += btnAddUrl_Click;
        //
        // btnAddTorrent
        //
        btnAddTorrent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAddTorrent.BackColor = Color.White;
        btnAddTorrent.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnAddTorrent.FlatStyle = FlatStyle.Flat;
        btnAddTorrent.ForeColor = Color.FromArgb(28, 34, 44);
        btnAddTorrent.Location = new Point(554, 40);
        btnAddTorrent.Name = "btnAddTorrent";
        btnAddTorrent.Size = new Size(104, 30);
        btnAddTorrent.TabIndex = 2;
        btnAddTorrent.Text = "Add Torrent";
        btnAddTorrent.UseVisualStyleBackColor = false;
        btnAddTorrent.Click += btnAddTorrent_Click;
        //
        // btnGrabLinks
        //
        btnGrabLinks.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnGrabLinks.BackColor = Color.White;
        btnGrabLinks.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnGrabLinks.FlatStyle = FlatStyle.Flat;
        btnGrabLinks.ForeColor = Color.FromArgb(28, 34, 44);
        btnGrabLinks.Location = new Point(780, 40);
        btnGrabLinks.Name = "btnGrabLinks";
        btnGrabLinks.Size = new Size(104, 30);
        btnGrabLinks.TabIndex = 3;
        btnGrabLinks.Text = "Grab Links";
        btnGrabLinks.UseVisualStyleBackColor = false;
        btnGrabLinks.Click += btnGrabLinks_Click;
        //
        // btnStream
        //
        btnStream.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnStream.BackColor = Color.White;
        btnStream.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnStream.FlatStyle = FlatStyle.Flat;
        btnStream.ForeColor = Color.FromArgb(28, 34, 44);
        btnStream.Location = new Point(672, 40);
        btnStream.Name = "btnStream";
        btnStream.Size = new Size(104, 30);
        btnStream.TabIndex = 4;
        btnStream.Text = "Stream URL";
        btnStream.UseVisualStyleBackColor = false;
        btnStream.Click += btnStream_Click;
        //
        // btnBrowsers
        //
        btnBrowsers.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowsers.BackColor = Color.White;
        btnBrowsers.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnBrowsers.FlatStyle = FlatStyle.Flat;
        btnBrowsers.ForeColor = Color.FromArgb(28, 34, 44);
        btnBrowsers.Location = new Point(564, 40);
        btnBrowsers.Name = "btnBrowsers";
        btnBrowsers.Size = new Size(104, 30);
        btnBrowsers.TabIndex = 5;
        btnBrowsers.Text = "Browsers";
        btnBrowsers.UseVisualStyleBackColor = false;
        btnBrowsers.Click += btnBrowsers_Click;
        //
        // btnSettings
        //
        btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSettings.BackColor = Color.White;
        btnSettings.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnSettings.FlatStyle = FlatStyle.Flat;
        btnSettings.ForeColor = Color.FromArgb(28, 34, 44);
        btnSettings.Location = new Point(898, 40);
        btnSettings.Name = "btnSettings";
        btnSettings.Size = new Size(104, 30);
        btnSettings.TabIndex = 6;
        btnSettings.Text = "Settings";
        btnSettings.UseVisualStyleBackColor = false;
        btnSettings.Click += btnSettings_Click;
        //
        // dataGridView
        //
        dataGridView.AllowUserToAddRows = false;
        dataGridView.AllowUserToDeleteRows = false;
        dataGridView.AllowUserToResizeRows = false;
        dataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dataGridView.BackgroundColor = Color.White;
        dataGridView.BorderStyle = BorderStyle.FixedSingle;
        dataGridView.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        dataGridView.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        dataGridView.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(249, 250, 253),
            ForeColor = Color.FromArgb(78, 88, 105),
            SelectionBackColor = Color.FromArgb(249, 250, 253),
            SelectionForeColor = Color.FromArgb(78, 88, 105),
            Font = new Font("Segoe UI Semibold", 9F)
        };
        dataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dataGridView.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(28, 34, 44),
            SelectionBackColor = Color.FromArgb(214, 233, 253),
            SelectionForeColor = Color.FromArgb(20, 26, 36),
            WrapMode = DataGridViewTriState.False
        };
        dataGridView.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(250, 251, 253),
            ForeColor = Color.FromArgb(28, 34, 44),
            SelectionBackColor = Color.FromArgb(214, 233, 253),
            SelectionForeColor = Color.FromArgb(20, 26, 36),
            WrapMode = DataGridViewTriState.False
        };
        dataGridView.EnableHeadersVisualStyles = false;
        dataGridView.GridColor = Color.FromArgb(228, 233, 241);
        dataGridView.Location = new Point(12, 78);
        dataGridView.MultiSelect = false;
        dataGridView.Name = "dataGridView";
        dataGridView.ReadOnly = true;
        dataGridView.RowHeadersVisible = false;
        dataGridView.RowTemplate.Height = 28;
        dataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dataGridView.Size = new Size(976, 392);
        dataGridView.TabIndex = 6;
        dataGridView.CellDoubleClick += dataGridView_CellDoubleClick;
        dataGridView.SelectionChanged += dataGridView_SelectionChanged;
        //
        // btnStart
        //
        btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnStart.BackColor = Color.White;
        btnStart.Enabled = false;
        btnStart.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnStart.FlatStyle = FlatStyle.Flat;
        btnStart.ForeColor = Color.FromArgb(28, 34, 44);
        btnStart.Location = new Point(12, 498);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(88, 30);
        btnStart.TabIndex = 7;
        btnStart.Text = "Start";
        btnStart.UseVisualStyleBackColor = false;
        btnStart.Click += btnStart_Click;
        //
        // btnPause
        //
        btnPause.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnPause.BackColor = Color.White;
        btnPause.Enabled = false;
        btnPause.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnPause.FlatStyle = FlatStyle.Flat;
        btnPause.ForeColor = Color.FromArgb(28, 34, 44);
        btnPause.Location = new Point(106, 498);
        btnPause.Name = "btnPause";
        btnPause.Size = new Size(88, 30);
        btnPause.TabIndex = 8;
        btnPause.Text = "Pause";
        btnPause.UseVisualStyleBackColor = false;
        btnPause.Click += btnPause_Click;
        //
        // btnResume
        //
        btnResume.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnResume.BackColor = Color.White;
        btnResume.Enabled = false;
        btnResume.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnResume.FlatStyle = FlatStyle.Flat;
        btnResume.ForeColor = Color.FromArgb(28, 34, 44);
        btnResume.Location = new Point(200, 498);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(88, 30);
        btnResume.TabIndex = 9;
        btnResume.Text = "Resume";
        btnResume.UseVisualStyleBackColor = false;
        btnResume.Click += btnResume_Click;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnCancel.BackColor = Color.White;
        btnCancel.Enabled = false;
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnCancel.FlatStyle = FlatStyle.Flat;
        btnCancel.ForeColor = Color.FromArgb(206, 55, 62);
        btnCancel.Location = new Point(294, 498);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(88, 30);
        btnCancel.TabIndex = 10;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = false;
        btnCancel.Click += btnCancel_Click;
        //
        // btnRemove
        //
        btnRemove.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRemove.BackColor = Color.White;
        btnRemove.Enabled = false;
        btnRemove.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnRemove.FlatStyle = FlatStyle.Flat;
        btnRemove.ForeColor = Color.FromArgb(28, 34, 44);
        btnRemove.Location = new Point(388, 498);
        btnRemove.Name = "btnRemove";
        btnRemove.Size = new Size(88, 30);
        btnRemove.TabIndex = 11;
        btnRemove.Text = "Remove";
        btnRemove.UseVisualStyleBackColor = false;
        btnRemove.Click += btnRemove_Click;
        //
        // btnOpenFolder
        //
        btnOpenFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnOpenFolder.BackColor = Color.White;
        btnOpenFolder.FlatAppearance.BorderColor = Color.FromArgb(206, 213, 224);
        btnOpenFolder.FlatStyle = FlatStyle.Flat;
        btnOpenFolder.ForeColor = Color.FromArgb(28, 34, 44);
        btnOpenFolder.Location = new Point(482, 498);
        btnOpenFolder.Name = "btnOpenFolder";
        btnOpenFolder.Size = new Size(104, 30);
        btnOpenFolder.TabIndex = 12;
        btnOpenFolder.Text = "Open Folder";
        btnOpenFolder.UseVisualStyleBackColor = false;
        btnOpenFolder.Click += btnOpenFolder_Click;
        //
        // lblSearch
        //
        lblSearch.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        lblSearch.AutoSize = true;
        lblSearch.ForeColor = Color.FromArgb(94, 104, 120);
        lblSearch.Location = new Point(672, 474);
        lblSearch.Name = "lblSearch";
        lblSearch.Size = new Size(47, 15);
        lblSearch.TabIndex = 13;
        lblSearch.Text = "Search:";
        //
        // txtSearch
        //
        txtSearch.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        txtSearch.BackColor = Color.White;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;
        txtSearch.ForeColor = Color.FromArgb(28, 34, 44);
        txtSearch.Location = new Point(672, 498);
        txtSearch.Name = "txtSearch";
        txtSearch.Size = new Size(180, 23);
        txtSearch.TabIndex = 14;
        txtSearch.TextChanged += txtSearch_TextChanged;
        //
        // lblCategoryFilter
        //
        lblCategoryFilter.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        lblCategoryFilter.AutoSize = true;
        lblCategoryFilter.ForeColor = Color.FromArgb(94, 104, 120);
        lblCategoryFilter.Location = new Point(858, 474);
        lblCategoryFilter.Name = "lblCategoryFilter";
        lblCategoryFilter.Size = new Size(58, 15);
        lblCategoryFilter.TabIndex = 15;
        lblCategoryFilter.Text = "Category:";
        //
        // cmbCategoryFilter
        //
        cmbCategoryFilter.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cmbCategoryFilter.BackColor = Color.White;
        cmbCategoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbCategoryFilter.FlatStyle = FlatStyle.Flat;
        cmbCategoryFilter.ForeColor = Color.FromArgb(28, 34, 44);
        cmbCategoryFilter.FormattingEnabled = true;
        cmbCategoryFilter.Items.AddRange(new object[] { "All", "Video", "Audio", "Archive", "Document", "Application", "Image", "Torrent", "Other" });
        cmbCategoryFilter.Location = new Point(858, 498);
        cmbCategoryFilter.Name = "cmbCategoryFilter";
        cmbCategoryFilter.Size = new Size(130, 23);
        cmbCategoryFilter.TabIndex = 16;
        cmbCategoryFilter.SelectedIndexChanged += cmbCategoryFilter_SelectedIndexChanged;
        //
        // lblStatus
        //
        lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lblStatus.AutoEllipsis = true;
        lblStatus.AutoSize = false;
        lblStatus.ForeColor = Color.FromArgb(94, 104, 120);
        lblStatus.Location = new Point(12, 538);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(832, 15);
        lblStatus.TabIndex = 17;
        lblStatus.Text = "Ready";
        //
        // lblDownloadCount
        //
        lblDownloadCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        lblDownloadCount.AutoSize = false;
        lblDownloadCount.ForeColor = Color.FromArgb(94, 104, 120);
        lblDownloadCount.Location = new Point(852, 538);
        lblDownloadCount.Name = "lblDownloadCount";
        lblDownloadCount.Size = new Size(136, 15);
        lblDownloadCount.TabIndex = 18;
        lblDownloadCount.Text = "Downloads: 0";
        lblDownloadCount.TextAlign = ContentAlignment.MiddleRight;
        //
        // statusStrip
        //
        statusStrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusStrip.BackColor = Color.FromArgb(249, 250, 253);
        statusStrip.ForeColor = Color.FromArgb(94, 104, 120);
        statusStrip.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, toolStripStatusLabel2, toolStripStatusLabel3 });
        statusStrip.Location = new Point(0, 582);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1000, 24);
        statusStrip.SizingGrip = false;
        statusStrip.TabIndex = 19;
        //
        // toolStripStatusLabel1
        //
        toolStripStatusLabel1.ForeColor = Color.FromArgb(94, 104, 120);
        toolStripStatusLabel1.Name = "toolStripStatusLabel1";
        toolStripStatusLabel1.Size = new Size(52, 19);
        toolStripStatusLabel1.Text = "Speed:";
        //
        // toolStripStatusLabel2
        //
        toolStripStatusLabel2.ForeColor = Color.FromArgb(28, 34, 44);
        toolStripStatusLabel2.Name = "toolStripStatusLabel2";
        toolStripStatusLabel2.Size = new Size(94, 19);
        toolStripStatusLabel2.Text = "0 KB/s down";
        //
        // toolStripStatusLabel3
        //
        toolStripStatusLabel3.ForeColor = Color.FromArgb(94, 104, 120);
        toolStripStatusLabel3.Name = "toolStripStatusLabel3";
        toolStripStatusLabel3.Size = new Size(109, 19);
        toolStripStatusLabel3.Text = "Active: 0 | Queued: 0";
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(244, 246, 250);
        ClientSize = new Size(1000, 634);
        Controls.Add(statusStrip);
        Controls.Add(btnStream);
        Controls.Add(btnBrowsers);
        Controls.Add(lblDownloadCount);
        Controls.Add(lblStatus);
        Controls.Add(cmbCategoryFilter);
        Controls.Add(lblCategoryFilter);
        Controls.Add(txtSearch);
        Controls.Add(lblSearch);
        Controls.Add(btnOpenFolder);
        Controls.Add(btnRemove);
        Controls.Add(btnCancel);
        Controls.Add(btnResume);
        Controls.Add(btnPause);
        Controls.Add(btnStart);
        Controls.Add(dataGridView);
        Controls.Add(btnSettings);
        Controls.Add(btnGrabLinks);
        Controls.Add(btnAddTorrent);
        Controls.Add(btnAddUrl);
        Controls.Add(txtUrl);
        Font = new Font("Segoe UI", 9F);
        ForeColor = Color.FromArgb(28, 34, 44);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Margin = new Padding(3, 4, 3, 4);
        MinimumSize = new Size(780, 534);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ifredrix Download Manager";
        Load += MainForm_Load;
        ((System.ComponentModel.ISupportInitialize)dataGridView).EndInit();
        statusStrip.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TextBox txtUrl;
    private Button btnAddUrl;
    private Button btnAddTorrent;
    private Button btnGrabLinks;
    private Button btnStream;
    private Button btnBrowsers;
    private Button btnSettings;
    private DataGridView dataGridView;
    private Button btnStart;
    private Button btnPause;
    private Button btnResume;
    private Button btnCancel;
    private Button btnRemove;
    private Button btnOpenFolder;
    private TextBox txtSearch;
    private Label lblSearch;
    private ComboBox cmbCategoryFilter;
    private Label lblCategoryFilter;
    private Label lblStatus;
    private Label lblDownloadCount;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel toolStripStatusLabel1;
    private ToolStripStatusLabel toolStripStatusLabel2;
    private ToolStripStatusLabel toolStripStatusLabel3;
}
