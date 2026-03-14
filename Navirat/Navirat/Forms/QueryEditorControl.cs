using Navirat.Services;
using System.Data;
using System.Text;

namespace Navirat.Forms;

/// <summary>
/// SQL クエリエディタコントロール
/// </summary>
public class QueryEditorControl : UserControl
{
    private readonly DatabaseService? _dbService;
    private readonly string? _defaultDatabase;
    private CancellationTokenSource? _cts;

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
    private ComboBox cmbDatabase = null!;

    public QueryEditorControl(DatabaseService? dbService, string? defaultDatabase)
    {
        _dbService = dbService;
        _defaultDatabase = defaultDatabase;
        InitializeComponent();
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

        toolbar.Items.Add(new ToolStripLabel("DB: "));
        cmbDatabase = new ComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        if (_defaultDatabase != null)
            cmbDatabase.Items.Add(_defaultDatabase);
        cmbDatabase.SelectedItem = _defaultDatabase;

        var cmbHost = new ToolStripControlHost(cmbDatabase);
        toolbar.Items.AddRange([btnRun, btnStop, new ToolStripSeparator(),
            btnFormat, btnClear, new ToolStripSeparator(), new ToolStripLabel("データベース: "), cmbHost]);

        // ===== スプリットコンテナ =====
        splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 80,
            Panel2MinSize = 80
        };
        // SplitterDistance はコントロールサイズ確定後に設定する
        splitContainer.HandleCreated += (s, e) =>
        {
            int desired = 200;
            int safeMax = splitContainer.Height - splitContainer.SplitterWidth - splitContainer.Panel2MinSize;
            if (safeMax > splitContainer.Panel1MinSize)
                splitContainer.SplitterDistance = Math.Max(splitContainer.Panel1MinSize, Math.Min(desired, safeMax));
        };

        // ===== SQLエディタ =====
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
        lblRows = new ToolStripStatusLabel("") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        lblTime = new ToolStripStatusLabel("") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        statusBar.Items.AddRange([lblStatus, lblRows, lblTime]);

        // ドッキング順序: Fill を先に Add → Top/Bottom を後から Add
        Controls.Add(splitContainer); // [1] Fill: 最初に Add
        Controls.Add(toolbar);        // [2] Top:  後から Add
        Controls.Add(statusBar);      // [3] Bottom: 最後に Add
    }

    private void TxtQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.Return))
        {
            e.Handled = true;
            _ = ExecuteQueryAsync();
        }
        else if (e.KeyCode == Keys.Tab)
        {
            // タブ = 4スペース
            e.Handled = true;
            int selStart = txtQuery.SelectionStart;
            txtQuery.SelectedText = "    ";
            txtQuery.SelectionStart = selStart + 4;
        }
    }

    private async Task ExecuteQueryAsync()
    {
        if (_dbService == null)
        {
            MessageBox.Show("データベースに接続されていません。\n接続ツリーからデータベースを選択してください。",
                "未接続", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 選択範囲があれば選択部分のみ実行
        var sql = string.IsNullOrWhiteSpace(txtQuery.SelectedText)
            ? txtQuery.Text
            : txtQuery.SelectedText;

        if (string.IsNullOrWhiteSpace(sql)) return;

        // データベースを切り替え
        var selectedDb = cmbDatabase.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedDb))
        {
            try { await _dbService.UseDatabaseAsync(selectedDb); }
            catch { /* 無視 */ }
        }

        _cts = new CancellationTokenSource();
        btnRun.Enabled = false;
        btnStop.Enabled = true;
        tabResults.TabPages.Clear();
        lblStatus.Text = "実行中...";
        lblRows.Text = "";
        lblTime.Text = "";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var results = await _dbService.ExecuteMultipleAsync(sql, _cts.Token);
            sw.Stop();

            int totalRows = 0;
            int stmtCount = 0;

            foreach (var (stmt, dt, affected, error, elapsed) in results)
            {
                stmtCount++;
                if (error != null)
                {
                    var errTab = new TabPage($"エラー #{stmtCount}");
                    var errText = new RichTextBox
                    {
                        Dock = DockStyle.Fill,
                        ReadOnly = true,
                        Text = $"SQL:\n{stmt}\n\nエラー:\n{error}",
                        ForeColor = Color.Red,
                        BackColor = Color.FromArgb(40, 40, 40),
                        Font = new Font("Consolas", 9f)
                    };
                    errTab.Controls.Add(errText);
                    tabResults.TabPages.Add(errTab);
                }
                else if (dt != null)
                {
                    totalRows += dt.Rows.Count;
                    var gridTab = new TabPage($"結果 #{stmtCount} ({dt.Rows.Count:N0} 行)");
                    var grid = CreateResultGrid(dt);
                    gridTab.Controls.Add(grid);
                    tabResults.TabPages.Add(gridTab);
                }
                else
                {
                    var infoTab = new TabPage($"完了 #{stmtCount}");
                    var infoText = new Label
                    {
                        Dock = DockStyle.Fill,
                        Text = $"  {affected:N0} 行が影響を受けました。\n  実行時間: {elapsed.TotalMilliseconds:F1} ms",
                        ForeColor = Color.LimeGreen,
                        BackColor = Color.FromArgb(30, 30, 30),
                        Font = new Font("Meiryo UI", 10f),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(12)
                    };
                    infoTab.BackColor = Color.FromArgb(30, 30, 30);
                    infoTab.Controls.Add(infoText);
                    tabResults.TabPages.Add(infoTab);
                }
            }

            lblStatus.Text = $"{stmtCount} 個のステートメントを実行しました。";
            lblRows.Text = $"  {totalRows:N0} 行  ";
            lblTime.Text = $"  {sw.Elapsed.TotalMilliseconds:F1} ms  ";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "実行がキャンセルされました。";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"エラー: {ex.Message}";
            var errTab = new TabPage("エラー");
            var errText = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Text = ex.ToString(),
                ForeColor = Color.Red,
                Font = new Font("Consolas", 9f)
            };
            errTab.Controls.Add(errText);
            tabResults.TabPages.Add(errTab);
        }
        finally
        {
            btnRun.Enabled = true;
            btnStop.Enabled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static DataGridView CreateResultGrid(DataTable dt)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = dt,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText,
            BackgroundColor = Color.White,
            GridColor = Color.LightGray,
            BorderStyle = BorderStyle.None,
            Font = new Font("Meiryo UI", 9f),
            VirtualMode = false,
            ScrollBars = ScrollBars.Both
        };

        // 列幅を自動調整（最大200px）
        grid.DataBindingComplete += (s, e) =>
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
                int w = col.Width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width = Math.Min(w, 200);
                col.MinimumWidth = 50;
            }
        };

        // NULL 値を "(NULL)" と表示（チェックボックス列は別扱い）
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

        // DataGridView の内部エラーダイアログを抑制
        grid.DataError += (s, e) => { e.Cancel = true; };

        // コンテキストメニュー（コピー）
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

    private void FormatQuery()
    {
        var sql = txtQuery.Text;
        if (string.IsNullOrWhiteSpace(sql)) return;

        // 簡易フォーマット（キーワードを改行）
        var keywords = new[] { "SELECT", "FROM", "WHERE", "AND", "OR", "ORDER BY",
            "GROUP BY", "HAVING", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "JOIN",
            "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "LIMIT", "OFFSET" };

        var result = new StringBuilder(sql.Trim());
        foreach (var kw in keywords)
        {
            // キーワードの前に改行
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
