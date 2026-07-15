namespace Playgama.Packager;

// Create / edit / remove publisher profiles and save them to publishers.json (git-ignored).
internal sealed class PublishersDialog : Form
{
    private readonly string _root;
    private readonly List<PublisherProfile> _list;
    private PublisherProfile? _current;
    private bool _loading;

    private ListBox _lst = null!;
    private TextBox _name = null!, _publisher = null!, _pubDisplay = null!, _pfx = null!, _pfxPw = null!;
    private TextBox _clientId = null!, _serviceUrl = null!;

    public PublishersDialog(string root)
    {
        _root = root;
        _list = PublisherStore.Load(root);

        Text = "Manage publishers";
        Width = 720;
        Height = 460;
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        RefreshList();
    }

    private void BuildUi()
    {
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var btnSave = new Button { Text = "Save", AutoSize = true, Padding = new Padding(14, 4, 14, 4) };
        var btnCancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(8, 0, 0, 0) };
        btnSave.Click += (_, _) => { PublisherStore.Save(_root, _list); DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnCancel);
        Controls.Add(bottom);

        var left = new Panel { Dock = DockStyle.Left, Width = 220, Padding = new Padding(8) };
        _lst = new ListBox { Dock = DockStyle.Fill };
        _lst.SelectedIndexChanged += (_, _) => LoadSelected();
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var btnAdd = new Button { Text = "Add", AutoSize = true };
        var btnRemove = new Button { Text = "Remove", AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
        btnAdd.Click += (_, _) => AddProfile();
        btnRemove.Click += (_, _) => RemoveProfile();
        leftButtons.Controls.Add(btnAdd);
        leftButtons.Controls.Add(btnRemove);
        left.Controls.Add(_lst);
        left.Controls.Add(leftButtons);
        Controls.Add(left);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _name = AddField(grid, "Profile name");
        _publisher = AddField(grid, "Publisher (CN=…)");
        _pubDisplay = AddField(grid, "Publisher display name");
        _pfx = AddFieldWithBrowse(grid, "Certificate (.pfx)");
        _pfxPw = AddField(grid, "Certificate password");
        _clientId = AddField(grid, "Client ID (Azure AD)");
        _serviceUrl = AddField(grid, "Service ticket URL");

        _name.TextChanged += (_, _) => { if (!_loading && _current != null) { _current.Name = _name.Text; _lst.Invalidate(); } };
        _publisher.TextChanged += (_, _) => Set(p => p.Publisher = _publisher.Text);
        _pubDisplay.TextChanged += (_, _) => Set(p => p.PublisherDisplayName = _pubDisplay.Text);
        _pfx.TextChanged += (_, _) => Set(p => p.Pfx = _pfx.Text);
        _pfxPw.TextChanged += (_, _) => Set(p => p.PfxPassword = _pfxPw.Text);
        _clientId.TextChanged += (_, _) => Set(p => p.ClientId = _clientId.Text);
        _serviceUrl.TextChanged += (_, _) => Set(p => p.ServiceTicketBaseUrl = _serviceUrl.Text);

        var rightHost = new Panel { Dock = DockStyle.Fill };
        rightHost.Controls.Add(grid);
        Controls.Add(rightHost);
        rightHost.BringToFront();
    }

    private void Set(Action<PublisherProfile> apply)
    {
        if (_loading || _current is null) return;
        apply(_current);
    }

    private static TextBox AddField(TableLayoutPanel t, string label)
    {
        int r = t.RowCount;
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, r);
        var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
        t.Controls.Add(box, 1, r);
        t.RowCount = r + 1;
        return box;
    }

    private TextBox AddFieldWithBrowse(TableLayoutPanel t, string label)
    {
        int r = t.RowCount;
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, r);
        var panel = new Panel { Dock = DockStyle.Fill, Height = 28, Margin = new Padding(3, 3, 3, 3) };
        var btn = new Button { Text = "Browse…", Dock = DockStyle.Right, Width = 90 };
        var box = new TextBox { Dock = DockStyle.Fill };
        btn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Certificate (*.pfx)|*.pfx|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var full = dlg.FileName;
                box.Text = full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(_root, full)
                    : full;
            }
        };
        panel.Controls.Add(box);
        panel.Controls.Add(btn);
        t.Controls.Add(panel, 1, r);
        t.RowCount = r + 1;
        return box;
    }

    private void RefreshList()
    {
        _lst.Items.Clear();
        foreach (var p in _list) _lst.Items.Add(p);
        if (_lst.Items.Count > 0) _lst.SelectedIndex = 0;
        else LoadSelected();
    }

    private void LoadSelected()
    {
        _current = _lst.SelectedItem as PublisherProfile;

        _loading = true;
        _name.Text = _current?.Name ?? "";
        _publisher.Text = _current?.Publisher ?? "";
        _pubDisplay.Text = _current?.PublisherDisplayName ?? "";
        _pfx.Text = _current?.Pfx ?? "";
        _pfxPw.Text = _current?.PfxPassword ?? "";
        _clientId.Text = _current?.ClientId ?? "";
        _serviceUrl.Text = _current?.ServiceTicketBaseUrl ?? "";
        _loading = false;

        bool enabled = _current != null;
        foreach (var c in new Control[] { _name, _publisher, _pubDisplay, _pfx, _pfxPw, _clientId, _serviceUrl })
            c.Enabled = enabled;
    }

    private void AddProfile()
    {
        var p = new PublisherProfile { Name = "New publisher", PfxPassword = "11111111", ServiceTicketBaseUrl = "https://playgama.com" };
        _list.Add(p);
        _lst.Items.Add(p);
        _lst.SelectedItem = p;
    }

    private void RemoveProfile()
    {
        if (_current is null) return;
        int i = _lst.SelectedIndex;
        _list.Remove(_current);
        _lst.Items.Remove(_current);
        _current = null;
        if (_lst.Items.Count > 0) _lst.SelectedIndex = Math.Min(i, _lst.Items.Count - 1);
        else LoadSelected();
    }
}
