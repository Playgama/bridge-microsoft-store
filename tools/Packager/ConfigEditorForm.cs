using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playgama.Packager;

// Edits playgama-bridge-config.json in a game folder: Store Game ID, in-app payments, raw JSON.
internal sealed class ConfigEditorForm : Form
{
    private readonly string _gameDir;
    private readonly string _configPath;

    private TextBox _txtGameId = null!, _txtRaw = null!, _status = null!;
    private TabControl _tabs = null!;
    private TabPage _tabPayments = null!, _tabRaw = null!;
    private Panel _paymentsHost = null!;
    private readonly List<PaymentRowUi> _rows = new();

    private sealed class PaymentRowUi
    {
        public string PaymentId = "";
        public TextBox StoreId = null!;
        public TextBox Amount = null!;
        public TextBox Desc = null!;
    }

    public ConfigEditorForm(string gameDir)
    {
        _gameDir = gameDir;
        _configPath = Path.Combine(gameDir, "playgama-bridge-config.json");

        Text = "Config & in-app purchases — " + Path.GetFileName(gameDir.TrimEnd('\\'));
        Width = 900;
        Height = 640;
        MinimumSize = new Size(700, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        LoadConfig();
    }

    private void BuildUi()
    {
        // Top: Store Game ID + buttons
        var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(10, 10, 10, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "Store Game ID (platforms)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, 0);
        _txtGameId = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
        _txtGameId.Leave += (_, _) => _txtGameId.Text = ExtractStoreId(_txtGameId.Text);
        top.Controls.Add(_txtGameId, 1, 0);
        Controls.Add(top);

        // Bottom: Reload / Save / Close + status
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var btnClose = new Button { Text = "Close", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        var btnSave = new Button { Text = "Save", AutoSize = true, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(8, 0, 0, 0) };
        var btnReload = new Button { Text = "Reload", AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(8, 0, 0, 0) };
        btnClose.Click += (_, _) => Close();
        btnSave.Click += (_, _) => Save();
        btnReload.Click += (_, _) => LoadConfig();
        _status = new TextBox { BorderStyle = BorderStyle.None, ReadOnly = true, Width = 360, Margin = new Padding(8, 8, 8, 0), BackColor = SystemColors.Control };
        bottom.Controls.Add(btnClose);
        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnReload);
        bottom.Controls.Add(_status);
        Controls.Add(bottom);

        // Center: tabs
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabPayments = new TabPage("In-app purchases");
        _paymentsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        _tabPayments.Controls.Add(_paymentsHost);
        _tabRaw = new TabPage("Raw JSON");
        _txtRaw = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9f), WordWrap = false, AcceptsTab = true };
        _tabRaw.Controls.Add(_txtRaw);
        _tabs.TabPages.Add(_tabPayments);
        _tabs.TabPages.Add(_tabRaw);
        _tabs.Selecting += (s, e) =>
        {
            try
            {
                if (e.TabPage == _tabRaw) _txtRaw.Text = ApplyEdits(_txtRaw.Text);
                else if (e.TabPage == _tabPayments) BuildPaymentsUi(_txtRaw.Text);
            }
            catch (Exception ex) { Status("sync: " + ex.Message); }
        };
        Controls.Add(_tabs);
        _tabs.BringToFront();
    }

    private void Status(string s) => _status.Text = s;

    private void LoadConfig()
    {
        try
        {
            _txtRaw.Text = File.Exists(_configPath) ? File.ReadAllText(_configPath) : "";
            _txtGameId.Text = TryReadGameId(_txtRaw.Text);
            BuildPaymentsUi(_txtRaw.Text);
            Status(File.Exists(_configPath) ? "Loaded " + _configPath : "No playgama-bridge-config.json in this folder.");
        }
        catch (Exception ex) { Status("Could not read: " + ex.Message); }
    }

    private void Save()
    {
        var merged = ApplyEdits(_txtRaw.Text);
        _txtRaw.Text = merged;

        if (string.IsNullOrWhiteSpace(merged)) { Status("Nothing to save (empty config)."); return; }
        try { JsonNode.Parse(merged); }
        catch (Exception ex) { MessageBox.Show(this, "Invalid JSON: " + ex.Message, "Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        try
        {
            Directory.CreateDirectory(_gameDir);
            File.WriteAllText(_configPath, merged);
            Status("Saved " + _configPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- payments grid ----
    private void BuildPaymentsUi(string rawJson)
    {
        _paymentsHost.Controls.Clear();
        _rows.Clear();

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
            row.StoreId.Leave += (_, _) => row.StoreId.Text = ExtractStoreId(row.StoreId.Text);
            grid.Controls.Add(new Label { Text = pid, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 6, 0) }, 0, r);
            grid.Controls.Add(row.StoreId, 1, r);
            grid.Controls.Add(row.Amount, 2, r);
            grid.Controls.Add(row.Desc, 3, r);
            grid.RowCount = r + 1;
            _rows.Add(row);
        }

        _paymentsHost.Controls.Add(grid);
    }

    // ---- merge edits into JSON ----
    private string ApplyEdits(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;

        JsonNode? node;
        try { node = JsonNode.Parse(rawJson); }
        catch { return rawJson; }
        if (node is not JsonObject root) return rawJson;

        if (root["payments"] is JsonArray payments && _rows.Count > 0)
        {
            var byId = new Dictionary<string, PaymentRowUi>();
            foreach (var r in _rows)
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

        EnsureDefaults(root, ExtractStoreId(_txtGameId.Text));
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void EnsureDefaults(JsonObject root, string gameId)
    {
        if (root["sendAnalyticsEvents"] is null) root["sendAnalyticsEvents"] = true;

        if (root["platforms"] is not JsonObject platforms) { platforms = new JsonObject(); root["platforms"] = platforms; }
        if (platforms["microsoft_store"] is not JsonObject ms) { ms = new JsonObject(); platforms["microsoft_store"] = ms; }
        if (!string.IsNullOrWhiteSpace(gameId)) ms["gameId"] = gameId;
        else if (ms["gameId"] is null) ms["gameId"] = "";
        if (ms["playgamaAdsId"] is null) ms["playgamaAdsId"] = "msn_store";

        if (root["advertisement"] is not JsonObject ad) { ad = new JsonObject(); root["advertisement"] = ad; }
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

    // raw product ID or Partner Center URL (.../products/<ID>/overview) -> <ID>
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
}
