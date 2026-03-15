using Navirat.Services;
using System.Data;
using System.Text;

namespace Navirat.Forms;

/// <summary>
/// SQL クエリエディタコントロール
/// </summary>
public class QueryEditorControl : UserControl
{
    // MainForm の _activeConnections への参照（起動後に追加された接続も参照できる）
    private readonly Dictionary<string, (DatabaseService DbService, SshTunnelService? SshService)> _connections;
    private DatabaseService? _currentDbService;
    private CancellationTokenSource? _cts;
    // 接続切り替え後にDBコンボへ反映する保留DB名
    private string? _pendingDatabase;

    // UI コントロール
    private RichTextBox txtQuery = null!;
    private TabControl tabResults = null!;
    private SplitContainer splitContainer = null!;
    private ToolStrip toolbar = null!;
    private StatusStrip statusBar = null!;
    private ToolStripStatusLabel lblStatus = null!;
    private ToolStripStatusLabel lblTime = null!;
    private ToolStripStatusLabel lblRows = null!;
    private ToolStripButton btnRun = null!;
    private ToolStripButton btnStop = null!;
    private ComboBox cmbConnection = null!;
    private ComboBox cmbDatabase = null!;

    public QueryEditorControl(
        Dictionary<string, (DatabaseService DbService, SshTunnelService? SshService)> connections,
        string? defaultConnection,
        string? defaultDatabase)
    {
        _connections = connections;
        InitializeComponent();

        // 接続コンボを初期値で設定（SelectedIndexChanged → DB一覧ロードが走る）
        PopulateConnectionCombo();
        if (defaultConnection != null && cmbConnection.Items.Contains(defaultConnection))
            cmbConnection.SelectedItem = defaultConnection;
        else if (cmbConnection.Items.Count > 0)
            cmbConnection.SelectedIndex = 0;

        // デフォルト DB をコンボに反映（DB一覧ロード完了後に上書きされるが先に仮セット）
        if (defaultDatabase != null)
        {
            if (!cmbDatabase.Items.Contains(defaultDatabase))
                cmbDatabase.Items.Add(defaultDatabase);
            cmbDatabase.SelectedItem = defaultDatabase;
        }
    }

    private void InitializeComponent()
    {
        Font = new Font("Meiryo UI", 9f);

        // ===== ツールバー =====
        toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

        btnRun = new ToolStripButton("▶ 実行 (F5)") { ToolTipText = "クエリを実行 (F5)" };
        btnRun.Click += (s, e) => _ = ExecuteQueryAsync();

        btnStop = new ToolStripButton("■ 停止") { ToolTipText = "実行を停止", Enabled = false };
        btnStop.Click += (s, e) => _cts?.Cancel();

        var btnFormat = new ToolStripButton("整形") { ToolTipText = "SQL を整形（簡易）" };
        btnFormat.Click += (s, e) => FormatQuery();

        var btnClear = new ToolStripButton("クリア") { ToolTipText = "エディタをクリア" };
        btnClear.Click += (s, e) =>
        {
            if (MessageBox.Show("エディタをクリアしますか？", "確認", MessageBoxButtons.YesNo) == DialogResult.Yes)
                txtQuery.Clear();
        };

        // 接続コンボ
        cmbConnection = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbConnection.DropDown += (s, e) => PopulateConnectionCombo(); // ドロップダウン時に最新接続を反映
        cmbConnection.SelectedIndexChanged += CmbConnection_SelectedIndexChanged;

        // データベースコンボ
        cmbDatabase = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var cmbConnHost = new ToolStripControlHost(cmbConnection);
        var cmbDbHost   = new ToolStripControlHost(cmbDatabase);

        toolbar.Items.AddRange([
            btnRun, btnStop,
            new ToolStripSeparator(),
            btnFormat, btnClear,
            new ToolStripSeparator(),
            new ToolStripLabel("接続: "), cmbConnHost,
            new ToolStripLabel("  DB: "), cmbDbHost
        ]);

        // ===== スプリットコンテナ =====
        splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 80,
            Panel2MinSize = 80
        };
        splitContainer.HandleCreated += (s, e) =>
        {
            int desired = 200;
            int safeMax = splitContainer.Height - splitContainer.SplitterWidth - splitContainer.Panel2MinSize;
            if (safeMax > splitContainer.Panel1MinSize)
                splitContainer.SplitterDistance = Math.Max(splitContainer.Panel1MinSize, Math.Min(desired, safeMax));
        };

        // ===== SQL エディタ =====
        txtQuery = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
            AcceptsTab = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            Text = "-- SQLを入力してください\n-- F5で実行、Ctrl+Enterでも実行できます\n\n"
        };
        txtQuery.KeyDown += TxtQuery_KeyDown;
        splitContainer.Panel1.Controls.Add(txtQuery);

        // ===== 結果タブコントロール =====
        tabResults = new TabControl { Dock = DockStyle.Fill };
        splitContainer.Panel2.Controls.Add(tabResults);

        // ===== ステータスバー =====
        statusBar = new StatusStrip();
        lblStatus = new ToolStripStatusLabel("準備完了") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        lblRows   = new ToolStripStatusLabel("") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        lblTime   = new ToolStripStatusLabel("") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        statusBar.Items.AddRange([lblStatus, lblRows, lblTime]);

        // ドッキング順序: Fill を先に Add → Top/Bottom を後から Add
        Controls.Add(splitContainer); // [1] Fill
        Controls.Add(toolbar);        // [2] Top
        Controls.Add(statusBar);      // [3] Bottom
    }

    // =============================================
    // 接続コンボの制御
    // =============================================

    private void PopulateConnectionCombo()
    {
        var current = cmbConnection.SelectedItem?.ToString();
        cmbConnection.SelectedIndexChanged -= CmbConnection_SelectedIndexChanged;
        cmbConnection.Items.Clear();
        foreach (var key in _connections.Keys)
            cmbConnection.Items.Add(key);
        cmbConnection.SelectedIndexChanged += CmbConnection_SelectedIndexChanged;

        // 以前の選択を維持
        if (current != null && cmbConnection.Items.Contains(current))
            cmbConnection.SelectedItem = current;
        else if (cmbConnection.Items.Count > 0 && cmbConnection.SelectedIndex < 0)
            cmbConnection.SelectedIndex = 0;
    }

    private async void CmbConnection_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var connName = cmbConnection.SelectedItem?.ToString();
        if (connName == null || !_connections.TryGetValue(connName, out var conn))
        {
            _currentDbService = null;
            cmbDatabase.Items.Clear();
            lblStatus.Text = "接続を選択してください";
            return;
        }

        _currentDbService = conn.DbService;

        // DB 一覧を非同期で取得してコンボに反映
        var prevDb = _pendingDatabase ?? cmbDatabase.SelectedItem?.ToString();
        _pendingDatabase = null;
        cmbDatabase.Items.Clear();
        try
        {
            var dbs = await conn.DbService.GetDatabasesAsync();
            foreach (var db in dbs)
                cmbDatabase.Items.Add(db);

            if (prevDb != null && cmbDatabase.Items.Contains(prevDb))
                cmbDatabase.SelectedItem = prevDb;
            else if (cmbDatabase.Items.Count > 0)
                cmbDatabase.SelectedIndex = 0;
        }
        catch { /* DB一覧取得失敗は無視 */ }

        lblStatus.Text = $"接続: {connName}";
    }

    // =============================================
    // キーボード
    // =============================================

    private void TxtQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.Return))
        {
            e.Handled = true;
            _ = ExecuteQueryAsync();
        }
        else if (e.KeyCode == Keys.Tab)
        {
            e.Handled = true;
            int selStart = txtQuery.SelectionStart;
            txtQuery.SelectedText = "    ";
            txtQuery.SelectionStart = selStart + 4;
        }
    }

    // =============================================
    // クエリ実行
    // =============================================

    private async Task ExecuteQueryAsync()
    {
        if (_currentDbService == null)
        {
            MessageBox.Show("接続を選択してください。",
                "未接続", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var sql = string.IsNullOrWhiteSpace(txtQuery.SelectedText)
            ? txtQuery.Text
            : txtQuery.SelectedText;

        if (string.IsNullOrWhiteSpace(sql)) return;

        // データベースを切り替え
        var selectedDb = cmbDatabase.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedDb))
        {
            try { await _currentDbService.UseDatabaseAsync(selectedDb); }
            catch { /* 無視 */ }
        }

        _cts = new CancellationTokenSource();
        btnRun.Enabled  = false;
        btnStop.Enabled = true;
        tabResults.TabPages.Clear();
        lblStatus.Text = "実行中...";
        lblRows.Text   = "";
        lblTime.Text   = "";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var results = await _currentDbService.ExecuteMultipleAsync(sql, _cts.Token);
            sw.Stop();

            int totalRows  = 0;
            int stmtCount  = 0;

            foreach (var (stmt, dt, affected, error, elapsed) in results)
            {
                stmtCount++;
                if (error != null)
                {
                    var errTab  = new TabPage($"エラー #{stmtCount}");
                    var errText = new RichTextBox
                    {
                        Dock      = DockStyle.Fill,
                        ReadOnly  = true,
                        Text      = $"SQL:\n{stmt}\n\nエラー:\n{error}",
                        ForeColor = Color.Red,
                        BackColor = Color.FromArgb(40, 40, 40),
                        Font      = new Font("Consolas", 9f)
                    };
                    errTab.Controls.Add(errText);
                    tabResults.TabPages.Add(errTab);
                }
                else if (dt != null)
                {
                    totalRows += dt.Rows.Count;
                    var gridTab = new TabPage($"結果 #{stmtCount} ({dt.Rows.Count:N0} 行)");
                    gridTab.Controls.Add(CreateResultGrid(dt));
                    tabResults.TabPages.Add(gridTab);
                }
                else
                {
                    var infoTab  = new TabPage($"完了 #{stmtCount}");
                    var infoText = new Label
                    {
                        Dock      = DockStyle.Fill,
                        Text      = $"  {affected:N0} 行が影響を受けました。\n  実行時間: {elapsed.TotalMilliseconds:F1} ms",
                        ForeColor = Color.LimeGreen,
                        BackColor = Color.FromArgb(30, 30, 30),
                        Font      = new Font("Meiryo UI", 10f),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding   = new Padding(12)
                    };
                    infoTab.BackColor = Color.FromArgb(30, 30, 30);
                    infoTab.Controls.Add(infoText);
                    tabResults.TabPages.Add(infoTab);
                }
            }

            lblStatus.Text = $"{stmtCount} 個のステートメントを実行しました。";
            lblRows.Text   = $"  {totalRows:N0} 行  ";
            lblTime.Text   = $"  {sw.Elapsed.TotalMilliseconds:F1} ms  ";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "実行がキャンセルされました。";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"エラー: {ex.Message}";
            var errTab  = new TabPage("エラー");
            var errText = new RichTextBox
            {
                Dock      = DockStyle.Fill,
                ReadOnly  = true,
                Text      = ex.ToString(),
                ForeColor = Color.Red,
                Font      = new Font("Consolas", 9f)
            };
            errTab.Controls.Add(errText);
            tabResults.TabPages.Add(errTab);
        }
        finally
        {
            btnRun.Enabled  = true;
            btnStop.Enabled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // =============================================
    // 結果グリッド
    // =============================================

    private static DataGridView CreateResultGrid(DataTable dt)
    {
        var grid = new DataGridView
        {
            Dock                          = DockStyle.Fill,
            DataSource                    = dt,
            ReadOnly                      = true,
            AllowUserToAddRows            = false,
            AllowUserToDeleteRows         = false,
            AutoSizeColumnsMode           = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode   = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode                 = DataGridViewSelectionMode.CellSelect,
            ClipboardCopyMode             = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText,
            BackgroundColor               = Color.White,
            GridColor                     = Color.LightGray,
            BorderStyle                   = BorderStyle.None,
            Font                          = new Font("Meiryo UI", 9f),
            VirtualMode                   = false,
            ScrollBars                    = ScrollBars.Both
        };

        grid.DataBindingComplete += (s, e) =>
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
                int w = col.Width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width        = Math.Min(w, 200);
                col.MinimumWidth = 50;
            }
        };

        grid.CellFormatting += (s, e) =>
        {
            if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
            if (e.Value == DBNull.Value || e.Value == null)
            {
                if (grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)
                {
                    e.Value = false;
                    e.FormattingApplied = true;
                }
                else
                {
                    e.Value = "(NULL)";
                    e.CellStyle!.ForeColor = Color.Gray;
                    e.FormattingApplied = true;
                }
            }
        };

        grid.DataError += (s, e) => { e.Cancel = true; };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("コピー (Ctrl+C)", null, (s, e) =>
        {
            if (grid.GetCellCount(DataGridViewElementStates.Selected) > 0)
                Clipboard.SetDataObject(grid.GetClipboardContent());
        });
        contextMenu.Items.Add("全選択", null, (s, e) => grid.SelectAll());
        grid.ContextMenuStrip = contextMenu;

        return grid;
    }

    // =============================================
    // 外部からの接続/DB 切り替え（ツリービュー連動用）
    // =============================================

    /// <summary>
    /// ツリービューの選択変更に合わせて接続とDBコンボを更新します。
    /// </summary>
    public void SetConnectionAndDatabase(string connectionName, string? dbName)
    {
        PopulateConnectionCombo();

        if (!cmbConnection.Items.Contains(connectionName)) return;

        if (cmbConnection.SelectedItem?.ToString() == connectionName)
        {
            // 接続は同じ → DBコンボだけ切り替え
            if (dbName != null && cmbDatabase.Items.Contains(dbName))
                cmbDatabase.SelectedItem = dbName;
        }
        else
        {
            // 接続が変わる → DB一覧ロード完了後に適用する保留DB名をセット
            _pendingDatabase = dbName;
            cmbConnection.SelectedItem = connectionName;
            // CmbConnection_SelectedIndexChanged が非同期でDB一覧をロードし
            // _pendingDatabase を適用する
        }
    }

    // =============================================
    // SQL 整形
    // =============================================

    private void FormatQuery()
    {
        var sql = txtQuery.Text;
        if (string.IsNullOrWhiteSpace(sql)) return;

        var keywords = new[] { "SELECT", "FROM", "WHERE", "AND", "OR", "ORDER BY",
            "GROUP BY", "HAVING", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "JOIN",
            "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "LIMIT", "OFFSET" };

        var result = new StringBuilder(sql.Trim());
        foreach (var kw in keywords)
        {
            var idx = 0;
            while (true)
            {
                idx = result.ToString().IndexOf(" " + kw + " ", idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                result.Replace(" " + kw + " ", "\n" + kw + " ", idx, kw.Length + 2);
                idx++;
            }
        }

        txtQuery.Text = result.ToString();
    }
}
