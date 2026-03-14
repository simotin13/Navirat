using CsvHelper;
using CsvHelper.Configuration;
using Navirat.Services;
using System.Data;
using System.Globalization;
using System.Text;

namespace Navirat.Forms;

public enum ImportExportMode { Import, Export }

/// <summary>
/// データインポート/エクスポートフォーム
/// </summary>
public class ImportExportForm : Form
{
    private readonly DatabaseService _dbService;
    private readonly string _dbName;
    private readonly string _tableName;
    private readonly ImportExportMode _mode;

    // UI コントロール
    private TextBox txtFilePath = null!;
    private ComboBox cmbFormat = null!;
    private ComboBox cmbEncoding = null!;
    private CheckBox chkFirstRowHeader = null!;
    private CheckBox chkCreateTable = null!;
    private RichTextBox txtLog = null!;
    private Button btnStart = null!;
    private Button btnBrowse = null!;
    private ProgressBar progressBar = null!;
    private Label lblStatus = null!;

    public ImportExportForm(DatabaseService dbService, string dbName, string tableName, ImportExportMode mode)
    {
        _dbService = dbService;
        _dbName = dbName;
        _tableName = tableName;
        _mode = mode;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = _mode == ImportExportMode.Import
            ? $"インポート: {_dbName}.{_tableName}"
            : $"エクスポート: {_dbName}.{_tableName}";
        Size = new Size(580, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Meiryo UI", 9f);

        int y = 16;
        int labelWidth = 120;
        int inputLeft = 140;

        // ===== ファイルパス =====
        var lblFile = new Label { Text = "ファイル:", Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
        txtFilePath = new TextBox { Left = inputLeft, Top = y, Width = 340 };
        btnBrowse = new Button { Text = "参照...", Left = 490, Top = y - 1, Width = 70, Height = 24 };
        btnBrowse.Click += BtnBrowse_Click;
        Controls.AddRange([lblFile, txtFilePath, btnBrowse]);
        y += 34;

        // ===== フォーマット =====
        var lblFormat = new Label { Text = "フォーマット:", Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
        cmbFormat = new ComboBox { Left = inputLeft, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbFormat.Items.AddRange(["CSV", "TSV", "SQL"]);
        cmbFormat.SelectedIndex = 0;
        cmbFormat.SelectedIndexChanged += (s, e) => UpdateUi();
        Controls.AddRange([lblFormat, cmbFormat]);
        y += 34;

        // ===== エンコーディング =====
        var lblEncoding = new Label { Text = "文字コード:", Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
        cmbEncoding = new ComboBox { Left = inputLeft, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEncoding.Items.AddRange(["UTF-8", "UTF-8 BOM", "Shift-JIS", "EUC-JP"]);
        cmbEncoding.SelectedIndex = 0;
        Controls.AddRange([lblEncoding, cmbEncoding]);
        y += 34;

        // ===== オプション =====
        chkFirstRowHeader = new CheckBox
        {
            Text = "先頭行をヘッダーとして扱う",
            Left = inputLeft, Top = y, AutoSize = true, Checked = true
        };
        Controls.Add(chkFirstRowHeader);
        y += 28;

        if (_mode == ImportExportMode.Import)
        {
            chkCreateTable = new CheckBox
            {
                Text = "インポート前にデータを全削除 (TRUNCATE)",
                Left = inputLeft, Top = y, AutoSize = true
            };
            Controls.Add(chkCreateTable);
            y += 28;
        }

        y += 8;

        // ===== 開始ボタン =====
        btnStart = new Button
        {
            Text = _mode == ImportExportMode.Import ? "インポート開始" : "エクスポート開始",
            Left = inputLeft, Top = y,
            Width = 160, Height = 32,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnStart.Click += BtnStart_Click;
        Controls.Add(btnStart);
        y += 46;

        // ===== プログレスバー =====
        progressBar = new ProgressBar
        {
            Left = 12, Top = y, Width = 548, Height = 18,
            Style = ProgressBarStyle.Marquee,
            Visible = false
        };
        Controls.Add(progressBar);
        y += 26;

        // ===== ステータスラベル =====
        lblStatus = new Label
        {
            Left = 12, Top = y, Width = 548,
            AutoSize = false, ForeColor = Color.DimGray
        };
        Controls.Add(lblStatus);
        y += 28;

        // ===== ログ =====
        var lblLog = new Label { Text = "ログ:", Left = 12, Top = y, AutoSize = true };
        Controls.Add(lblLog);
        y += 20;

        txtLog = new RichTextBox
        {
            Left = 12, Top = y, Width = 548,
            Height = 520 - y - 54,
            ReadOnly = true,
            BackColor = Color.FromArgb(250, 250, 250),
            Font = new Font("Consolas", 8.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        Controls.Add(txtLog);

        UpdateUi();
    }

    private void UpdateUi()
    {
        bool isSql = cmbFormat.SelectedItem?.ToString() == "SQL";
        chkFirstRowHeader.Enabled = !isSql;
        cmbEncoding.Enabled = !isSql;
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        if (_mode == ImportExportMode.Import)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "インポートファイルを選択",
                Filter = "CSV/TSV ファイル (*.csv;*.tsv)|*.csv;*.tsv|SQL ファイル (*.sql)|*.sql|すべてのファイル (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtFilePath.Text = dlg.FileName;
        }
        else
        {
            using var dlg = new SaveFileDialog
            {
                Title = "エクスポート先を指定",
                FileName = $"{_tableName}.csv",
                Filter = "CSV ファイル (*.csv)|*.csv|TSV ファイル (*.tsv)|*.tsv|SQL ファイル (*.sql)|*.sql|すべてのファイル (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtFilePath.Text = dlg.FileName;
        }
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        var filePath = txtFilePath.Text.Trim();

        if (string.IsNullOrEmpty(filePath))
        {
            MessageBox.Show("ファイルパスを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnStart.Enabled = false;
        progressBar.Visible = true;
        txtLog.Clear();

        try
        {
            if (_mode == ImportExportMode.Import)
                await ImportDataAsync(filePath);
            else
                await ExportDataAsync(filePath);
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}", Color.Red);
            MessageBox.Show($"処理中にエラーが発生しました。\n\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnStart.Enabled = true;
            progressBar.Visible = false;
        }
    }

    // =============================================
    // インポート処理
    // =============================================

    private async Task ImportDataAsync(string filePath)
    {
        var format = cmbFormat.SelectedItem?.ToString() ?? "CSV";

        if (format == "SQL")
        {
            await ImportSqlAsync(filePath);
            return;
        }

        Log($"インポート開始: {filePath}");
        Log($"テーブル: {_dbName}.{_tableName}");

        var encoding = GetEncoding();
        char delimiter = format == "TSV" ? '\t' : ',';
        bool hasHeader = chkFirstRowHeader.Checked;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = hasHeader,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StreamReader(filePath, encoding);
        using var csv = new CsvReader(reader, config);

        var dt = new DataTable();
        int rowCount = 0;

        await Task.Run(() =>
        {
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];

            foreach (var h in headers)
                dt.Columns.Add(h ?? $"col{dt.Columns.Count}");

            while (csv.Read())
            {
                var row = dt.NewRow();
                for (int i = 0; i < headers.Length; i++)
                    row[i] = csv.TryGetField(i, out string? val) ? (val != null ? (object)val : DBNull.Value) : DBNull.Value;
                dt.Rows.Add(row);
                rowCount++;
            }
        });

        Log($"CSVファイルを読み込みました: {rowCount:N0} 行");

        if (chkCreateTable?.Checked == true)
        {
            await _dbService.TruncateTableAsync(_dbName, _tableName);
            Log($"テーブル '{_tableName}' をトランケートしました。");
        }

        lblStatus.Text = "データを挿入中...";
        await _dbService.BulkInsertAsync(_dbName, _tableName, dt);

        Log($"インポート完了: {rowCount:N0} 行を挿入しました。", Color.Green);
        lblStatus.Text = $"インポート完了: {rowCount:N0} 行";
        MessageBox.Show($"{rowCount:N0} 行をインポートしました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ImportSqlAsync(string filePath)
    {
        Log($"SQL インポート開始: {filePath}");
        var sql = await File.ReadAllTextAsync(filePath);

        await _dbService.ExecuteMultipleAsync(sql);
        Log("SQL インポート完了", Color.Green);
        lblStatus.Text = "SQL インポート完了";
    }

    // =============================================
    // エクスポート処理
    // =============================================

    private async Task ExportDataAsync(string filePath)
    {
        var format = cmbFormat.SelectedItem?.ToString() ?? "CSV";

        Log($"エクスポート開始: {filePath}");
        Log($"テーブル: {_dbName}.{_tableName}");
        lblStatus.Text = "データを読み込み中...";

        if (format == "SQL")
        {
            await ExportSqlAsync(filePath);
            return;
        }

        var dt = await _dbService.GetAllTableDataAsync(_dbName, _tableName);
        Log($"データを読み込みました: {dt.Rows.Count:N0} 行");

        var encoding = GetEncoding();
        char delimiter = format == "TSV" ? '\t' : ',';
        bool hasHeader = chkFirstRowHeader.Checked;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = hasHeader,
        };

        // UTF-8 BOM の場合は BOM 付きで書き込む
        Encoding writeEncoding = cmbEncoding.SelectedItem?.ToString() == "UTF-8 BOM"
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : encoding;

        await Task.Run(() =>
        {
            using var writer = new StreamWriter(filePath, false, writeEncoding);
            using var csv = new CsvWriter(writer, config);

            if (hasHeader)
            {
                foreach (DataColumn col in dt.Columns)
                    csv.WriteField(col.ColumnName);
                csv.NextRecord();
            }

            foreach (DataRow row in dt.Rows)
            {
                foreach (var item in row.ItemArray)
                {
                    if (item == null || item == DBNull.Value)
                        csv.WriteField("");
                    else
                        csv.WriteField(item.ToString());
                }
                csv.NextRecord();
            }
        });

        Log($"エクスポート完了: {dt.Rows.Count:N0} 行を書き出しました。", Color.Green);
        lblStatus.Text = $"エクスポート完了: {dt.Rows.Count:N0} 行";
        MessageBox.Show($"{dt.Rows.Count:N0} 行をエクスポートしました。\n\n{filePath}", "完了",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ExportSqlAsync(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Generated by Navirat");
        sb.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Database: {_dbName}");
        sb.AppendLine($"-- Table: {_tableName}");
        sb.AppendLine();

        // CREATE TABLE文
        lblStatus.Text = "テーブル定義を生成中...";
        var createSql = await _dbService.GetCreateTableSqlAsync(_dbName, _tableName);
        sb.AppendLine("-- Table structure");
        sb.AppendLine($"DROP TABLE IF EXISTS `{_tableName}`;");
        sb.AppendLine(createSql + ";");
        sb.AppendLine();

        // INSERT文
        lblStatus.Text = "データを読み込み中...";
        var dt = await _dbService.GetAllTableDataAsync(_dbName, _tableName);
        Log($"データを読み込みました: {dt.Rows.Count:N0} 行");

        if (dt.Rows.Count > 0)
        {
            sb.AppendLine("-- Data");
            sb.AppendLine($"LOCK TABLES `{_tableName}` WRITE;");

            // 1000行ずつにまとめて INSERT
            int batchSize = 500;
            var columns = dt.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`").ToList();
            string colList = string.Join(", ", columns);

            for (int i = 0; i < dt.Rows.Count; i += batchSize)
            {
                var batch = dt.Rows.Cast<DataRow>().Skip(i).Take(batchSize).ToList();
                sb.Append($"INSERT INTO `{_tableName}` ({colList}) VALUES\n");

                var valueLines = batch.Select(row =>
                {
                    var vals = row.ItemArray.Select(v =>
                        v == null || v == DBNull.Value
                            ? "NULL"
                            : $"'{v.ToString()!.Replace("\\", "\\\\").Replace("'", "\\'")}'");
                    return "  (" + string.Join(", ", vals) + ")";
                });

                sb.AppendLine(string.Join(",\n", valueLines) + ";");
            }

            sb.AppendLine("UNLOCK TABLES;");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        Log($"SQL エクスポート完了: {dt.Rows.Count:N0} 行", Color.Green);
        lblStatus.Text = $"SQL エクスポート完了: {dt.Rows.Count:N0} 行";
        MessageBox.Show($"SQLをエクスポートしました。\n\n{filePath}", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // =============================================
    // ユーティリティ
    // =============================================

    private Encoding GetEncoding()
    {
        return cmbEncoding.SelectedItem?.ToString() switch
        {
            "UTF-8" or "UTF-8 BOM" => Encoding.UTF8,
            "Shift-JIS" => Encoding.GetEncoding("shift_jis"),
            "EUC-JP" => Encoding.GetEncoding("euc-jp"),
            _ => Encoding.UTF8
        };
    }

    private void Log(string message, Color? color = null)
    {
        if (InvokeRequired) { Invoke(() => Log(message, color)); return; }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formatted = $"[{timestamp}] {message}\n";

        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionLength = 0;
        txtLog.SelectionColor = color ?? Color.FromArgb(40, 40, 40);
        txtLog.AppendText(formatted);
        txtLog.ScrollToCaret();
    }
}
