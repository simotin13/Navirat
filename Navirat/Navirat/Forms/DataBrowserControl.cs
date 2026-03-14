using Navirat.Services;
using System.Data;
using System.Text;

namespace Navirat.Forms;

/// <summary>
/// テーブルデータブラウザ（ページング・CRUD 対応）
/// </summary>
public class DataBrowserControl : UserControl
{
    private readonly DatabaseService _dbService;
    private readonly string _dbName;
    private readonly string _tableName;

    private const int PAGE_SIZE = 1000;
    private int _currentPage = 1;
    private long _totalCount = 0;
    private int _totalPages = 1;

    // データ管理
    private DataTable? _dataTable;
    private List<string> _primaryKeyColumns = [];
    private bool _suppressEvents = false;

    // UI コントロール
    private DataGridView grid = null!;
    private ToolStrip toolbar = null!;
    private StatusStrip statusBar = null!;
    private ToolStripStatusLabel lblStatus = null!;
    private ToolStripButton btnFirst = null!;
    private ToolStripButton btnPrev = null!;
    private ToolStripButton btnNext = null!;
    private ToolStripButton btnLast = null!;
    private ToolStripLabel lblPageInfo = null!;
    private ToolStripTextBox txtPage = null!;
    private ToolStripButton btnRefresh = null!;
    private ToolStripButton btnAddRow = null!;
    private ToolStripButton btnDeleteRow = null!;

    public DataBrowserControl(DatabaseService dbService, string dbName, string tableName)
    {
        _dbService = dbService;
        _dbName = dbName;
        _tableName = tableName;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Font = new Font("Meiryo UI", 9f);

        // ===== ツールバー =====
        toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

        btnRefresh = new ToolStripButton("更新 (F5)") { ToolTipText = "データを再読み込み" };
        btnRefresh.Click += (s, e) => _ = LoadDataAsync();

        btnAddRow = new ToolStripButton("行を追加") { ToolTipText = "新しい行を追加 (Insert)", Enabled = false };
        btnAddRow.Click += (s, e) => BeginAddNewRow();

        btnDeleteRow = new ToolStripButton("行を削除") { ToolTipText = "選択した行を削除 (Delete)", Enabled = false };
        btnDeleteRow.Click += async (s, e) => await DeleteSelectedRowsAsync();

        toolbar.Items.AddRange([btnRefresh, new ToolStripSeparator(),
            btnAddRow, btnDeleteRow, new ToolStripSeparator()]);

        // ページングコントロール
        btnFirst = new ToolStripButton("|◀") { ToolTipText = "最初のページ", Enabled = false };
        btnFirst.Click += (s, e) => _ = GotoPageAsync(1);

        btnPrev = new ToolStripButton("◀") { ToolTipText = "前のページ", Enabled = false };
        btnPrev.Click += (s, e) => _ = GotoPageAsync(_currentPage - 1);

        lblPageInfo = new ToolStripLabel("ページ: - / -");

        txtPage = new ToolStripTextBox { Width = 45, Text = "1" };
        txtPage.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Return && int.TryParse(txtPage.Text, out int p))
                _ = GotoPageAsync(p);
        };

        btnNext = new ToolStripButton("▶") { ToolTipText = "次のページ", Enabled = false };
        btnNext.Click += (s, e) => _ = GotoPageAsync(_currentPage + 1);

        btnLast = new ToolStripButton("▶|") { ToolTipText = "最後のページ", Enabled = false };
        btnLast.Click += (s, e) => _ = GotoPageAsync(_totalPages);

        toolbar.Items.AddRange([btnFirst, btnPrev, new ToolStripLabel(" "),
            lblPageInfo, new ToolStripLabel(" ページ: "), txtPage, new ToolStripLabel(" "),
            btnNext, btnLast]);

        // ===== グリッド =====
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = false,
            AllowUserToAddRows = false,      // PK確認後に有効化
            AllowUserToDeleteRows = false,   // 右クリックメニューで制御
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            RowHeadersVisible = true,
            RowHeadersWidth = 24,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText,
            BackgroundColor = Color.White,
            GridColor = Color.LightGray,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Both,
            VirtualMode = false
        };

        // 右クリック時に対象セルへ移動
        grid.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = grid.HitTest(e.X, e.Y);
                if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
                    grid.CurrentCell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
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
        grid.DataError += (s, e) =>
        {
            e.Cancel = true;
            SetStatus($"表示エラー (列: {e.ColumnIndex}, 行: {e.RowIndex}): {e.Exception?.Message}");
        };

        // 列幅の自動調整（ヘッダ名・セル値を含む）
        grid.DataBindingComplete += (s, e) =>
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                int w = col.Width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Width = Math.Min(w, 250);
                col.MinimumWidth = 60;
            }
        };

        // 編集終了時：空文字 → DBNull.Value
        grid.CellEndEdit += Grid_CellEndEdit;

        // 行を離れた時に自動保存
        grid.RowValidated += Grid_RowValidated;

        // キーボード操作
        grid.KeyDown += Grid_KeyDown;

        // コンテキストメニュー
        BuildContextMenu();

        // ===== ステータスバー =====
        statusBar = new StatusStrip();
        lblStatus = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusBar.Items.Add(lblStatus);

        // ドッキング順序: Fill → Top → Bottom
        Controls.Add(grid);
        Controls.Add(toolbar);
        Controls.Add(statusBar);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var addItem = new ToolStripMenuItem("行を追加 (Insert)");
        addItem.Click += (s, e) => BeginAddNewRow();
        menu.Items.Add(addItem);

        var deleteItem = new ToolStripMenuItem("行を削除 (Delete)");
        deleteItem.Click += async (s, e) => await DeleteSelectedRowsAsync();
        menu.Items.Add(deleteItem);

        menu.Items.Add(new ToolStripSeparator());

        var revertItem = new ToolStripMenuItem("変更を元に戻す");
        revertItem.Click += (s, e) => RevertCurrentRow();
        menu.Items.Add(revertItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("コピー (Ctrl+C)", null, (s, e) =>
        {
            if (grid.GetCellCount(DataGridViewElementStates.Selected) > 0)
                Clipboard.SetDataObject(grid.GetClipboardContent());
        });
        menu.Items.Add("行をコピー (TSV)", null, (s, e) => CopyRowsAsTsv());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("全選択", null, (s, e) => grid.SelectAll());

        grid.ContextMenuStrip = menu;
    }

    // =============================================
    // イベントハンドラ
    // =============================================

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (grid.Rows[e.RowIndex].IsNewRow) return;

        // 空文字列 → DBNull.Value（NULL として保存する）
        var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (cell.Value is string str && str == "")
            cell.Value = DBNull.Value;
    }

    private async void Grid_RowValidated(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressEvents || _dataTable == null) return;
        if (e.RowIndex < 0 || e.RowIndex >= _dataTable.Rows.Count) return;

        var row = _dataTable.Rows[e.RowIndex];
        try
        {
            switch (row.RowState)
            {
                case DataRowState.Modified:
                    await UpdateRowAsync(row, e.RowIndex);
                    break;
                case DataRowState.Added:
                    await InsertRowAsync(row, e.RowIndex);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"保存エラー: {ex.Message}");
            MessageBox.Show($"変更の保存に失敗しました。\n\n{ex.Message}", "保存エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _suppressEvents = true;
            try { row.RejectChanges(); }
            finally { _suppressEvents = false; }
        }
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F5:
                _ = LoadDataAsync();
                break;
            case Keys.Delete when !grid.IsCurrentCellInEditMode:
                e.Handled = true;
                _ = DeleteSelectedRowsAsync();
                break;
            case Keys.Insert:
                e.Handled = true;
                BeginAddNewRow();
                break;
        }
    }

    // =============================================
    // データ読み込み
    // =============================================

    public async Task LoadDataAsync()
    {
        // 主キー情報を取得（テーブルごとに1回だけ）
        if (_primaryKeyColumns.Count == 0)
        {
            try
            {
                var cols = await _dbService.GetColumnsAsync(_dbName, _tableName);
                _primaryKeyColumns = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
            }
            catch { /* PK取得失敗は無視して続行 */ }
        }

        await GotoPageAsync(1);
    }

    private async Task GotoPageAsync(int page)
    {
        if (page < 1) page = 1;

        SetStatus("読み込み中...");
        SetPagingEnabled(false);
        btnRefresh.Enabled = false;
        Cursor = Cursors.WaitCursor;

        try
        {
            var (data, totalCount) = await _dbService.GetTableDataAsync(_dbName, _tableName, page, PAGE_SIZE);

            _totalCount = totalCount;
            _totalPages = (int)Math.Ceiling((double)totalCount / PAGE_SIZE);
            if (_totalPages < 1) _totalPages = 1;
            _currentPage = Math.Min(page, _totalPages);

            // DataSource 切り替え中はイベントを抑制
            _suppressEvents = true;
            try
            {
                _dataTable = data;
                grid.DataSource = null;
                grid.DataSource = _dataTable;
            }
            finally
            {
                _suppressEvents = false;
            }

            // 主キーがない場合は読み取り専用
            bool hasPk = _primaryKeyColumns.Count > 0;
            grid.ReadOnly = !hasPk;
            grid.AllowUserToAddRows = hasPk;
            btnAddRow.Enabled = hasPk;
            btnDeleteRow.Enabled = hasPk;

            UpdatePagingControls();
            SetStatus($"合計: {_totalCount:N0} 行  |  " +
                      $"表示: {(_currentPage - 1) * PAGE_SIZE + 1:N0} 〜 {Math.Min(_currentPage * PAGE_SIZE, _totalCount):N0} 行  |  " +
                      $"1ページ {PAGE_SIZE:N0} 件" +
                      (hasPk ? "" : "  ※主キーなし（読み取り専用）"));
        }
        catch (Exception ex)
        {
            SetStatus($"エラー: {ex.Message}");
            MessageBox.Show($"データの読み込みに失敗しました。\n\n{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnRefresh.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void SetPagingEnabled(bool enabled)
    {
        btnFirst.Enabled = enabled && _currentPage > 1;
        btnPrev.Enabled  = enabled && _currentPage > 1;
        btnNext.Enabled  = enabled && _currentPage < _totalPages;
        btnLast.Enabled  = enabled && _currentPage < _totalPages;
    }

    private void UpdatePagingControls()
    {
        lblPageInfo.Text = $"{_currentPage:N0} / {_totalPages:N0} ページ";
        txtPage.Text = _currentPage.ToString();
        SetPagingEnabled(true);
    }

    // =============================================
    // CRUD 操作
    // =============================================

    /// <summary>グリッド末尾の新規行にフォーカスして入力を開始する。</summary>
    private void BeginAddNewRow()
    {
        if (!btnAddRow.Enabled || _dataTable == null) return;

        grid.EndEdit();
        // AllowUserToAddRows=true のとき、最終行は常に空のプレースホルダ
        int newRowIdx = grid.Rows.Count - 1;
        if (newRowIdx >= 0 && grid.Rows[newRowIdx].IsNewRow && grid.Columns.Count > 0)
        {
            grid.CurrentCell = grid.Rows[newRowIdx].Cells[0];
            grid.BeginEdit(true);
        }
    }

    /// <summary>既存行を UPDATE する。</summary>
    private async Task UpdateRowAsync(DataRow row, int rowIndex)
    {
        if (_primaryKeyColumns.Count == 0)
        {
            row.RejectChanges();
            return;
        }

        var setParts   = new List<string>();
        var whereParts = new List<string>();
        var parameters = new List<(string, object?)>();

        // SET 句：全カラムを更新対象にする（PK も含む）
        foreach (DataColumn col in _dataTable!.Columns)
        {
            var pName = $"@set_{col.ColumnName}";
            setParts.Add($"`{Esc(col.ColumnName)}` = {pName}");
            var val = row[col.ColumnName];
            parameters.Add((pName, val == DBNull.Value ? null : val));
        }

        // WHERE 句：主キーの元の値（編集前）で特定
        foreach (var pk in _primaryKeyColumns)
        {
            var pName = $"@where_{pk}";
            whereParts.Add($"`{Esc(pk)}` = {pName}");
            var origVal = row[pk, DataRowVersion.Original];
            parameters.Add((pName, origVal == DBNull.Value ? null : origVal));
        }

        var sql = $"UPDATE `{Esc(_dbName)}`.`{Esc(_tableName)}` " +
                  $"SET {string.Join(", ", setParts)} " +
                  $"WHERE {string.Join(" AND ", whereParts)} LIMIT 1";

        int affected = await _dbService.ExecuteNonQueryWithParamsAsync(sql, parameters);
        if (affected > 0)
        {
            _suppressEvents = true;
            try { row.AcceptChanges(); }
            finally { _suppressEvents = false; }
            SetStatus($"行 {rowIndex + 1} を更新しました。");
        }
        else
        {
            throw new Exception("対象レコードが見つかりませんでした。他のユーザーによって変更または削除された可能性があります。");
        }
    }

    /// <summary>新規行を INSERT する。</summary>
    private async Task InsertRowAsync(DataRow row, int rowIndex)
    {
        var cols      = _dataTable!.Columns.Cast<DataColumn>().ToList();
        var colNames  = cols.Select(c => $"`{Esc(c.ColumnName)}`");
        var paramNames = cols.Select((_, i) => $"@p{i}");
        var parameters = cols.Select((c, i) =>
        {
            var val = row[c.ColumnName];
            return ($"@p{i}", val == DBNull.Value ? null : (object?)val);
        }).ToList();

        var sql = $"INSERT INTO `{Esc(_dbName)}`.`{Esc(_tableName)}` " +
                  $"({string.Join(", ", colNames)}) VALUES ({string.Join(", ", paramNames)})";

        await _dbService.ExecuteNonQueryWithParamsAsync(sql, parameters);

        _suppressEvents = true;
        try { row.AcceptChanges(); }
        finally { _suppressEvents = false; }

        _totalCount++;
        SetStatus($"新しい行を追加しました。合計: {_totalCount:N0} 行");
    }

    /// <summary>選択中の行を DELETE する。</summary>
    private async Task DeleteSelectedRowsAsync()
    {
        if (!btnDeleteRow.Enabled || _dataTable == null || _primaryKeyColumns.Count == 0) return;

        // 選択セルから行インデックスを収集（新規行・重複除外、降順でDelete時のインデックスずれを防ぐ）
        var selectedRowIndexes = grid.SelectedCells
            .Cast<DataGridViewCell>()
            .Select(c => c.RowIndex)
            .Distinct()
            .Where(i => i >= 0 && i < _dataTable.Rows.Count && !grid.Rows[i].IsNewRow)
            .OrderByDescending(i => i)
            .ToList();

        if (selectedRowIndexes.Count == 0) return;

        var confirm = MessageBox.Show(
            $"{selectedRowIndexes.Count} 行を削除しますか？\nこの操作は元に戻せません。",
            "行の削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        grid.EndEdit();
        SetStatus("削除中...");
        Cursor = Cursors.WaitCursor;

        int deletedCount = 0;
        var errors = new List<string>();

        foreach (int rowIndex in selectedRowIndexes)
        {
            if (rowIndex >= _dataTable.Rows.Count) continue;
            var row = _dataTable.Rows[rowIndex];

            try
            {
                var whereParts = _primaryKeyColumns.Select(pk => $"`{Esc(pk)}` = @{pk}").ToList();
                var parameters = _primaryKeyColumns.Select(pk =>
                {
                    var val = row[pk];
                    return ($"@{pk}", val == DBNull.Value ? null : (object?)val);
                }).ToList();

                var sql = $"DELETE FROM `{Esc(_dbName)}`.`{Esc(_tableName)}` " +
                          $"WHERE {string.Join(" AND ", whereParts)} LIMIT 1";

                int affected = await _dbService.ExecuteNonQueryWithParamsAsync(sql, parameters);
                if (affected == 0)
                    throw new Exception("対象レコードが見つかりませんでした。");

                _suppressEvents = true;
                try { row.Delete(); row.AcceptChanges(); }
                finally { _suppressEvents = false; }

                deletedCount++;
                _totalCount--;
            }
            catch (Exception ex)
            {
                errors.Add($"行 {rowIndex + 1}: {ex.Message}");
            }
        }

        Cursor = Cursors.Default;

        if (errors.Count > 0)
            MessageBox.Show($"一部の行の削除に失敗しました。\n\n{string.Join("\n", errors)}",
                "削除エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        SetStatus($"{deletedCount} 行を削除しました。合計: {_totalCount:N0} 行");
    }

    /// <summary>現在行の変更を破棄して元の値に戻す。</summary>
    private void RevertCurrentRow()
    {
        if (_dataTable == null || grid.CurrentRow == null) return;
        int rowIndex = grid.CurrentRow.Index;
        if (rowIndex < 0 || rowIndex >= _dataTable.Rows.Count) return;

        var row = _dataTable.Rows[rowIndex];
        if (row.RowState is DataRowState.Modified or DataRowState.Added)
        {
            _suppressEvents = true;
            try
            {
                grid.CancelEdit();
                row.RejectChanges();
            }
            finally
            {
                _suppressEvents = false;
            }
            SetStatus("変更を元に戻しました。");
        }
    }

    // =============================================
    // ユーティリティ
    // =============================================

    private static string Esc(string id) => id.Replace("`", "``");

    private void SetStatus(string msg) => lblStatus.Text = msg;

    private void CopyRowsAsTsv()
    {
        if (grid.SelectedRows.Count == 0) return;

        var sb = new StringBuilder();
        var headers = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText);
        sb.AppendLine(string.Join("\t", headers));

        foreach (DataGridViewRow row in grid.SelectedRows)
        {
            if (row.IsNewRow) continue;
            var cells = row.Cells.Cast<DataGridViewCell>()
                .Select(c => c.Value?.ToString() ?? "(NULL)");
            sb.AppendLine(string.Join("\t", cells));
        }

        if (sb.Length > 0) Clipboard.SetText(sb.ToString());
    }
}
