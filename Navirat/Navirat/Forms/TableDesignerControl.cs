using Navirat.Models;
using Navirat.Services;

namespace Navirat.Forms;

/// <summary>
/// テーブル設計画面（新規作成・既存テーブルの変更）
/// </summary>
public class TableDesignerControl : UserControl
{
    private readonly DatabaseService _dbService;
    private readonly string _dbName;
    private readonly string? _tableName;
    private readonly bool _isNew;
    private List<ColumnDefinition> _columns = [];
    private string? _originalTableName;

    // UI コントロール
    private TextBox txtTableName = null!;
    private TextBox txtComment = null!;
    private ComboBox cmbEngine = null!;
    private ComboBox cmbCharset = null!;
    private DataGridView gridColumns = null!;
    private ToolStrip toolbar = null!;
    private Label lblStatus = null!;

    public event Action<string>? TableSaved;

    public TableDesignerControl(DatabaseService dbService, string dbName, string? tableName, bool isNew)
    {
        _dbService = dbService;
        _dbName = dbName;
        _tableName = tableName;
        _isNew = isNew;
        _originalTableName = tableName;

        InitializeComponent();

        if (!isNew && tableName != null)
            _ = LoadTableStructureAsync();
    }

    private void InitializeComponent()
    {
        Font = new Font("Meiryo UI", 9f);

        // ===== ツールバー =====
        toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

        var btnSave = new ToolStripButton("保存 (Ctrl+S)") { ToolTipText = "テーブルを保存" };
        btnSave.Click += (s, e) => _ = SaveTableAsync();

        var btnAddCol = new ToolStripButton("カラムを追加") { ToolTipText = "末尾にカラムを追加" };
        btnAddCol.Click += (s, e) => AddColumn();

        var btnInsertCol = new ToolStripButton("カラムを挿入") { ToolTipText = "選択行の前にカラムを挿入" };
        btnInsertCol.Click += (s, e) => InsertColumn();

        var btnDeleteCol = new ToolStripButton("カラムを削除") { ToolTipText = "選択中のカラムを削除" };
        btnDeleteCol.Click += (s, e) => DeleteColumn();

        var btnMoveUp = new ToolStripButton("↑") { ToolTipText = "上に移動" };
        btnMoveUp.Click += (s, e) => MoveColumn(-1);

        var btnMoveDown = new ToolStripButton("↓") { ToolTipText = "下に移動" };
        btnMoveDown.Click += (s, e) => MoveColumn(1);

        toolbar.Items.AddRange([btnSave, new ToolStripSeparator(),
            btnAddCol, btnInsertCol, btnDeleteCol, new ToolStripSeparator(), btnMoveUp, btnMoveDown]);

        // ===== テーブル設定エリア =====
        var settingsPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(6, 4, 6, 4)
        };

        var lblTableName = new Label { Text = "テーブル名:", Left = 6, Top = 10, Width = 80, AutoSize = false };
        txtTableName = new TextBox { Left = 90, Top = 7, Width = 200, Text = _tableName ?? "" };

        var lblEngine = new Label { Text = "エンジン:", Left = 310, Top = 10, Width = 65, AutoSize = false };
        cmbEngine = new ComboBox { Left = 380, Top = 7, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEngine.Items.AddRange(["InnoDB", "MyISAM", "MEMORY", "CSV", "ARCHIVE"]);
        cmbEngine.SelectedItem = "InnoDB";

        var lblCharset = new Label { Text = "文字セット:", Left = 498, Top = 10, Width = 75, AutoSize = false };
        cmbCharset = new ComboBox { Left = 578, Top = 7, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbCharset.Items.AddRange(["utf8mb4", "utf8", "latin1", "ascii", "cp932", "ujis"]);
        cmbCharset.SelectedItem = "utf8mb4";

        var lblComment = new Label { Text = "コメント:", Left = 6, Top = 36, Width = 80, AutoSize = false };
        txtComment = new TextBox { Left = 90, Top = 33, Width = 300 };

        settingsPanel.Controls.AddRange([lblTableName, txtTableName, lblEngine, cmbEngine,
            lblCharset, cmbCharset, lblComment, txtComment]);

        // ===== カラムグリッド =====
        gridColumns = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            RowHeadersVisible = true,
            RowHeadersWidth = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            BackgroundColor = Color.White,
            GridColor = Color.LightGray,
            BorderStyle = BorderStyle.None,
            Font = new Font("Meiryo UI", 9f)
        };

        BuildGridColumns();

        // ===== ステータスバー =====
        lblStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Text = _isNew ? "新しいテーブルを設計してください。" : "テーブル構造を読み込み中...",
            ForeColor = Color.DimGray,
            Padding = new Padding(6, 3, 0, 0)
        };

        // キーボードショートカット
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
                _ = SaveTableAsync();
        };

        // ドッキング順序: Fill → Top群（下→上の順） → Bottom
        // WinForms は Controls を逆順でドッキング処理するため：
        //   [1] Fill を最初に Add → 処理は最後（残余領域を取る）
        //   [2] settingsPanel（toolbar の直下に来る Top）→ toolbar より先に処理させるため後から Add
        //   [3] toolbar（最上段の Top）→ settingsPanel の後に Add → 処理が settingsPanel より先 → 最上段に配置
        //   [4] lblStatus（Bottom）→ 最後に Add → 処理が最初 → 最下段に確保
        Controls.Add(gridColumns);    // [1] Fill
        Controls.Add(settingsPanel);  // [2] Top (2段目)
        Controls.Add(toolbar);        // [3] Top (1段目・最上段)
        Controls.Add(lblStatus);      // [4] Bottom
    }

    private void BuildGridColumns()
    {
        // # 列（行番号）
        var colNo = new DataGridViewTextBoxColumn
        {
            HeaderText = "#",
            Name = "colNo",
            Width = 36,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // カラム名
        var colName = new DataGridViewTextBoxColumn
        {
            HeaderText = "カラム名",
            Name = "colName",
            Width = 160,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // データ型
        var colType = new DataGridViewComboBoxColumn
        {
            HeaderText = "データ型",
            Name = "colType",
            Width = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colType.Items.AddRange(MySqlDataTypes.All);

        // 長さ/値
        var colLength = new DataGridViewTextBoxColumn
        {
            HeaderText = "長さ/値",
            Name = "colLength",
            Width = 80,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // NOT NULL
        var colNotNull = new DataGridViewCheckBoxColumn
        {
            HeaderText = "NOT NULL",
            Name = "colNotNull",
            Width = 70,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // PRIMARY KEY
        var colPk = new DataGridViewCheckBoxColumn
        {
            HeaderText = "PK",
            Name = "colPk",
            Width = 45,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // AUTO_INCREMENT
        var colAi = new DataGridViewCheckBoxColumn
        {
            HeaderText = "AI",
            Name = "colAi",
            Width = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // UNSIGNED
        var colUnsigned = new DataGridViewCheckBoxColumn
        {
            HeaderText = "符号なし",
            Name = "colUnsigned",
            Width = 65,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // デフォルト値
        var colDefault = new DataGridViewTextBoxColumn
        {
            HeaderText = "デフォルト値",
            Name = "colDefault",
            Width = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        // コメント
        var colComment = new DataGridViewTextBoxColumn
        {
            HeaderText = "コメント",
            Name = "colComment",
            Width = 180,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        gridColumns.Columns.AddRange([colNo, colName, colType, colLength,
            colNotNull, colPk, colAi, colUnsigned, colDefault, colComment]);
    }

    private async Task LoadTableStructureAsync()
    {
        try
        {
            _columns = await _dbService.GetColumnsAsync(_dbName, _tableName!);
            RefreshGrid();
            lblStatus.Text = $"カラム数: {_columns.Count}";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"読み込みエラー: {ex.Message}";
        }
    }

    private void RefreshGrid()
    {
        gridColumns.Rows.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            gridColumns.Rows.Add(
                i + 1,
                col.Name,
                col.DataType.ToUpper(),
                col.Length?.ToString() ?? "",
                !col.IsNullable,
                col.IsPrimaryKey,
                col.IsAutoIncrement,
                col.IsUnsigned,
                col.DefaultValue ?? "",
                col.Comment
            );
        }
    }

    private ColumnDefinition? ReadColumnFromRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= gridColumns.Rows.Count) return null;
        var row = gridColumns.Rows[rowIndex];

        var name = row.Cells["colName"].Value?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return null;

        var col = new ColumnDefinition
        {
            Name = name,
            DataType = row.Cells["colType"].Value?.ToString() ?? "VARCHAR",
            IsNullable = !(bool)(row.Cells["colNotNull"].Value ?? false),
            IsPrimaryKey = (bool)(row.Cells["colPk"].Value ?? false),
            IsAutoIncrement = (bool)(row.Cells["colAi"].Value ?? false),
            IsUnsigned = (bool)(row.Cells["colUnsigned"].Value ?? false),
            Comment = row.Cells["colComment"].Value?.ToString() ?? ""
        };

        var lenStr = row.Cells["colLength"].Value?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(lenStr) && int.TryParse(lenStr, out int len))
            col.Length = len;

        var defVal = row.Cells["colDefault"].Value?.ToString();
        if (!string.IsNullOrEmpty(defVal))
            col.DefaultValue = defVal;

        return col;
    }

    private List<ColumnDefinition> ReadAllColumns()
    {
        var result = new List<ColumnDefinition>();
        for (int i = 0; i < gridColumns.Rows.Count; i++)
        {
            var col = ReadColumnFromRow(i);
            if (col != null) result.Add(col);
        }
        return result;
    }

    private void AddColumn()
    {
        _columns.Add(new ColumnDefinition { Name = $"col{_columns.Count + 1}", DataType = "VARCHAR", Length = 255 });
        RefreshGrid();
        gridColumns.ClearSelection();
        gridColumns.Rows[gridColumns.Rows.Count - 1].Selected = true;
        gridColumns.FirstDisplayedScrollingRowIndex = gridColumns.Rows.Count - 1;
        lblStatus.Text = $"カラム数: {_columns.Count}";
    }

    private void InsertColumn()
    {
        int idx = gridColumns.SelectedRows.Count > 0
            ? gridColumns.SelectedRows[0].Index
            : _columns.Count;

        _columns.Insert(idx, new ColumnDefinition { Name = $"col{idx + 1}", DataType = "VARCHAR", Length = 255 });
        RefreshGrid();
        gridColumns.Rows[idx].Selected = true;
        lblStatus.Text = $"カラム数: {_columns.Count}";
    }

    private void DeleteColumn()
    {
        if (gridColumns.SelectedRows.Count == 0) return;

        int idx = gridColumns.SelectedRows[0].Index;
        if (idx < 0 || idx >= _columns.Count) return;

        var colName = _columns[idx].Name;
        var result = MessageBox.Show(
            $"カラム '{colName}' を削除しますか？",
            "カラムの削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        _columns.RemoveAt(idx);
        RefreshGrid();
        lblStatus.Text = $"カラム数: {_columns.Count}";
    }

    private void MoveColumn(int direction)
    {
        if (gridColumns.SelectedRows.Count == 0) return;
        int idx = gridColumns.SelectedRows[0].Index;
        int newIdx = idx + direction;

        if (newIdx < 0 || newIdx >= _columns.Count) return;

        // グリッドから最新の値を読み直す
        _columns = ReadAllColumns();

        var col = _columns[idx];
        _columns.RemoveAt(idx);
        _columns.Insert(newIdx, col);
        RefreshGrid();
        gridColumns.Rows[newIdx].Selected = true;
    }

    private async Task SaveTableAsync()
    {
        // グリッドの変更を読み取り
        _columns = ReadAllColumns();

        var tableName = txtTableName.Text.Trim();
        if (string.IsNullOrEmpty(tableName))
        {
            MessageBox.Show("テーブル名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_columns.Count == 0)
        {
            MessageBox.Show("最低1つのカラムが必要です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string engine = cmbEngine.SelectedItem?.ToString() ?? "InnoDB";
        string charset = cmbCharset.SelectedItem?.ToString() ?? "utf8mb4";
        string comment = txtComment.Text.Trim();

        lblStatus.Text = "保存中...";

        try
        {
            if (_isNew)
            {
                await _dbService.CreateTableAsync(_dbName, tableName, _columns, engine, charset, comment);
                lblStatus.Text = $"テーブル '{tableName}' を作成しました。";
                MessageBox.Show($"テーブル '{tableName}' を作成しました。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                // 既存テーブルの変更（カラムの追加/変更）
                await AlterTableAsync(tableName, _columns);
                lblStatus.Text = $"テーブル '{tableName}' を更新しました。";
                MessageBox.Show($"テーブル '{tableName}' を更新しました。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            TableSaved?.Invoke(tableName);
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"保存に失敗しました: {ex.Message}";
            MessageBox.Show($"保存に失敗しました。\n\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task AlterTableAsync(string newTableName, List<ColumnDefinition> newColumns)
    {
        // 既存カラムと差分比較して ALTER TABLE を実行
        var existingColumns = await _dbService.GetColumnsAsync(_dbName, _originalTableName!);
        var existingNames = existingColumns.Select(c => c.Name).ToHashSet();
        var newNames = newColumns.Select(c => c.Name).ToHashSet();

        // 削除されたカラム
        foreach (var existing in existingColumns)
        {
            if (!newNames.Contains(existing.Name))
                await _dbService.DropColumnAsync(_dbName, _originalTableName!, existing.Name);
        }

        // 追加/変更されたカラム
        string? afterCol = null;
        foreach (var newCol in newColumns)
        {
            if (!existingNames.Contains(newCol.Name))
                await _dbService.AddColumnAsync(_dbName, _originalTableName!, newCol, afterCol);
            else
                await _dbService.ModifyColumnAsync(_dbName, _originalTableName!, newCol.Name, newCol);

            afterCol = newCol.Name;
        }
    }
}
