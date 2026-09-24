using System;
using System.Drawing;
using System.Windows.Forms;

namespace IfredrixDownloadManager;

/// <summary>Delete choice: list row only, or row plus downloaded files.</summary>
public sealed class DeleteConfirmForm : Form
{
    public enum Choice { Cancel, ListOnly, ListAndFile }

    public Choice Result { get; private set; } = Choice.Cancel;

    private static string T(string key) => Localization.T(key);

    public DeleteConfirmForm(string fileName)
    {
        Text = T("del.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 158);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var lbl = new Label
        {
            Text = string.Format(T("del.prompt"), fileName),
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(428, 66)
        };

        var btnFile = new Button
        {
            Text = T("del.listAndFile"), Location = new Point(16, 92), Size = new Size(170, 32),
            FlatStyle = FlatStyle.Flat
        };
        btnFile.Click += (_, _) => { Result = Choice.ListAndFile; DialogResult = DialogResult.OK; Close(); };
        var btnList = new Button
        {
            Text = T("del.listOnly"), Location = new Point(192, 92), Size = new Size(150, 32),
            FlatStyle = FlatStyle.Flat
        };
        btnList.Click += (_, _) => { Result = Choice.ListOnly; DialogResult = DialogResult.OK; Close(); };
        var btnCancel = new Button
        {
            Text = T("dlg.cancel"), Location = new Point(348, 92), Size = new Size(96, 32),
            FlatStyle = FlatStyle.Flat
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = btnFile;
        CancelButton = btnCancel;

        Controls.Add(lbl);
        Controls.Add(btnFile);
        Controls.Add(btnList);
        Controls.Add(btnCancel);

        Theme.StyleForm(this);
    }
}
