using System.Diagnostics;

namespace AiKeyLauncher;

/// <summary>Small popup near the cursor listing every AI: open it, or get the app if it isn't installed.</summary>
sealed class PickerForm : Form
{
    public PickerForm(Settings settings)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Padding = new Padding(8);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label { Text = "Open with…", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 2, 4, 6) };
        table.Controls.Add(title, 0, 0);
        table.SetColumnSpan(title, 3);

        int row = 1;
        foreach (var p in Providers.All)
        {
            bool installed = p.IsInstalled;
            bool isDefault = p.Id == settings.DefaultProvider;

            var open = Button(installed ? $"{p.Name}" : $"{p.Name} (web)");
            if (isDefault) open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += (_, _) => { Close(); p.Launch(settings.AlwaysUseWeb); };
            table.Controls.Add(open, 0, row);

            if (installed)
            {
                var web = Button("Web"); web.Click += (_, _) => { Close(); p.Launch(forceWeb: true); };
                table.Controls.Add(web, 1, row);
            }
            else if (p.InstallUrl != p.WebUrl)
            {
                var get = Button("Get app"); get.ForeColor = Color.FromArgb(120, 190, 255);
                get.Click += (_, _) => { Close(); Process.Start(new ProcessStartInfo(p.InstallUrl) { UseShellExecute = true }); };
                table.Controls.Add(get, 1, row);
            }

            var def = Button(isDefault ? "Default" : "Make default");
            def.Enabled = !isDefault;
            def.Click += (_, _) => { settings.DefaultProvider = p.Id; settings.Save(); Close(); };
            table.Controls.Add(def, 2, row);
            row++;
        }
        Controls.Add(table);

        Deactivate += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (_, _) => Activate();
    }

    static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(2),
        BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.White, MinimumSize = new Size(0, 30),
        FlatAppearance = { BorderColor = Color.FromArgb(70, 70, 70) },
    };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var pt = Cursor.Position;
        var area = Screen.FromPoint(pt).WorkingArea;
        Location = new Point(
            Math.Min(Math.Max(pt.X - Width / 2, area.Left), area.Right - Width),
            Math.Min(Math.Max(pt.Y - 20, area.Top), area.Bottom - Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(90, 90, 90));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
