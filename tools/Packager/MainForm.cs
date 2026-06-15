using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Playgama.Packager;

public sealed class MainForm : Form
{
    private static readonly XNamespace D = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace UAP = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace MP = "http://schemas.microsoft.com/appx/2014/phone/manifest";

    private readonly string _root;
    private readonly string _manifest;
    private readonly string _appSettings;
    private readonly string _assets;
    private readonly string _gameDir;

    private TextBox _txtGame = null!, _txtIcon = null!, _txtTitle = null!, _txtVersion = null!;
    private TextBox _txtIdName = null!, _txtPublisher = null!, _txtPubDisplay = null!;
    private TextBox _txtClientId = null!, _txtServiceUrl = null!, _txtOutput = null!;
    private RadioButton _rbLocal = null!, _rbStore = null!;
    private Button _btnBuild = null!;
    private readonly List<Control> _storeOnly = new();

    public MainForm()
    {
        _root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
        _manifest = Path.Combine(_root, "Package.appxmanifest");
        _appSettings = Path.Combine(_root, "appsettings.json");
        _assets = Path.Combine(_root, "Assets");
        _gameDir = Path.Combine(_assets, "game");

        Text = "Playgama Bridge — Packager";
        Width = 860;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        LoadDefaults();
    }

    // ---------------------------------------------------------------- UI
    private void BuildUi()
    {
        var rootGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(rootGrid);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Build type FIRST — it controls which fields are shown.
        AddSection(fields, "Build type");
        var typePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _rbLocal = new RadioButton { Text = "Local test (.msix, signed)", AutoSize = true, Checked = true };
        _rbStore = new RadioButton { Text = "Microsoft Store (.msixbundle, x64+arm64)", AutoSize = true, Margin = new Padding(20, 3, 3, 3) };
        typePanel.Controls.Add(_rbLocal);
        typePanel.Controls.Add(_rbStore);
        AddRow(fields, "", typePanel);
        _rbLocal.CheckedChanged += (_, _) => UpdateMode();
        _rbStore.CheckedChanged += (_, _) => UpdateMode();

        AddSection(fields, "Game");
        AddRow(fields, "Game folder (has index.html)", BrowseRow(out _txtGame, "Browse…", (_, _) => PickFolder(_txtGame)));
        AddRow(fields, "App icon (square PNG, optional)", BrowseRow(out _txtIcon, "Browse…", (_, _) => PickFile(_txtIcon, "PNG images|*.png")));

        AddSection(fields, "App info");
        AddRow(fields, "Game title", _txtTitle = NewText());
        AddRow(fields, "Version (e.g. 1.0.0)", _txtVersion = NewText());

        // Store-only fields (hidden for local test builds).
        var storeHeader = AddSection(fields, "Microsoft Store identity (from Partner Center)");
        var lblName = AddRow(fields, "Identity Name", _txtIdName = NewText());
        var lblPub = AddRow(fields, "Publisher (CN=…)", _txtPublisher = NewText());
        var lblDisp = AddRow(fields, "Publisher display name", _txtPubDisplay = NewText());
        _storeOnly.AddRange(new Control[] { storeHeader, lblName, _txtIdName, lblPub, _txtPublisher, lblDisp, _txtPubDisplay });

        AddSection(fields, "Bridge config");
        AddRow(fields, "clientId", _txtClientId = NewText());
        AddRow(fields, "serviceTicketBaseUrl", _txtServiceUrl = NewText());

        rootGrid.Controls.Add(fields, 0, 0);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
        _btnBuild = new Button { Text = "Build package", AutoSize = true, Padding = new Padding(16, 6, 16, 6) };
        _btnBuild.Click += async (_, _) => await OnBuildAsync();
        var btnOpen = new Button { Text = "Open output folder", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(12, 3, 3, 3) };
        btnOpen.Click += (_, _) => OpenOutputFolder();
        actions.Controls.Add(_btnBuild);
        actions.Controls.Add(btnOpen);
        rootGrid.Controls.Add(actions, 0, 1);

        _txtOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.Black,
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 9f),
            WordWrap = false
        };
        rootGrid.Controls.Add(_txtOutput, 0, 2);
    }

    private static TextBox NewText() => new() { Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };

    private static Label AddSection(TableLayoutPanel t, string title)
    {
        var lbl = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 2), ForeColor = Color.DimGray };
        int row = t.RowCount;
        t.Controls.Add(lbl, 0, row);
        t.SetColumnSpan(lbl, 2);
        t.RowCount = row + 1;
        return lbl;
    }

    private static Label AddRow(TableLayoutPanel t, string label, Control control)
    {
        int row = t.RowCount;
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) };
        t.Controls.Add(lbl, 0, row);
        t.Controls.Add(control, 1, row);
        t.RowCount = row + 1;
        return lbl;
    }

    private void UpdateMode()
    {
        bool store = _rbStore.Checked;
        foreach (var c in _storeOnly) c.Visible = store;
        if (_btnBuild != null) _btnBuild.Text = store ? "Build Store package" : "Build test package";
    }

    private static Panel BrowseRow(out TextBox box, string buttonText, EventHandler onBrowse)
    {
        var p = new Panel { Dock = DockStyle.Fill, Height = 28, Margin = new Padding(3, 3, 3, 3) };
        var btn = new Button { Text = buttonText, Dock = DockStyle.Right, Width = 90 };
        btn.Click += onBrowse;
        box = new TextBox { Dock = DockStyle.Fill };
        p.Controls.Add(box);
        p.Controls.Add(btn);
        return p;
    }

    private void PickFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog();
        if (Directory.Exists(target.Text)) dlg.InitialDirectory = target.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    private void PickFile(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
    }

    // ---------------------------------------------------------------- defaults
    private void LoadDefaults()
    {
        _txtGame.Text = _gameDir;
        try
        {
            var doc = XDocument.Load(_manifest);
            var pkg = doc.Root!;
            var id = pkg.Element(D + "Identity");
            var props = pkg.Element(D + "Properties");
            var ve = pkg.Element(D + "Applications")?.Element(D + "Application")?.Element(UAP + "VisualElements");
            _txtIdName.Text = (string?)id?.Attribute("Name") ?? "";
            _txtPublisher.Text = (string?)id?.Attribute("Publisher") ?? "";
            var ver = (string?)id?.Attribute("Version") ?? "1.0.0.0";
            _txtVersion.Text = string.Join('.', ver.Split('.').Take(3));
            _txtPubDisplay.Text = (string?)props?.Element(D + "PublisherDisplayName") ?? "";
            _txtTitle.Text = (string?)ve?.Attribute("DisplayName") ?? (string?)props?.Element(D + "DisplayName") ?? "";
        }
        catch { /* keep blanks */ }

        try
        {
            if (File.Exists(_appSettings))
            {
                var json = JsonNode.Parse(File.ReadAllText(_appSettings)) as JsonObject;
                _txtClientId.Text = (string?)json?["clientId"] ?? "";
                _txtServiceUrl.Text = (string?)json?["serviceTicketBaseUrl"] ?? "";
            }
        }
        catch { }

        UpdateMode();
        Log("Ready. Choose a build type, fill in the fields, and press Build.");
        Log($"Project: {_root}");
    }

    // ---------------------------------------------------------------- build
    private async Task OnBuildAsync()
    {
        var game = _txtGame.Text.Trim();
        if (!Directory.Exists(game) || !File.Exists(Path.Combine(game, "index.html")))
        {
            MessageBox.Show(this, "Choose a game folder that contains index.html.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_txtVersion.Text))
        {
            MessageBox.Show(this, "Enter a version (e.g. 1.0.0).", "Version", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool store = _rbStore.Checked;
        _btnBuild.Enabled = false;
        _txtOutput.Clear();

        try
        {
            await Task.Run(() => Prepare(game, store));

            var script = store ? "build-store.ps1" : "build.ps1";
            var args = store ? $"-Version {NormalizeVersion(_txtVersion.Text, storeZero: true)}" : "";
            Log($"\n--- Running {script} ---\n");
            int code = await RunScriptAsync(script, args);

            if (code == 0)
            {
                var outFolder = store ? "dist-store" : "dist";
                Log($"\n✔ DONE. Package is in the \"{outFolder}\" folder.");
                MessageBox.Show(this, $"Build finished. See the \"{outFolder}\" folder.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log($"\n✖ Build failed (exit {code}). See messages above.");
                MessageBox.Show(this, "Build failed. See the log for details.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Log($"\nERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnBuild.Enabled = true;
        }
    }

    private void Prepare(string game, bool store)
    {
        // 1. Manifest
        Log("Updating Package.appxmanifest…");
        var doc = XDocument.Load(_manifest);
        var pkg = doc.Root!;
        var id = pkg.Element(D + "Identity")!;
        if (!string.IsNullOrWhiteSpace(_txtIdName.Text)) id.SetAttributeValue("Name", _txtIdName.Text.Trim());
        if (!string.IsNullOrWhiteSpace(_txtPublisher.Text)) id.SetAttributeValue("Publisher", _txtPublisher.Text.Trim());
        id.SetAttributeValue("Version", NormalizeVersion(_txtVersion.Text, storeZero: store));

        var props = pkg.Element(D + "Properties")!;
        if (!string.IsNullOrWhiteSpace(_txtTitle.Text)) props.Element(D + "DisplayName")!.Value = _txtTitle.Text.Trim();
        if (!string.IsNullOrWhiteSpace(_txtPubDisplay.Text)) props.Element(D + "PublisherDisplayName")!.Value = _txtPubDisplay.Text.Trim();

        var ve = pkg.Element(D + "Applications")?.Element(D + "Application")?.Element(UAP + "VisualElements");
        if (ve is not null && !string.IsNullOrWhiteSpace(_txtTitle.Text))
        {
            ve.SetAttributeValue("DisplayName", _txtTitle.Text.Trim());
            ve.SetAttributeValue("Description", _txtTitle.Text.Trim());
        }
        // Remove legacy PhoneIdentity (not needed for desktop, breaks non-GUID Store names).
        pkg.Element(MP + "PhoneIdentity")?.Remove();
        doc.Save(_manifest);

        // 2. appsettings.json
        Log("Writing appsettings.json…");
        var json = new JsonObject
        {
            ["clientId"] = _txtClientId.Text.Trim(),
            ["serviceTicketBaseUrl"] = string.IsNullOrWhiteSpace(_txtServiceUrl.Text) ? "https://playgama.com" : _txtServiceUrl.Text.Trim()
        };
        File.WriteAllText(_appSettings, json.ToString());

        // 3. Game files
        if (!PathsEqual(game, _gameDir))
        {
            Log("Copying game into Assets\\game…");
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, recursive: true);
            CopyDir(game, _gameDir);
        }
        else
        {
            Log("Using existing Assets\\game.");
        }

        // 4. Icon -> logos
        var icon = _txtIcon.Text.Trim();
        if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
        {
            Log("Generating logos from icon…");
            GenerateLogos(icon, _assets);
        }
    }

    private async Task<int> RunScriptAsync(string scriptName, string extraArgs)
    {
        var script = Path.Combine(_root, scriptName);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {extraArgs}",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        var tcs = new TaskCompletionSource<int>();
        p.Exited += (_, _) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return await tcs.Task;
    }

    // ---------------------------------------------------------------- helpers
    private void OpenOutputFolder()
    {
        var folder = Path.Combine(_root, _rbStore.Checked ? "dist-store" : "dist");
        if (!Directory.Exists(folder)) { MessageBox.Show(this, "Nothing built yet.", "Output", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void Log(string line)
    {
        if (_txtOutput.InvokeRequired) { _txtOutput.BeginInvoke(new Action(() => Log(line))); return; }
        _txtOutput.AppendText(line + Environment.NewLine);
    }

    private static string NormalizeVersion(string v, bool storeZero)
    {
        var parts = (v ?? "").Split('.').Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
        while (parts.Count < 4) parts.Add(0);
        if (storeZero) parts[3] = 0;
        return string.Join('.', parts.Take(4));
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(src, dst), overwrite: true);
    }

    private static string? FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PlaygamaBridgeMicrosoftStore.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        return null;
    }

    // ---- logo generation -------------------------------------------------
    private static void GenerateLogos(string srcPng, string assetsDir)
    {
        using var src = new Bitmap(srcPng);
        SaveSquare(src, Path.Combine(assetsDir, "Square44x44Logo.png"), 44);
        SaveSquare(src, Path.Combine(assetsDir, "Square150x150Logo.png"), 150);
        SaveSquare(src, Path.Combine(assetsDir, "StoreLogo.png"), 50);
        SaveCanvas(src, Path.Combine(assetsDir, "Wide310x150Logo.png"), 310, 150);
        SaveCanvas(src, Path.Combine(assetsDir, "SplashScreen.png"), 620, 300);
    }

    private static void SaveSquare(Bitmap src, string path, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = NewGraphics(bmp))
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        bmp.Save(path, ImageFormat.Png);
    }

    private static void SaveCanvas(Bitmap src, string path, int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = NewGraphics(bmp))
        {
            double scale = Math.Min((double)w / src.Width, (double)h / src.Height);
            int dw = (int)(src.Width * scale), dh = (int)(src.Height * scale);
            g.DrawImage(src, new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh));
        }
        bmp.Save(path, ImageFormat.Png);
    }

    private static Graphics NewGraphics(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        return g;
    }
}
