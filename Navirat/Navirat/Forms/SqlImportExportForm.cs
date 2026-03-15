using Navirat.Services;
using System.Data;
using System.Text;

namespace Navirat.Forms;

public enum SqlIoMode { Export, Import }

/// <summary>
/// SQL エクスポート（構造のみ / 構造＋データ）とインポートを行うダイアログ。
/// </summary>
public class SqlImportExportForm : Form
{
    private readonly DatabaseService _dbService;
    private readonly string _dbName;
    private readonly SqlIoMode _mode;
    private CancellationTokenSource? _cts;
    private bool _isRunning = false;

    // UI コントロール
    private TextBox txtFilePath = null!;
    private Button btnBrowse = null!;
    private Button btnStart = null!;
    private Button btnStop = null!;
    private Button btnClose = null!;
    private RichTextBox txtLog = null!;
    private CheckedListBox lstTables = null!;
    private CheckBox chkIncludeData = null!;
    private CheckBox chkDropTable = null!;
    private CheckBox chkFkChecks = null!;
    private CheckBox chkUniqueChecks = null!;
    private CheckBox chkContinueOnError = null!;
    private ProgressBar progressBar = null!;
    private Label lblProgress = null!;

    public SqlImportExportForm(DatabaseService dbService, string dbName, SqlIoMode mode)
    {
        _dbService = dbService;
        _dbName = dbName;
        _mode = mode;
        InitializeComponent();
        _ = LoadTablesAsync();
    }

    private void InitializeComponent()
    {
        bool isExport = _mode == SqlIoMode.Export;

        Text = isExport ? $"SQL エクスポート  ─  {_dbName}" : $"SQL インポート  ─  {_dbName}";
        Size = new Size(780, 640);
        MinimumSize = new Size(640, 500);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9f);

        // ===== ① ファイルパスパネル（Top） =====
        var filePanel = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 6, 8, 0) };

        var lblFile = new Label
        {
            Text = isExport ? "保存先:" : "インポートファイル:",
            Left = 0, Top = 9, Width = 100, AutoSize = false
        };
        txtFilePath = new TextBox
        {
            Left = 104, Top = 6, Height = 23,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        btnBrowse = new Button
        {
            Text = "参照...", Width = 72, Height = 25, Top = 5,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnBrowse.Click += BtnBrowse_Click;

        filePanel.Controls.AddRange([lblFile, txtFilePath, btnBrowse]);
        filePanel.Resize += (s, e) =>
        {
            txtFilePath.Width = filePanel.Width - 104 - btnBrowse.Width - 16;
            btnBrowse.Left    = filePanel.Width - btnBrowse.Width - 8;
        };

        // ===== ② 設定パネル（Top） =====
        int cfgHeight = isExport ? 210 : 115;
        var configPanel = new Panel { Dock = DockStyle.Top, Height = cfgHeight, Padding = new Padding(8, 4, 8, 4) };

        if (isExport)
        {
            // 左：オプション
            var grpOpt = new GroupBox
            {
                Text = "オプション", Left = 0, Top = 0, Width = 230,
                Height = cfgHeight - 8,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom
            };
            chkIncludeData = new CheckBox { Text = "データを含める", Left = 10, Top = 22, Width = 210, Checked = true, AutoSize = false };
            chkDropTable   = new CheckBox { Text = "DROP TABLE IF EXISTS を追加", Left = 10, Top = 48, Width = 210, Checked = true, AutoSize = false };
            chkFkChecks    = new CheckBox { Text = "外部キー制約を無効化 (FK_CHECKS=0)", Left = 10, Top = 74, Width = 210, Checked = true, AutoSize = false };
            grpOpt.Controls.AddRange([chkIncludeData, chkDropTable, chkFkChecks]);
            configPanel.Controls.Add(grpOpt);

            // 右：テーブル選択
            var grpTbl = new GroupBox
            {
                Text = "対象テーブル", Left = 238, Top = 0,
                Height = cfgHeight - 8,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
            };
            var btnSelAll   = new Button { Text = "全選択", Left = 8,  Top = 18, Width = 70, Height = 24 };
            var btnDeselAll = new Button { Text = "全解除", Left = 86, Top = 18, Width = 70, Height = 24 };
            lstTables = new CheckedListBox
            {
                Left = 8, Top = 48, CheckOnClick = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
            };
            btnSelAll.Click   += (s, e) => { for (int i = 0; i < lstTables.Items.Count; i++) lstTables.SetItemChecked(i, true); };
            btnDeselAll.Click += (s, e) => { for (int i = 0; i < lstTables.Items.Count; i++) lstTables.SetItemChecked(i, false); };
            grpTbl.Controls.AddRange([btnSelAll, btnDeselAll, lstTables]);
            configPanel.Controls.Add(grpTbl);

            configPanel.Resize += (s, e) =>
            {
                grpTbl.Width    = configPanel.Width - 238 - 8;
                lstTables.Width  = grpTbl.Width - 16;
                lstTables.Height = grpTbl.Height - 56;
            };
        }
        else
        {
            // インポート用オプション
            var grpOpt = new GroupBox
            {
                Text = "オプション", Dock = DockStyle.Fill, Margin = new Padding(0)
            };
            chkContinueOnError = new CheckBox { Text = "エラーが発生しても続行する",              Left = 10, Top = 22, Width = 340, Checked = true };
            chkFkChecks        = new CheckBox { Text = "外部キー制約を無効化 (FK_CHECKS=0)",      Left = 10, Top = 48, Width = 340, Checked = true };
            chkUniqueChecks    = new CheckBox { Text = "インデックス重複チェックを無効化 (UNIQUE_CHECKS=0)  ※データに重複がない場合のみ",
                                                Left = 10, Top = 74, Width = 500, Checked = false };
            grpOpt.Controls.AddRange([chkContinueOnError, chkFkChecks, chkUniqueChecks]);
            configPanel.Controls.Add(grpOpt);
        }

        // ===== ③ プログレスパネル（Bottom） =====
        var progressPanel = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 4, 8, 4) };
        progressBar = new ProgressBar
        {
            Left = 0, Top = 2, Height = 16,
            Minimum = 0, Maximum = 100, Value = 0,
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        lblProgress = new Label
        {
            Left = 0, Top = 20, Height = 16, AutoSize = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            ForeColor = Color.DimGray
        };
        progressPanel.Controls.AddRange([progressBar, lblProgress]);
        progressPanel.Resize += (s, e) =>
        {
            progressBar.Width = progressPanel.Width - 16;
            lblProgress.Width = progressPanel.Width - 16;
        };

        // ===== ④ ボタンパネル（Bottom） =====
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        btnStart = new Button
        {
            Text = isExport ? "エクスポート開始" : "インポート開始",
            Width = 130, Height = 30, Top = 9,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnStop = new Button
        {
            Text = "停止", Width = 80, Height = 30, Top = 9,
            Anchor = AnchorStyles.Right | AnchorStyles.Top, Enabled = false
        };
        btnClose = new Button
        {
            Text = "閉じる", Width = 80, Height = 30, Top = 9,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        btnStart.Click += async (s, e) => await StartAsync();
        btnStop.Click  += (s, e) => _cts?.Cancel();
        btnClose.Click += (s, e) => Close();
        buttonPanel.Controls.AddRange([btnStart, btnStop, btnClose]);
        buttonPanel.Resize += (s, e) => PositionButtons(buttonPanel);

        // ===== ⑤ ログパネル（Fill） =====
        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 4) };
        var lblLog = new Label { Text = "ログ:", Dock = DockStyle.Top, Height = 18 };
        txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = Color.FromArgb(204, 204, 204),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        // logPanel 内は Fill → Top の順
        logPanel.Controls.Add(txtLog);
        logPanel.Controls.Add(lblLog);

        // ===== Controls.Add 順序（WinForms ドッキングルール） =====
        // 処理順 = 逆順。Fill は最後に処理 → 最初に Add
        // Bottom は後から Add するほど上に積まれる → progressPanel を先に Add → buttonPanel を後に Add
        // Top は後から Add するほど下に配置 → filePanel を後に Add → 最上段
        Controls.Add(logPanel);       // Fill  → 処理は最後（残余領域）
        Controls.Add(progressPanel);  // Bottom → buttonPanel の上
        Controls.Add(buttonPanel);    // Bottom → 最下段（最初に処理）
        Controls.Add(configPanel);    // Top   → filePanel の下（2段目）
        Controls.Add(filePanel);      // Top   → 最上段（最後に処理された Top）

        AcceptButton = btnStart;
        CancelButton = btnClose;

        // 初期レイアウト
        PositionButtons(buttonPanel);
        filePanel.Width = ClientSize.Width - filePanel.Padding.Horizontal;
    }

    private void PositionButtons(Panel panel)
    {
        int right = panel.ClientSize.Width - panel.Padding.Right;
        btnClose.Left = right - btnClose.Width;
        btnStop.Left  = btnClose.Left - btnStop.Width - 6;
        btnStart.Left = btnStop.Left  - btnStart.Width - 6;
    }

    // =============================================
    // テーブル一覧の読み込み
    // =============================================

    private async Task LoadTablesAsync()
    {
        if (_mode == SqlIoMode.Import) return;
        try
        {
            var tables = await _dbService.GetTablesAsync(_dbName);
            foreach (var (name, _, _, _) in tables)
                lstTables.Items.Add(name, true);
        }
        catch (Exception ex)
        {
            Log($"テーブル一覧取得失敗: {ex.Message}", Color.OrangeRed);
        }
    }

    // =============================================
    // ファイル参照
    // =============================================

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        if (_mode == SqlIoMode.Export)
        {
            using var dlg = new SaveFileDialog
            {
                Title  = "エクスポート先ファイルを選択",
                Filter = "SQL ファイル (*.sql)|*.sql|すべてのファイル (*.*)|*.*",
                FileName   = $"{_dbName}_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
                DefaultExt = "sql"
            };
            if (dlg.ShowDialog() == DialogResult.OK) txtFilePath.Text = dlg.FileName;
        }
        else
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "インポートするSQLファイルを選択",
                Filter = "SQL ファイル (*.sql)|*.sql|すべてのファイル (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK) txtFilePath.Text = dlg.FileName;
        }
    }

    // =============================================
    // 開始
    // =============================================

    private async Task StartAsync()
    {
        if (_isRunning) return;

        if (string.IsNullOrWhiteSpace(txtFilePath.Text))
        {
            MessageBox.Show("ファイルパスを指定してください。", "入力エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        btnStart.Enabled  = false;
        btnStop.Enabled   = true;
        btnBrowse.Enabled = false;
        txtLog.Clear();
        progressBar.Value = 0;
        lblProgress.Text  = "";

        try
        {
            if (_mode == SqlIoMode.Import)
                await ImportAsync(_cts.Token);
            else
                await ExportAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("─── キャンセルされました ───", Color.Yellow);
        }
        catch (Exception ex)
        {
            Log($"致命的なエラー: {ex.Message}", Color.OrangeRed);
        }
        finally
        {
            _isRunning = false;
            btnStart.Enabled  = true;
            btnStop.Enabled   = false;
            btnBrowse.Enabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // =============================================
    // エクスポート
    // =============================================

    private async Task ExportAsync(CancellationToken ct)
    {
        var selectedTables = lstTables.CheckedItems.Cast<string>().ToList();
        if (selectedTables.Count == 0)
        {
            MessageBox.Show("エクスポートするテーブルを選択してください。", "未選択",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool includeData = chkIncludeData?.Checked ?? true;
        bool dropTable   = chkDropTable?.Checked   ?? true;
        bool disableFk   = chkFkChecks?.Checked    ?? true;

        progressBar.Maximum = selectedTables.Count;

        await using var writer = new StreamWriter(txtFilePath.Text, false, new UTF8Encoding(true));

        // ヘッダー
        await writer.WriteLineAsync("-- ============================================================");
        await writer.WriteLineAsync($"-- Navirat SQL Export");
        await writer.WriteLineAsync($"-- Host    : {_dbName}");
        await writer.WriteLineAsync($"-- Database: `{_dbName}`");
        await writer.WriteLineAsync($"-- Created : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"-- Include Data: {(includeData ? "Yes" : "No")}");
        await writer.WriteLineAsync("-- ============================================================");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("SET NAMES utf8mb4;");
        await writer.WriteLineAsync("SET CHARACTER_SET_CLIENT = utf8mb4;");
        if (disableFk)  await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS = 0;");
        await writer.WriteLineAsync();

        Log($"エクスポート開始: {selectedTables.Count} テーブル  (データ: {(includeData ? "含む" : "含まない")})", Color.Cyan);
        Log($"出力先: {txtFilePath.Text}");
        Log("");

        for (int tIdx = 0; tIdx < selectedTables.Count; tIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var tableName = selectedTables[tIdx];
            progressBar.Value = tIdx;
            lblProgress.Text  = $"テーブル {tIdx + 1} / {selectedTables.Count}: {tableName}";

            Log($"▶ `{tableName}`");

            // ── CREATE TABLE ──
            await writer.WriteLineAsync("-- ============================================================");
            await writer.WriteLineAsync($"-- テーブル構造: `{tableName}`");
            await writer.WriteLineAsync("-- ============================================================");
            await writer.WriteLineAsync();
            if (dropTable) await writer.WriteLineAsync($"DROP TABLE IF EXISTS `{tableName}`;");

            var createSql = await _dbService.GetCreateTableSqlAsync(_dbName, tableName, ct);
            if (!string.IsNullOrEmpty(createSql))
            {
                await writer.WriteLineAsync(createSql + ";");
                Log($"  CREATE TABLE 完了");
            }
            await writer.WriteLineAsync();

            // ── INSERT INTO（データエクスポート）──
            if (includeData)
            {
                await writer.WriteLineAsync($"-- データ: `{tableName}`");
                await writer.WriteLineAsync($"LOCK TABLES `{tableName}` WRITE;");
                await writer.WriteLineAsync();

                long exportedRows = 0;
                long totalRows    = 0;
                bool firstPage    = true;

                for (int page = 1; ; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (data, totalCount) = await _dbService.GetTableDataAsync(_dbName, tableName, page, 1000, null, ct);

                    if (firstPage)
                    {
                        totalRows = totalCount;
                        firstPage = false;
                        if (totalRows == 0) { Log($"  (データなし)"); break; }
                    }

                    foreach (var insertSql in GenerateInsertStatements(tableName, data))
                    {
                        await writer.WriteLineAsync(insertSql);
                        await writer.WriteLineAsync();
                    }

                    exportedRows += data.Rows.Count;
                    int totalPages = (int)Math.Ceiling((double)totalRows / 1000);
                    lblProgress.Text = $"テーブル {tIdx + 1}/{selectedTables.Count}: {tableName} ({exportedRows:N0}/{totalRows:N0} 行)";
                    Log($"  INSERT: {exportedRows:N0} / {totalRows:N0} 行");

                    if (page >= totalPages) break;
                }

                await writer.WriteLineAsync($"UNLOCK TABLES;");
                await writer.WriteLineAsync();
            }
        }

        // フッター
        if (disableFk) await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS = 1;");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"-- Export completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        progressBar.Value = selectedTables.Count;
        Log("", null);
        Log($"✔ エクスポート完了: {txtFilePath.Text}", Color.LimeGreen);
    }

    // =============================================
    // INSERT 文の生成
    // =============================================

    private static IEnumerable<string> GenerateInsertStatements(string tableName, DataTable dt, int batchSize = 100)
    {
        if (dt.Rows.Count == 0) yield break;

        var colNames = string.Join(", ",
            dt.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`"));

        var rowValues = new List<string>(batchSize);

        foreach (DataRow row in dt.Rows)
        {
            var vals = string.Join(", ",
                dt.Columns.Cast<DataColumn>().Select(c =>
                {
                    var v = row[c];
                    if (v == DBNull.Value || v == null) return "NULL";
                    return v switch
                    {
                        bool    b  => b ? "1" : "0",
                        byte[]  ba => $"0x{Convert.ToHexString(ba)}",
                        DateTime d => $"'{d:yyyy-MM-dd HH:mm:ss}'",
                        string  s  => $"'{EscSql(s)}'",
                        _          => v.ToString() ?? "NULL"
                    };
                }));

            rowValues.Add($"  ({vals})");

            if (rowValues.Count >= batchSize)
            {
                yield return $"INSERT INTO `{tableName}` ({colNames}) VALUES\n{string.Join(",\n", rowValues)};";
                rowValues.Clear();
            }
        }

        if (rowValues.Count > 0)
            yield return $"INSERT INTO `{tableName}` ({colNames}) VALUES\n{string.Join(",\n", rowValues)};";
    }

    private static string EscSql(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("'",  "\\'")
         .Replace("\0", "\\0")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\x1a", "\\Z");

    // =============================================
    // インポート
    // =============================================

    private async Task ImportAsync(CancellationToken ct)
    {
        if (!File.Exists(txtFilePath.Text))
        {
            MessageBox.Show("指定したファイルが見つかりません。", "ファイルエラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool continueOnError  = chkContinueOnError?.Checked ?? true;
        bool disableFk        = chkFkChecks?.Checked        ?? true;
        bool disableUnique    = chkUniqueChecks?.Checked    ?? false;

        Log($"インポート開始: {txtFilePath.Text}", Color.Cyan);
        Log($"対象 DB: {_dbName}");
        Log($"FK_CHECKS: {(disableFk ? "無効" : "有効")}  " +
            $"UNIQUE_CHECKS: {(disableUnique ? "無効" : "有効")}  " +
            $"バッチコミット: 5000件ごと");
        Log("");

        var rawSql = await File.ReadAllTextAsync(txtFilePath.Text, Encoding.UTF8, ct);

        // USE / セッション変数を先頭に注入
        var header = new StringBuilder();
        header.AppendLine($"USE `{_dbName}`;");
        if (disableFk)     header.AppendLine("SET FOREIGN_KEY_CHECKS = 0;");
        if (disableUnique) header.AppendLine("SET UNIQUE_CHECKS = 0;");
        var sql = header + rawSql;

        int lastReported = 0;
        var progressHandler = new Progress<(int Current, int Total)>(p =>
        {
            progressBar.Maximum = p.Total;
            progressBar.Value   = Math.Min(p.Current, p.Total);
            lblProgress.Text    = $"ステートメント {p.Current:N0} / {p.Total:N0}";

            // 200件ごとにログ出力（体感と RichTextBox 追記コストのバランス）
            if (p.Current - lastReported >= 200 || p.Current == p.Total)
            {
                Log($"  [{p.Current:N0} / {p.Total:N0}] 実行中...");
                lastReported = p.Current;
            }
        });

        var (success, failed, errors) = await _dbService.ExecuteScriptAsync(sql, continueOnError, progressHandler, ct);

        if (disableFk)
        {
            try { await _dbService.ExecuteNonQueryAsync("SET FOREIGN_KEY_CHECKS = 1;", ct); }
            catch { /* 無視 */ }
        }
        if (disableUnique)
        {
            try { await _dbService.ExecuteNonQueryAsync("SET UNIQUE_CHECKS = 1;", ct); }
            catch { /* 無視 */ }
        }

        progressBar.Value = progressBar.Maximum;
        Log("");

        if (failed == 0)
        {
            Log($"✔ インポート完了: 成功 {success:N0} ステートメント", Color.LimeGreen);
        }
        else
        {
            Log($"⚠ インポート完了: 成功 {success:N0} / 失敗 {failed:N0} ステートメント", Color.Yellow);
            Log("─── エラー詳細 ───", Color.OrangeRed);
            foreach (var err in errors.Take(20))
                Log(err, Color.OrangeRed);
            if (errors.Count > 20)
                Log($"  ... 他 {errors.Count - 20} 件のエラー", Color.OrangeRed);
        }
    }

    // =============================================
    // ログ出力
    // =============================================

    private void Log(string message, Color? color = null)
    {
        if (InvokeRequired) { Invoke(() => Log(message, color)); return; }

        txtLog.SuspendLayout();
        txtLog.SelectionStart  = txtLog.TextLength;
        txtLog.SelectionLength = 0;
        txtLog.SelectionColor  = color ?? Color.FromArgb(204, 204, 204);
        txtLog.AppendText(message + "\n");
        txtLog.ScrollToCaret();
        txtLog.ResumeLayout();
    }
}
