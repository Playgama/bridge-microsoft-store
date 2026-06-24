using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Linq;

namespace Playgama.Packager;

public sealed class MainForm : Form
{
    private static readonly XNamespace D = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace UAP = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace MP = "http://schemas.microsoft.com/appx/2014/phone/manifest";

    private readonly string _root;
    private readonly string _manifest;
    private readonly string _assets;
    private readonly string _gameDir;

    private TextBox _txtGame = null!, _txtIcon = null!, _txtTitle = null!, _txtVersion = null!;
    private TextBox _txtIdName = null!, _txtPublisher = null!, _txtPubDisplay = null!, _txtOutput = null!;
    private ComboBox _cmbPublisher = null!;
    private Button _btnLocal = null!, _btnStore = null!, _btnBoth = null!;

    private List<PublisherProfile> _publishers = new();
    private PublisherProfile? _selectedProfile;
    private string _lastOutputDir = "dist";

    private sealed class BuildInputs
    {
        public bool Store;
        public string Game = "", Icon = "", Title = "", Version = "";
        public string IdName = "", Publisher = "", PubDisplay = "";
    }

    public MainForm()
    {
        _root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
        _manifest = Path.Combine(_root, "Package.appxmanifest");
        _assets = Path.Combine(_root, "Assets");
        _gameDir = Path.Combine(_assets, "game");

        Text = "Playgama Bridge — Packager";
        Width = 900;
        Height = 760;
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        LoadDefaults();
    }

    // ---------------------------------------------------------------- UI
    private void BuildUi()
    {
        var rootGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // fields (scrollable host)
        rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons
        rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // console
        Controls.Add(rootGrid);

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(fields, "Publisher profile (optional)");
        var pubPanel = new Panel { Dock = DockStyle.Fill, Height = 28, Margin = new Padding(3, 3, 3, 3) };
        var btnManage = new Button { Text = "Manage…", Dock = DockStyle.Right, Width = 90 };
        btnManage.Click += (_, _) => OpenPublishersDialog();
        _cmbPublisher = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbPublisher.SelectedIndexChanged += (_, _) => OnPublisherSelected();
        pubPanel.Controls.Add(_cmbPublisher);
        pubPanel.Controls.Add(btnManage);
        AddRow(fields, "Publisher", pubPanel);
        AddRow(fields, "Publisher (CN=…)", _txtPublisher = NewText());
        AddRow(fields, "Publisher display name", _txtPubDisplay = NewText());

        AddSection(fields, "Game");
        AddRow(fields, "Game folder (has index.html)", BrowseRow(out _txtGame, "Browse…", (_, _) => PickFolder(_txtGame)));
        AddRow(fields, "App icon (square PNG, optional)", BrowseRow(out _txtIcon, "Browse…", (_, _) => PickFile(_txtIcon, "PNG images|*.png")));

        AddSection(fields, "App info (per game)");
        AddRow(fields, "Game title", _txtTitle = NewText());
        AddRow(fields, "Identity Name (Partner Center)", _txtIdName = NewText());
        AddRow(fields, "Version (e.g. 1.0.0)", _txtVersion = NewText());

        AddSection(fields, "Config / in-app purchases");
        var btnCfg = new Button { Text = "Edit config & in-apps…", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        btnCfg.Click += (_, _) => OpenConfigEditor();
        AddRow(fields, "playgama-bridge-config.json", btnCfg);

        var fieldsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Height = 320 };
        fieldsHost.Controls.Add(fields);
        rootGrid.Controls.Add(fieldsHost, 0, 0);

        // Build buttons
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
        _btnLocal = new Button { Text = "Build test (.msix)", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
        _btnStore = new Button { Text = "Build Store (.msixbundle)", AutoSize = true, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(10, 3, 3, 3) };
        _btnBoth = new Button { Text = "Build test + Store", AutoSize = true, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(10, 3, 3, 3) };
        var btnOpen = new Button { Text = "Open output folder", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(18, 3, 3, 3) };
        _btnLocal.Click += async (_, _) => await BuildAsync(both: false, store: false);
        _btnStore.Click += async (_, _) => await BuildAsync(both: false, store: true);
        _btnBoth.Click += async (_, _) => await BuildAsync(both: true, store: false);
        btnOpen.Click += (_, _) => OpenOutputFolder();
        actions.Controls.Add(_btnLocal);
        actions.Controls.Add(_btnStore);
        actions.Controls.Add(_btnBoth);
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

    private static void AddSection(TableLayoutPanel t, string title)
    {
        var lbl = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 2), ForeColor = Color.DimGray };
        int row = t.RowCount;
        t.Controls.Add(lbl, 0, row);
        t.SetColumnSpan(lbl, 2);
        t.RowCount = row + 1;
    }

    private static void AddRow(TableLayoutPanel t, string label, Control control)
    {
        int row = t.RowCount;
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, row);
        t.Controls.Add(control, 1, row);
        t.RowCount = row + 1;
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

    // ---------------------------------------------------------------- defaults / publishers
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

        LoadPublishers();
        Log("Ready. Pick a publisher, fill the fields, edit config/in-apps, then build.");
        Log($"Project: {_root}");
    }

    private void LoadPublishers()
    {
        _publishers = PublisherStore.Load(_root);
        _cmbPublisher.Items.Clear();
        _cmbPublisher.Items.Add("(manual / none)");
        foreach (var p in _publishers) _cmbPublisher.Items.Add(p.Name);

        var last = LocalSettings.GetLastPublisher();
        int idx = _publishers.FindIndex(p => p.Name == last);
        _cmbPublisher.SelectedIndex = idx >= 0 ? idx + 1 : 0;
    }

    private void OpenPublishersDialog()
    {
        using var dlg = new PublishersDialog(_root);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            LoadPublishers();
            Log("Publishers saved to publishers.json (local, git-ignored).");
        }
    }

    private void OnPublisherSelected()
    {
        int i = _cmbPublisher.SelectedIndex;
        if (i <= 0) { _selectedProfile = null; return; }

        var p = _publishers[i - 1];
        _selectedProfile = p;
        // Publisher identity comes from the profile; Identity Name stays per-game (not overwritten).
        if (!string.IsNullOrWhiteSpace(p.Publisher)) _txtPublisher.Text = p.Publisher!;
        if (!string.IsNullOrWhiteSpace(p.PublisherDisplayName)) _txtPubDisplay.Text = p.PublisherDisplayName!;
        LocalSettings.SetLastPublisher(p.Name);
        Log($"Publisher selected: {p.Name}");
    }

    private void OpenConfigEditor()
    {
        var game = _txtGame.Text.Trim();
        if (!Directory.Exists(game))
        {
            MessageBox.Show(this, "Choose a valid game folder first.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new ConfigEditorForm(game);
        dlg.ShowDialog(this);
    }

    // ---------------------------------------------------------------- build
    private BuildInputs? Gather(bool store)
    {
        var i = new BuildInputs
        {
            Store = store,
            Game = _txtGame.Text.Trim(),
            Icon = _txtIcon.Text.Trim(),
            Title = _txtTitle.Text.Trim(),
            Version = _txtVersion.Text.Trim(),
            IdName = _txtIdName.Text.Trim(),
            Publisher = _txtPublisher.Text.Trim(),
            PubDisplay = _txtPubDisplay.Text.Trim()
        };

        if (!Directory.Exists(i.Game) || !File.Exists(Path.Combine(i.Game, "index.html")))
        {
            MessageBox.Show(this, "Choose a game folder that contains index.html.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (string.IsNullOrWhiteSpace(i.Version))
        {
            MessageBox.Show(this, "Enter a version (e.g. 1.0.0).", "Version", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return i;
    }

    private async Task BuildAsync(bool both, bool store)
    {
        var builds = new List<BuildInputs>();
        if (both)
        {
            var a = Gather(false); if (a == null) return;
            var b = Gather(true); if (b == null) return;
            builds.Add(a); builds.Add(b);
        }
        else
        {
            var a = Gather(store); if (a == null) return;
            builds.Add(a);
        }

        SetBuildButtons(false);
        _txtOutput.Clear();
        bool allOk = true;
        try
        {
            foreach (var i in builds)
            {
                int code = await RunOneAsync(i);
                _lastOutputDir = i.Store ? "dist-store" : "dist";
                if (code == 0) Log($"\n✔ {(i.Store ? "Store" : "Test")} build done → {_lastOutputDir}");
                else { Log($"\n✖ {(i.Store ? "Store" : "Test")} build failed (exit {code})."); allOk = false; break; }
            }
            MessageBox.Show(this,
                allOk ? "Build finished." : "Build failed — see the log.",
                allOk ? "Done" : "Failed",
                MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Log($"\nERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBuildButtons(true);
        }
    }

    private void SetBuildButtons(bool enabled)
    {
        _btnLocal.Enabled = _btnStore.Enabled = _btnBoth.Enabled = enabled;
    }

    private async Task<int> RunOneAsync(BuildInputs i)
    {
        await Task.Run(() => Prepare(i));
        var script = i.Store ? "build-store.ps1" : "build.ps1";
        var args = i.Store ? $"-Version {NormalizeVersion(i.Version, storeZero: true)}" : CertArgs();
        Log($"\n--- Running {script} ---\n");
        return await RunScriptAsync(script, args);
    }

    private string CertArgs()
    {
        if (_selectedProfile?.Pfx is string pfxRel && !string.IsNullOrWhiteSpace(pfxRel))
        {
            var pfxAbs = Path.GetFullPath(Path.Combine(_root, pfxRel));
            if (File.Exists(pfxAbs))
                return $" -PfxPath \"{pfxAbs}\" -PfxPassword \"{_selectedProfile.PfxPassword}\"";
            Log($"Note: certificate not found ({pfxAbs}); using the default signing cert.");
        }
        return "";
    }

    // Writes identity/title/version into the manifest, copies the game, generates logos.
    // The bridge config (in-apps/gameId) is edited in the Config editor and lives in the game folder.
    private void Prepare(BuildInputs i)
    {
        Log("Updating Package.appxmanifest…");
        var doc = XDocument.Load(_manifest);
        var pkg = doc.Root!;
        var id = pkg.Element(D + "Identity")!;
        if (!string.IsNullOrWhiteSpace(i.IdName)) id.SetAttributeValue("Name", i.IdName);
        if (!string.IsNullOrWhiteSpace(i.Publisher)) id.SetAttributeValue("Publisher", i.Publisher);
        id.SetAttributeValue("Version", NormalizeVersion(i.Version, storeZero: i.Store));

        var props = pkg.Element(D + "Properties")!;
        if (!string.IsNullOrWhiteSpace(i.Title)) props.Element(D + "DisplayName")!.Value = i.Title;
        if (!string.IsNullOrWhiteSpace(i.PubDisplay)) props.Element(D + "PublisherDisplayName")!.Value = i.PubDisplay;

        var ve = pkg.Element(D + "Applications")?.Element(D + "Application")?.Element(UAP + "VisualElements");
        if (ve is not null && !string.IsNullOrWhiteSpace(i.Title))
        {
            ve.SetAttributeValue("DisplayName", i.Title);
            ve.SetAttributeValue("Description", i.Title);
        }
        pkg.Element(MP + "PhoneIdentity")?.Remove();
        doc.Save(_manifest);

        if (!PathsEqual(i.Game, _gameDir))
        {
            Log("Copying game into Assets\\game…");
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, recursive: true);
            CopyDir(i.Game, _gameDir);
        }
        else
        {
            Log("Using existing Assets\\game.");
        }

        if (!string.IsNullOrWhiteSpace(i.Icon) && File.Exists(i.Icon))
        {
            Log("Generating logos from icon…");
            GenerateLogos(i.Icon, _assets);
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
        var folder = Path.Combine(_root, _lastOutputDir);
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
    // Match the Visual Studio asset generator: unqualified (scale-100) + scale-200 for each logo,
    // and include the SmallTile (71x71) / LargeTile (310x310) the App Installer & tiles use.
    private static readonly int[] Scales = { 200 };

    private static void GenerateLogos(string srcPng, string assetsDir)
    {
        using var src = new Bitmap(srcPng);

        SaveSquareScaled(src, assetsDir, "Square44x44Logo", 44);
        SaveSquareScaled(src, assetsDir, "SmallTile", 71);          // Square71x71Logo
        SaveSquareScaled(src, assetsDir, "Square150x150Logo", 150);
        SaveSquareScaled(src, assetsDir, "LargeTile", 310);         // Square310x310Logo
        SaveSquareScaled(src, assetsDir, "StoreLogo", 50);
        SaveCanvasScaled(src, assetsDir, "Wide310x150Logo", 310, 150);
        SaveCanvasScaled(src, assetsDir, "SplashScreen", 620, 300);
        SaveSquareScaled(src, assetsDir, "LockScreenLogo", 24);

        foreach (var s in new[] { 16, 24, 32, 44, 48, 256 })
        {
            SaveSquare(src, Path.Combine(assetsDir, $"Square44x44Logo.targetsize-{s}.png"), s);
            SaveSquare(src, Path.Combine(assetsDir, $"Square44x44Logo.targetsize-{s}_altform-unplated.png"), s);
        }

        WriteIco(Path.Combine(assetsDir, "favicon.ico"), src, new[] { 16, 32, 48, 256 });
    }

    // Emit only scale-qualified files (no unqualified scale-100 base), like the VS generator.
    // build.ps1 makepri uses scale-200 as the default qualifier, so these resolve everywhere
    // (and the App Installer / tiles render full-size instead of falling back to a small base).
    private static void SaveSquareScaled(Bitmap src, string dir, string baseName, int baseSize)
    {
        foreach (var s in Scales)
            SaveSquare(src, Path.Combine(dir, $"{baseName}.scale-{s}.png"), (int)Math.Round(baseSize * s / 100.0));
    }

    private static void SaveCanvasScaled(Bitmap src, string dir, string baseName, int w, int h)
    {
        foreach (var s in Scales)
            SaveCanvas(src, Path.Combine(dir, $"{baseName}.scale-{s}.png"), (int)Math.Round(w * s / 100.0), (int)Math.Round(h * s / 100.0));
    }

    private static void WriteIco(string path, Bitmap src, int[] sizes)
    {
        var images = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = NewGraphics(bmp)) g.DrawImage(src, new Rectangle(0, 0, s, s));
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            images.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }

    private static void SaveSquare(Bitmap src, string path, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = NewGraphics(bmp))
        {
            // Center-crop the source to a square so non-square images aren't distorted.
            int side = Math.Min(src.Width, src.Height);
            int sx = (src.Width - side) / 2;
            int sy = (src.Height - side) / 2;
            g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
        }
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
