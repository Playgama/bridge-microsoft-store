using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
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
    private TextBox _txtClientId = null!, _txtServiceUrl = null!, _txtGameId = null!, _txtOutput = null!;
    private TextBox _txtBridgeConfig = null!;
    private TabControl _bottomTabs = null!;
    private TabPage _tabPayments = null!, _tabRaw = null!;
    private Panel _paymentsHost = null!;
    private readonly List<PaymentRowUi> _paymentRows = new();

    private sealed class PaymentRowUi
    {
        public string PaymentId = "";
        public TextBox StoreId = null!;
        public TextBox Amount = null!;
        public TextBox Desc = null!;
    }
    private RadioButton _rbLocal = null!, _rbStore = null!;
    private Button _btnBuild = null!;
    private readonly List<Control> _storeOnly = new();

    private ComboBox _cmbPublisher = null!;
    private List<PublisherProfile> _publishers = new();
    private PublisherProfile? _selectedProfile;

    public MainForm()
    {
        _root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
        _manifest = Path.Combine(_root, "Package.appxmanifest");
        _appSettings = Path.Combine(_root, "appsettings.json");
        _assets = Path.Combine(_root, "Assets");
        _gameDir = Path.Combine(_assets, "game");

        Text = "Playgama Bridge — Packager";
        Width = 880;
        Height = 820;
        MinimumSize = new Size(720, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        LoadDefaults();
    }

    // ---------------------------------------------------------------- UI
    private void BuildUi()
    {
        var rootGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // fields (scrollable)
        rootGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // build buttons
        rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); // config editor + log
        Controls.Add(rootGrid);

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
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

        AddSection(fields, "Publisher profile (optional)");
        var pubPanel = new Panel { Dock = DockStyle.Fill, Height = 28, Margin = new Padding(3, 3, 3, 3) };
        var btnManage = new Button { Text = "Manage…", Dock = DockStyle.Right, Width = 90 };
        btnManage.Click += (_, _) => OpenPublishersDialog();
        _cmbPublisher = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbPublisher.SelectedIndexChanged += (_, _) => OnPublisherSelected();
        pubPanel.Controls.Add(_cmbPublisher);
        pubPanel.Controls.Add(btnManage);
        AddRow(fields, "Publisher", pubPanel);

        AddSection(fields, "Game");
        AddRow(fields, "Game folder (has index.html)", BrowseRow(out _txtGame, "Browse…", (_, _) => PickFolder(_txtGame)));
        AddRow(fields, "App icon (square PNG, optional)", BrowseRow(out _txtIcon, "Browse…", (_, _) => PickFile(_txtIcon, "PNG images|*.png")));
        _txtGame.TextChanged += (_, _) => LoadBridgeConfig();

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
        AddRow(fields, "Store Game ID (platforms)", _txtGameId = NewText());
        _txtGameId.Leave += (_, _) => _txtGameId.Text = ExtractStoreId(_txtGameId.Text);

        // Host the fields in a scrollable panel so they never squeeze out the editor/log below.
        var fieldsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        fieldsHost.Controls.Add(fields);
        rootGrid.Controls.Add(fieldsHost, 0, 0);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
        _btnBuild = new Button { Text = "Build package", AutoSize = true, Padding = new Padding(16, 6, 16, 6) };
        _btnBuild.Click += async (_, _) => await OnBuildAsync();
        var btnUpdateCfg = new Button { Text = "Update config only (no build)", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(12, 3, 3, 3) };
        btnUpdateCfg.Click += (_, _) => UpdateConfigOnly();
        var btnOpen = new Button { Text = "Open output folder", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(12, 3, 3, 3) };
        btnOpen.Click += (_, _) => OpenOutputFolder();
        actions.Controls.Add(_btnBuild);
        actions.Controls.Add(btnUpdateCfg);
        actions.Controls.Add(btnOpen);
        rootGrid.Controls.Add(actions, 0, 1);

        // Bottom area: bridge-config editor (top) over the build log (bottom).
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, Panel1MinSize = 60, Panel2MinSize = 60 };

        var cfgHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        cfgHeader.Controls.Add(new Label { Text = "playgama-bridge-config.json", AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.DimGray, Margin = new Padding(3, 7, 12, 3) });
        var btnReloadCfg = new Button { Text = "Reload", AutoSize = true };
        var btnSaveCfg = new Button { Text = "Save", AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
        btnReloadCfg.Click += (_, _) => LoadBridgeConfig();
        btnSaveCfg.Click += (_, _) => SaveBridgeConfig(silent: false);
        cfgHeader.Controls.Add(btnReloadCfg);
        cfgHeader.Controls.Add(btnSaveCfg);

        _bottomTabs = new TabControl { Dock = DockStyle.Fill };

        _tabPayments = new TabPage("Payments");
        _paymentsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        _tabPayments.Controls.Add(_paymentsHost);

        _tabRaw = new TabPage("Raw JSON");
        _txtBridgeConfig = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9f), WordWrap = false, AcceptsTab = true };
        _tabRaw.Controls.Add(_txtBridgeConfig);

        _bottomTabs.TabPages.Add(_tabPayments);
        _bottomTabs.TabPages.Add(_tabRaw);
        _bottomTabs.Selecting += BottomTabs_Selecting;

        split.Panel1.Controls.Add(_bottomTabs);
        split.Panel1.Controls.Add(cfgHeader);

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
        split.Panel2.Controls.Add(_txtOutput);

        rootGrid.Controls.Add(split, 0, 2);
        Shown += (_, _) => { try { split.SplitterDistance = Math.Max(120, split.Height / 2); } catch { } };
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

    private void LoadPublishers()
    {
        _publishers = PublisherStore.Load(_root);
        _cmbPublisher.Items.Clear();
        _cmbPublisher.Items.Add("(manual / none)");
        foreach (var p in _publishers) _cmbPublisher.Items.Add(p.Name);
        _cmbPublisher.SelectedIndex = 0;
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
        if (!string.IsNullOrWhiteSpace(p.IdentityName)) _txtIdName.Text = p.IdentityName!;
        if (!string.IsNullOrWhiteSpace(p.Publisher)) _txtPublisher.Text = p.Publisher!;
        if (!string.IsNullOrWhiteSpace(p.PublisherDisplayName)) _txtPubDisplay.Text = p.PublisherDisplayName!;
        if (p.ClientId != null) _txtClientId.Text = p.ClientId;
        if (!string.IsNullOrWhiteSpace(p.ServiceTicketBaseUrl)) _txtServiceUrl.Text = p.ServiceTicketBaseUrl!;
        Log($"Publisher selected: {p.Name}");
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

        LoadPublishers();
        LoadBridgeConfig();
        UpdateMode();
        Log("Ready. Choose a build type, fill in the fields, and press Build.");
        Log($"Project: {_root}");
    }

    // ---------------------------------------------------------------- bridge config
    private string CurrentGameDir()
    {
        var g = _txtGame.Text.Trim();
        return string.IsNullOrWhiteSpace(g) ? _gameDir : g;
    }

    private string BridgeConfigPath() => Path.Combine(CurrentGameDir(), "playgama-bridge-config.json");

    private void LoadBridgeConfig()
    {
        var path = BridgeConfigPath();
        try
        {
            _txtBridgeConfig.Text = File.Exists(path)
                ? File.ReadAllText(path)
                : "";
            _txtGameId.Text = TryReadGameId(_txtBridgeConfig.Text);
            BuildPaymentsUi(_txtBridgeConfig.Text);
            Log(File.Exists(path) ? $"Loaded {path}" : $"(no playgama-bridge-config.json in {CurrentGameDir()})");
        }
        catch (Exception ex)
        {
            Log($"Could not read bridge config: {ex.Message}");
        }
    }

    private bool SaveBridgeConfig(bool silent)
    {
        // Merge payment edits + ensure required config keys, then write.
        var merged = ApplyConfigEdits(_txtBridgeConfig.Text);
        _txtBridgeConfig.Text = merged;
        return WriteBridgeConfig(merged, BridgeConfigPath(), silent);
    }

    private void BottomTabs_Selecting(object? sender, TabControlCancelEventArgs e)
    {
        try
        {
            if (e.TabPage == _tabRaw) _txtBridgeConfig.Text = ApplyConfigEdits(_txtBridgeConfig.Text);
            else if (e.TabPage == _tabPayments) BuildPaymentsUi(_txtBridgeConfig.Text);
        }
        catch (Exception ex) { Log($"payments sync: {ex.Message}"); }
    }

    // Render one editable row per entry in the config's "payments" array.
    private void BuildPaymentsUi(string rawJson)
    {
        _paymentsHost.Controls.Clear();
        _paymentRows.Clear();

        JsonNode? node;
        try { node = string.IsNullOrWhiteSpace(rawJson) ? null : JsonNode.Parse(rawJson); }
        catch
        {
            _paymentsHost.Controls.Add(new Label { Text = "Config is not valid JSON — fix it on the Raw JSON tab.", AutoSize = true, ForeColor = Color.Firebrick });
            return;
        }

        if (node?["payments"] is not JsonArray payments || payments.Count == 0)
        {
            _paymentsHost.Controls.Add(new Label { Text = "No \"payments\" array in this config.", AutoSize = true, ForeColor = Color.DimGray });
            return;
        }

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        foreach (var (text, c) in new[] { ("Payment", 0), ("Microsoft Store Product ID", 1), ("Amount", 2), ("Description", 3) })
            grid.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(3, 3, 6, 3) }, c, 0);
        grid.RowCount = 1;

        foreach (var item in payments)
        {
            if (item is not JsonObject obj) continue;
            var pid = (string?)obj["id"] ?? "";
            var ms = obj["microsoft_store"] as JsonObject;

            int r = grid.RowCount;
            var row = new PaymentRowUi
            {
                PaymentId = pid,
                StoreId = new TextBox { Dock = DockStyle.Fill, Text = (string?)ms?["id"] ?? "", Margin = new Padding(3, 4, 3, 4) },
                Amount = new TextBox { Dock = DockStyle.Fill, Text = ms?["amount"]?.ToString() ?? "", Margin = new Padding(3, 4, 3, 4) },
                Desc = new TextBox { Dock = DockStyle.Fill, Text = (string?)ms?["description"] ?? "", Margin = new Padding(3, 4, 3, 4) }
            };
            // Accept a pasted Partner Center URL and reduce it to the product ID when leaving the field.
            row.StoreId.Leave += (_, _) => row.StoreId.Text = ExtractStoreId(row.StoreId.Text);
            grid.Controls.Add(new Label { Text = pid, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 6, 0) }, 0, r);
            grid.Controls.Add(row.StoreId, 1, r);
            grid.Controls.Add(row.Amount, 2, r);
            grid.Controls.Add(row.Desc, 3, r);
            grid.RowCount = r + 1;
            _paymentRows.Add(row);
        }

        _paymentsHost.Controls.Add(grid);
    }

    // Merge the structured payment rows AND ensure the required microsoft_store config keys
    // exist, preserving everything else in the file.
    private string ApplyConfigEdits(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;   // no config file -> don't fabricate one

        JsonNode? node;
        try { node = JsonNode.Parse(rawJson); }
        catch { return rawJson; }   // never edit invalid JSON
        if (node is not JsonObject root) return rawJson;

        // Payments -> microsoft_store ids
        if (root["payments"] is JsonArray payments && _paymentRows.Count > 0)
        {
            var byId = new Dictionary<string, PaymentRowUi>();
            foreach (var r in _paymentRows)
                if (!string.IsNullOrEmpty(r.PaymentId)) byId[r.PaymentId] = r;

            foreach (var item in payments)
            {
                if (item is not JsonObject obj) continue;
                var pid = (string?)obj["id"] ?? "";
                if (!byId.TryGetValue(pid, out var row)) continue;

                if (obj["microsoft_store"] is not JsonObject ms)
                {
                    ms = new JsonObject();
                    obj["microsoft_store"] = ms;
                }

                ms["id"] = ExtractStoreId(row.StoreId.Text);

                var amt = row.Amount.Text.Trim();
                if (double.TryParse(amt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                    ms["amount"] = JsonValue.Create(amount);
                else if (amt.Length == 0)
                    ms.Remove("amount");

                ms["description"] = row.Desc.Text;
            }
        }

        EnsureMicrosoftStoreDefaults(root, ExtractStoreId(_txtGameId.Text));

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Adds the Microsoft Store config keys if they are missing; gameId always comes from the tool.
    private static void EnsureMicrosoftStoreDefaults(JsonObject root, string gameId)
    {
        if (root["sendAnalyticsEvents"] is null) root["sendAnalyticsEvents"] = true;

        if (root["platforms"] is not JsonObject platforms)
        {
            platforms = new JsonObject();
            root["platforms"] = platforms;
        }
        if (platforms["microsoft_store"] is not JsonObject ms)
        {
            ms = new JsonObject();
            platforms["microsoft_store"] = ms;
        }
        if (!string.IsNullOrWhiteSpace(gameId)) ms["gameId"] = gameId;
        else if (ms["gameId"] is null) ms["gameId"] = "";
        if (ms["playgamaAdsId"] is null) ms["playgamaAdsId"] = "msn_store";

        if (root["advertisement"] is not JsonObject ad)
        {
            ad = new JsonObject();
            root["advertisement"] = ad;
        }
        if (ad["useBuiltInErrorPopup"] is null) ad["useBuiltInErrorPopup"] = false;

        if (root["showFullLoadingLogo"] is null) root["showFullLoadingLogo"] = false;
        if (root["forciblySetPlatformId"] is null) root["forciblySetPlatformId"] = "microsoft_store";
    }

    private static string TryReadGameId(string rawJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return "";
            var node = JsonNode.Parse(rawJson);
            return (string?)node?["platforms"]?["microsoft_store"]?["gameId"] ?? "";
        }
        catch { return ""; }
    }

    private bool WriteBridgeConfig(string text, string path, bool silent)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;   // nothing to save

        // Validate JSON so we never write a broken config.
        try { JsonNode.Parse(text); }
        catch (Exception ex)
        {
            var msg = $"Bridge config is not valid JSON: {ex.Message}";
            if (silent) { Log(msg + " (not saved)"); return false; }
            MessageBox.Show(this, msg, "Invalid JSON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            Log($"Saved {path}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Could not save bridge config: {ex.Message}");
            if (!silent) MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    // Apply edits + write playgama-bridge-config.json, then show the result — no bundle.
    private void UpdateConfigOnly()
    {
        var game = _txtGame.Text.Trim();
        if (!Directory.Exists(game))
        {
            MessageBox.Show(this, "Choose a valid game folder first.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (SaveBridgeConfig(silent: false))
        {
            _bottomTabs.SelectedTab = _tabRaw;   // show the final JSON to review
            Log("Config updated (no bundle created). Review it on the Raw JSON tab.");
        }
    }

    // ---------------------------------------------------------------- build
    private sealed class BuildInputs
    {
        public bool Store;
        public string Game = "", Icon = "", Title = "", Version = "";
        public string IdName = "", Publisher = "", PubDisplay = "", ClientId = "", ServiceUrl = "", BridgeConfig = "";
    }

    private async Task OnBuildAsync()
    {
        // Merge payment edits + ensure required config keys before reading it.
        try { _txtBridgeConfig.Text = ApplyConfigEdits(_txtBridgeConfig.Text); } catch { }

        // Read all controls on the UI thread (they can't be touched from a background thread).
        var i = new BuildInputs
        {
            Store = _rbStore.Checked,
            Game = _txtGame.Text.Trim(),
            Icon = _txtIcon.Text.Trim(),
            Title = _txtTitle.Text.Trim(),
            Version = _txtVersion.Text.Trim(),
            IdName = _txtIdName.Text.Trim(),
            Publisher = _txtPublisher.Text.Trim(),
            PubDisplay = _txtPubDisplay.Text.Trim(),
            ClientId = _txtClientId.Text.Trim(),
            ServiceUrl = _txtServiceUrl.Text.Trim(),
            BridgeConfig = _txtBridgeConfig.Text
        };

        if (!Directory.Exists(i.Game) || !File.Exists(Path.Combine(i.Game, "index.html")))
        {
            MessageBox.Show(this, "Choose a game folder that contains index.html.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(i.Version))
        {
            MessageBox.Show(this, "Enter a version (e.g. 1.0.0).", "Version", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Cert args from the selected publisher (local builds only; the Store re-signs).
        string certArgs = "";
        if (!i.Store && _selectedProfile?.Pfx is string pfxRel && !string.IsNullOrWhiteSpace(pfxRel))
        {
            var pfxAbs = Path.GetFullPath(Path.Combine(_root, pfxRel));
            if (File.Exists(pfxAbs))
                certArgs = $" -PfxPath \"{pfxAbs}\" -PfxPassword \"{_selectedProfile.PfxPassword}\"";
            else
                Log($"Note: certificate not found ({pfxAbs}); using the default signing cert.");
        }

        _btnBuild.Enabled = false;
        _txtOutput.Clear();

        try
        {
            await Task.Run(() => Prepare(i));

            var script = i.Store ? "build-store.ps1" : "build.ps1";
            var args = (i.Store ? $"-Version {NormalizeVersion(i.Version, storeZero: true)}" : "") + certArgs;

            Log($"\n--- Running {script} ---\n");
            int code = await RunScriptAsync(script, args);

            if (code == 0)
            {
                var outFolder = i.Store ? "dist-store" : "dist";
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

    private void Prepare(BuildInputs i)
    {
        // 0. Persist any bridge-config edits back into the game folder before it's copied.
        WriteBridgeConfig(i.BridgeConfig, Path.Combine(i.Game, "playgama-bridge-config.json"), silent: true);

        // 1. Manifest
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
        // Remove legacy PhoneIdentity (not needed for desktop, breaks non-GUID Store names).
        pkg.Element(MP + "PhoneIdentity")?.Remove();
        doc.Save(_manifest);

        // 2. appsettings.json
        Log("Writing appsettings.json…");
        var json = new JsonObject
        {
            ["clientId"] = i.ClientId,
            ["serviceTicketBaseUrl"] = string.IsNullOrWhiteSpace(i.ServiceUrl) ? "https://playgama.com" : i.ServiceUrl
        };
        File.WriteAllText(_appSettings, json.ToString());

        // 3. Game files
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

        // 4. Icon -> logos
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
        var folder = Path.Combine(_root, _rbStore.Checked ? "dist-store" : "dist");
        if (!Directory.Exists(folder)) { MessageBox.Show(this, "Nothing built yet.", "Output", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void Log(string line)
    {
        if (_txtOutput.InvokeRequired) { _txtOutput.BeginInvoke(new Action(() => Log(line))); return; }
        _txtOutput.AppendText(line + Environment.NewLine);
    }

    // Accepts a raw product ID or a Partner Center URL like
    // https://partner.microsoft.com/.../products/9NGNPW4CMHH1/overview  ->  9NGNPW4CMHH1
    private static string ExtractStoreId(string input)
    {
        var s = (input ?? "").Trim();
        const string marker = "products/";
        int idx = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            s = s.Substring(idx + marker.Length);
            int end = s.IndexOfAny(new[] { '/', '?', '#' });
            if (end >= 0) s = s.Substring(0, end);
        }
        return s.Trim();
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

        // Tiles / store / splash: base (scale-100) + scale-200 so they look crisp.
        SaveSquareScaled(src, assetsDir, "Square44x44Logo", 44);
        SaveSquareScaled(src, assetsDir, "Square150x150Logo", 150);
        SaveSquareScaled(src, assetsDir, "StoreLogo", 50);
        SaveCanvasScaled(src, assetsDir, "Wide310x150Logo", 310, 150);
        SaveCanvasScaled(src, assetsDir, "SplashScreen", 620, 300);
        SaveSquareScaled(src, assetsDir, "LockScreenLogo", 24);

        // App-list / taskbar target sizes (plated + unplated).
        foreach (var s in new[] { 16, 24, 32, 48, 256 })
        {
            SaveSquare(src, Path.Combine(assetsDir, $"Square44x44Logo.targetsize-{s}.png"), s);
            SaveSquare(src, Path.Combine(assetsDir, $"Square44x44Logo.targetsize-{s}_altform-unplated.png"), s);
        }

        // favicon.ico (16/32/48/256) for the window / taskbar / Alt-Tab icon.
        WriteIco(Path.Combine(assetsDir, "favicon.ico"), src, new[] { 16, 32, 48, 256 });
    }

    private static void SaveSquareScaled(Bitmap src, string dir, string baseName, int size)
    {
        SaveSquare(src, Path.Combine(dir, baseName + ".png"), size);
        SaveSquare(src, Path.Combine(dir, baseName + ".scale-200.png"), size * 2);
    }

    private static void SaveCanvasScaled(Bitmap src, string dir, string baseName, int w, int h)
    {
        SaveCanvas(src, Path.Combine(dir, baseName + ".png"), w, h);
        SaveCanvas(src, Path.Combine(dir, baseName + ".scale-200.png"), w * 2, h * 2);
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
        w.Write((short)0);              // reserved
        w.Write((short)1);              // type = icon
        w.Write((short)sizes.Length);   // image count
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); // width  (0 = 256)
            w.Write((byte)(s >= 256 ? 0 : s)); // height
            w.Write((byte)0);                  // palette
            w.Write((byte)0);                  // reserved
            w.Write((short)1);                 // color planes
            w.Write((short)32);                // bits per pixel
            w.Write(images[i].Length);         // size of image data
            w.Write(offset);                   // offset of image data
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
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
