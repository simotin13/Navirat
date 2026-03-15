using Navirat.Models;
using Navirat.Services;
using System.Data;

namespace Navirat.Forms;

/// <summary>
/// メインウィンドウ
/// </summary>
public class MainForm : Form
{
    // 接続管理
    private readonly List<ConnectionInfo> _connections = [];
    private readonly Dictionary<string, (DatabaseService DbService, SshTunnelService? SshService)> _activeConnections = [];
    private readonly HashSet<string> _connectingNodes = [];  // 接続処理中の重複呼び出し防止

    // UI コントロール
    private TreeView treeView = null!;
    private TabControl tabMain = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripStatusLabel statusDbLabel = null!;
    private MenuStrip menuStrip = null!;
    private ToolStrip toolStrip = null!;
    private SplitContainer splitContainer = null!;
    private Panel rightPanel = null!;

    // ツリーノードアイコン用のインデックス
    private const int ICON_SERVER = 0;
    private const int ICON_SERVER_CONNECTED = 1;
    private const int ICON_DATABASE = 2;
    private const int ICON_TABLE = 3;
    private const int ICON_VIEW = 4;

    public MainForm()
    {
        InitializeComponent();
        LoadConnectionsFromSettings();
        SetStatus("準備完了");
    }

    private void InitializeComponent()
    {
        Text = "Navirat";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Meiryo UI", 9f);
        Icon = SystemIcons.Application;

        // ImageList（ツリーアイコン用）
        var imageList = new ImageList { ImageSize = new Size(16, 16) };
        imageList.Images.Add(SystemIcons.Application.ToBitmap());        // 0: server (未接続)
        imageList.Images.Add(SystemIcons.Shield.ToBitmap());             // 1: server (接続済み)
        imageList.Images.Add(SystemIcons.Asterisk.ToBitmap());           // 2: database
        imageList.Images.Add(SystemIcons.WinLogo.ToBitmap());            // 3: table
        imageList.Images.Add(SystemIcons.Information.ToBitmap());        // 4: view

        // ===== コントロールの追加順序について =====
        // WinForms のドッキングは Controls コレクションの「逆順」で処理される。
        // Fill コントロールは最後に処理されて残り領域を占有するため、
        // Controls.Add の順番は以下のようにする必要がある:
        //   1. Fill (SplitContainer)  ← 最初に追加 → ドッキング処理は最後
        //   2. Top (MenuStrip/ToolStrip) ← 後から追加 → ドッキング処理が先
        //   3. Bottom (StatusStrip)   ← 最後に追加 → ドッキング処理が最初

        // ツリービュー（左パネル）
        treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            ImageList = imageList,
            ShowNodeToolTips = true,
            Font = new Font("Meiryo UI", 9f),
            FullRowSelect = true,
            HideSelection = false
        };
        treeView.NodeMouseDoubleClick += TreeView_NodeMouseDoubleClick;
        treeView.NodeMouseClick += TreeView_NodeMouseClick;
        treeView.AfterExpand += TreeView_AfterExpand;
        treeView.AfterSelect += TreeView_AfterSelect;

        // 右パネル
        rightPanel = new Panel { Dock = DockStyle.Fill };
        tabMain = new TabControl { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(tabMain);

        // スプリットコンテナ
        // ※ Panel1MinSize / Panel2MinSize は Shown イベントで SplitterDistance 設定後に適用する
        splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None
        };
        splitContainer.Panel1.Controls.Add(treeView);
        splitContainer.Panel2.Controls.Add(rightPanel);

        // [1] Fill を最初に追加（ドッキング処理は最後になる）
        Controls.Add(splitContainer);

        // [2] Top を後から追加（ドッキング処理が Fill より先になる）
        BuildMenuStrip();
        BuildToolStrip();
        Controls.Add(toolStrip);
        Controls.Add(menuStrip);

        // [3] Bottom を最後に追加（ドッキング処理が最初になる）
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("準備完了") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusDbLabel = new ToolStripStatusLabel("") { Alignment = ToolStripItemAlignment.Right };
        statusStrip.Items.AddRange([statusLabel, statusDbLabel]);
        Controls.Add(statusStrip);

        MainMenuStrip = menuStrip;
        FormClosing += MainForm_FormClosing;
        Shown += (s, e) =>
        {
            // SplitterDistance を先に設定してから MinSize を適用する
            // （MinSize を先に設定すると、デフォルトの SplitterDistance が範囲外になり例外が発生する）
            int desired = 260;
            int safeMax = splitContainer.Width - splitContainer.SplitterWidth - 1;
            splitContainer.SplitterDistance = Math.Max(1, Math.Min(desired, safeMax));

            // SplitterDistance 確定後に MinSize を設定
            splitContainer.Panel1MinSize = 150;
            splitContainer.Panel2MinSize = 200;
        };
    }

    // =============================================
    // メニュー・ツールバー
    // =============================================

    private void BuildMenuStrip()
    {
        menuStrip = new MenuStrip();

        // ファイルメニュー
        var fileMenu = new ToolStripMenuItem("ファイル(&F)");
        fileMenu.DropDownItems.Add("新しい接続(&N)", null, (s, e) => NewConnection());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("終了(&X)", null, (s, e) => Close());
        menuStrip.Items.Add(fileMenu);

        // クエリメニュー
        var queryMenu = new ToolStripMenuItem("クエリ(&Q)");
        queryMenu.DropDownItems.Add("新しいクエリ(&N)\tCtrl+N", null, (s, e) => OpenQueryEditor(null, null));
        menuStrip.Items.Add(queryMenu);

        // ツールメニュー
        var toolMenu = new ToolStripMenuItem("ツール(&T)");
        toolMenu.DropDownItems.Add("接続管理(&C)", null, (s, e) => ManageConnections());
        menuStrip.Items.Add(toolMenu);

        // ヘルプメニュー
        var helpMenu = new ToolStripMenuItem("ヘルプ(&H)");
        helpMenu.DropDownItems.Add("バージョン情報(&A)", null, (s, e) =>
            MessageBox.Show("Navirat v1.0\n.NET 8 / WinForms", "バージョン情報",
                MessageBoxButtons.OK, MessageBoxIcon.Information));
        menuStrip.Items.Add(helpMenu);
    }

    private void BuildToolStrip()
    {
        toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        var btnNewConn = new ToolStripButton("接続追加") { ToolTipText = "新しい接続を追加" };
        btnNewConn.Click += (s, e) => NewConnection();

        var btnQuery = new ToolStripButton("クエリ") { ToolTipText = "新しいクエリエディタを開く" };
        btnQuery.Click += (s, e) => OpenQueryEditor(null, null);

        toolStrip.Items.AddRange([btnNewConn, new ToolStripSeparator(), btnQuery]);
    }

    // =============================================
    // 接続管理
    // =============================================

    private void NewConnection()
    {
        using var form = new ConnectionForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        _connections.Add(form.Result);
        SaveConnections();
        AddConnectionNode(form.Result);
        SetStatus($"接続 '{form.Result.ConnectionName}' を追加しました。");
    }

    private void ManageConnections()
    {
        MessageBox.Show("接続はツリービューを右クリックして管理できます。", "接続管理",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AddConnectionNode(ConnectionInfo info)
    {
        var node = new TreeNode(info.ConnectionName)
        {
            Tag = new NodeTag(NodeType.Connection, info.ConnectionName, null, null),
            ImageIndex = ICON_SERVER,
            SelectedImageIndex = ICON_SERVER,
            ToolTipText = $"{info.Username}@{info.Host}:{info.Port}"
        };
        // 未接続のため子ノードなし（展開矢印を表示しない）
        treeView.Nodes.Add(node);
    }

    private async Task ConnectAsync(TreeNode serverNode, ConnectionInfo info)
    {
        var key = info.ConnectionName;

        // 既に接続済み、または接続処理中なら何もしない（二重呼び出し防止）
        if (_activeConnections.ContainsKey(key) || _connectingNodes.Contains(key))
        {
            serverNode.ImageIndex = ICON_SERVER_CONNECTED;
            serverNode.SelectedImageIndex = ICON_SERVER_CONNECTED;
            return;
        }

        _connectingNodes.Add(key);
        SetStatus($"{info.ConnectionName} に接続中...");
        Cursor = Cursors.WaitCursor;

        try
        {
            SshTunnelService? sshService = null;
            uint? sshPort = null;

            if (info.UseSshTunnel)
            {
                sshService = new SshTunnelService();
                sshPort = await sshService.ConnectAsync(info);
            }

            var dbService = new DatabaseService();
            await dbService.ConnectAsync(info, sshPort);

            _activeConnections[key] = (dbService, sshService);

            serverNode.ImageIndex = ICON_SERVER_CONNECTED;
            serverNode.SelectedImageIndex = ICON_SERVER_CONNECTED;
            serverNode.ToolTipText = $"接続済み: {info.Username}@{info.Host}:{info.Port}";

            await LoadDatabasesAsync(serverNode, dbService);
            serverNode.Expand();

            SetStatus($"{info.ConnectionName} に接続しました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"接続に失敗しました。\n\n{ex.Message}", "接続エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("接続に失敗しました。");
        }
        finally
        {
            _connectingNodes.Remove(key);
            Cursor = Cursors.Default;
        }
    }

    private void DisconnectServer(string connectionName)
    {
        if (!_activeConnections.TryGetValue(connectionName, out var conn)) return;

        conn.DbService.Disconnect();
        conn.DbService.Dispose();
        conn.SshService?.Disconnect();
        conn.SshService?.Dispose();
        _activeConnections.Remove(connectionName);

        SetStatus($"{connectionName} から切断しました。");
    }

    // =============================================
    // ツリービュー操作
    // =============================================

    private async Task LoadDatabasesAsync(TreeNode serverNode, DatabaseService dbService)
    {
        serverNode.Nodes.Clear();

        try
        {
            var databases = await dbService.GetDatabasesAsync();
            foreach (var db in databases)
            {
                var dbNode = new TreeNode(db)
                {
                    Tag = new NodeTag(NodeType.Database, GetConnectionName(serverNode), db, null),
                    ImageIndex = ICON_DATABASE,
                    SelectedImageIndex = ICON_DATABASE
                };
                dbNode.Nodes.Add(new TreeNode("読み込み中...") { Tag = null });
                serverNode.Nodes.Add(dbNode);
            }
        }
        catch (Exception ex)
        {
            var errNode = new TreeNode($"エラー: {ex.Message}") { ForeColor = Color.Red };
            serverNode.Nodes.Add(errNode);
            SetStatus($"データベース一覧の取得に失敗: {ex.Message}");
        }
    }

    private async Task LoadTablesAsync(TreeNode dbNode, DatabaseService dbService)
    {
        var tag = (NodeTag)dbNode.Tag!;
        dbNode.Nodes.Clear();

        try
        {
            var tables = await dbService.GetTablesAsync(tag.Database!);
            foreach (var (name, type, rows, engine) in tables)
            {
                int iconIndex = type == "VIEW" ? ICON_VIEW : ICON_TABLE;
                var tableNode = new TreeNode($"{name}")
                {
                    Tag = new NodeTag(NodeType.Table, tag.ConnectionName, tag.Database, name),
                    ImageIndex = iconIndex,
                    SelectedImageIndex = iconIndex,
                    ToolTipText = $"型: {type} | 行数(概算): {rows:N0} | エンジン: {engine}"
                };
                dbNode.Nodes.Add(tableNode);
            }

            if (tables.Count == 0)
                dbNode.Nodes.Add(new TreeNode("(テーブルなし)") { ForeColor = Color.Gray });

            SetStatus($"'{tag.Database}' のテーブルを {tables.Count} 件読み込みました。");
        }
        catch (Exception ex)
        {
            var errNode = new TreeNode($"エラー: {ex.Message}") { ForeColor = Color.Red };
            dbNode.Nodes.Add(errNode);
            SetStatus($"テーブル一覧の取得に失敗: {ex.Message}");
            MessageBox.Show($"テーブル一覧の取得に失敗しました。\n\nデータベース: {tag.Database}\n\nエラー:\n{ex.Message}",
                "テーブル読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TreeView_AfterExpand(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is not NodeTag tag) return;

        // ダミーノード（"読み込み中..."）があれば実際のデータを読み込む
        bool hasDummy = e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null;
        if (!hasDummy) return;

        // 未接続の場合は何もしない（接続はダブルクリックまたは右クリック→「接続」で行う）
        if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;

        try
        {
            if (tag.Type == NodeType.Connection)
                await LoadDatabasesAsync(e.Node, conn.DbService);
            else if (tag.Type == NodeType.Database)
                await LoadTablesAsync(e.Node, conn.DbService);

            // Nodes.Clear() で一時的に子ゼロになりノードが折りたたまれるため、
            // ロード完了後に再展開して子ノードを表示する
            e.Node.Expand();
        }
        catch (Exception ex)
        {
            SetStatus($"ロードエラー: {ex.Message}");
        }
    }

    private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is not NodeTag tag) return;
        if (tag.Database == null) return;  // Connection ノードは対象外

        // 開いているすべてのクエリエディタの接続/DB を同期する
        foreach (TabPage tab in tabMain.TabPages)
        {
            var editor = tab.Controls.OfType<QueryEditorControl>().FirstOrDefault();
            editor?.SetConnectionAndDatabase(tag.ConnectionName, tag.Database);
        }
    }

    private async void TreeView_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node?.Tag is not NodeTag tag) return;

        var connInfo = GetConnectionInfo(tag.ConnectionName);
        if (connInfo == null) return;

        switch (tag.Type)
        {
            case NodeType.Connection:
                if (!_activeConnections.ContainsKey(tag.ConnectionName))
                    await ConnectAsync(e.Node, connInfo);
                break;

            case NodeType.Database:
                if (_activeConnections.TryGetValue(tag.ConnectionName, out var conn))
                {
                    await conn.DbService.UseDatabaseAsync(tag.Database!);
                    SetStatus($"データベース '{tag.Database}' を選択しました。");
                    statusDbLabel.Text = $"DB: {tag.Database}";
                }
                break;

            case NodeType.Table:
                OpenTableDataViewer(tag);
                break;
        }
    }

    private void TreeView_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.Node?.Tag is not NodeTag tag) return;

        treeView.SelectedNode = e.Node;
        var menu = BuildContextMenu(tag, e.Node);
        menu.Show(treeView, e.Location);
    }

    private ContextMenuStrip BuildContextMenu(NodeTag tag, TreeNode node)
    {
        var menu = new ContextMenuStrip();

        switch (tag.Type)
        {
            case NodeType.Connection:
            {
                var connInfo = GetConnectionInfo(tag.ConnectionName);
                bool isConnected = _activeConnections.ContainsKey(tag.ConnectionName);

                if (!isConnected)
                {
                    var connectItem = new ToolStripMenuItem("接続");
                    connectItem.Click += async (s, e) =>
                    {
                        if (connInfo != null) await ConnectAsync(node, connInfo);
                    };
                    menu.Items.Add(connectItem);
                }
                else
                {
                    var disconnectItem = new ToolStripMenuItem("切断");
                    disconnectItem.Click += (s, e) =>
                    {
                        DisconnectServer(tag.ConnectionName);
                        node.ImageIndex = ICON_SERVER;
                        node.SelectedImageIndex = ICON_SERVER;
                        // 子ノードをすべて削除（展開矢印も消える）
                        node.Nodes.Clear();
                    };
                    menu.Items.Add(disconnectItem);

                    menu.Items.Add(new ToolStripSeparator());

                    var newDbItem = new ToolStripMenuItem("データベースを作成...");
                    newDbItem.Click += (s, e) => CreateDatabase(tag.ConnectionName);
                    menu.Items.Add(newDbItem);
                }

                menu.Items.Add(new ToolStripSeparator());

                var editItem = new ToolStripMenuItem("接続を編集...");
                editItem.Click += (s, e) => EditConnection(tag.ConnectionName, node);
                menu.Items.Add(editItem);

                var deleteItem = new ToolStripMenuItem("接続を削除");
                deleteItem.Click += (s, e) => DeleteConnection(tag.ConnectionName, node);
                menu.Items.Add(deleteItem);
                break;
            }

            case NodeType.Database:
            {
                bool isConnected = _activeConnections.ContainsKey(tag.ConnectionName);
                if (isConnected)
                {
                    var newTableItem = new ToolStripMenuItem("テーブルを作成...");
                    newTableItem.Click += (s, e) => CreateTable(tag.ConnectionName, tag.Database!);
                    menu.Items.Add(newTableItem);

                    var newQueryItem = new ToolStripMenuItem("クエリを開く");
                    newQueryItem.Click += (s, e) => OpenQueryEditor(tag.ConnectionName, tag.Database);
                    menu.Items.Add(newQueryItem);

                    menu.Items.Add(new ToolStripSeparator());

                    // ── SQL エクスポート / インポート ──
                    var exportSqlItem = new ToolStripMenuItem("SQL エクスポート...");
                    exportSqlItem.Click += (s, e) => OpenSqlImportExport(tag, SqlIoMode.Export);
                    menu.Items.Add(exportSqlItem);

                    var importSqlItem = new ToolStripMenuItem("SQL インポート...");
                    importSqlItem.Click += (s, e) => OpenSqlImportExport(tag, SqlIoMode.Import);
                    menu.Items.Add(importSqlItem);

                    menu.Items.Add(new ToolStripSeparator());

                    var dropDbItem = new ToolStripMenuItem("データベースを削除...");
                    dropDbItem.ForeColor = Color.DarkRed;
                    dropDbItem.Click += (s, e) => DropDatabase(tag.ConnectionName, tag.Database!, node);
                    menu.Items.Add(dropDbItem);

                }
                break;
            }

            case NodeType.Table:
            {
                var openDataItem = new ToolStripMenuItem("データを表示");
                openDataItem.Click += (s, e) => OpenTableDataViewer(tag);
                menu.Items.Add(openDataItem);

                var openStructureItem = new ToolStripMenuItem("テーブル構造を編集...");
                openStructureItem.Click += (s, e) => OpenTableDesigner(tag, false);
                menu.Items.Add(openStructureItem);

                menu.Items.Add(new ToolStripSeparator());

                var importItem = new ToolStripMenuItem("データのインポート...");
                importItem.Click += (s, e) => OpenImportExport(tag, ImportExportMode.Import);
                menu.Items.Add(importItem);

                var exportItem = new ToolStripMenuItem("データのエクスポート...");
                exportItem.Click += (s, e) => OpenImportExport(tag, ImportExportMode.Export);
                menu.Items.Add(exportItem);

                menu.Items.Add(new ToolStripSeparator());

                var truncateItem = new ToolStripMenuItem("テーブルをトランケート...");
                truncateItem.Click += (s, e) => TruncateTable(tag);
                menu.Items.Add(truncateItem);

                var dropItem = new ToolStripMenuItem("テーブルを削除...");
                dropItem.ForeColor = Color.DarkRed;
                dropItem.Click += async (s, e) =>
                {
                    var result = MessageBox.Show(
                        $"テーブル '{tag.Table}' を削除しますか？\nこの操作は元に戻せません。",
                        "テーブルの削除",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result != DialogResult.Yes) return;

                    try
                    {
                        if (_activeConnections.TryGetValue(tag.ConnectionName, out var c))
                        {
                            await c.DbService.DropTableAsync(tag.Database!, tag.Table!);
                            node.Remove();
                            SetStatus($"テーブル '{tag.Table}' を削除しました。");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"削除に失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                menu.Items.Add(dropItem);
                break;
            }
        }

        return menu;
    }

    // =============================================
    // データベース操作
    // =============================================

    private async void CreateDatabase(string connectionName)
    {
        var name = ShowInputDialog("データベースを作成", "新しいデータベース名を入力してください:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            if (!_activeConnections.TryGetValue(connectionName, out var conn)) return;
            await conn.DbService.CreateDatabaseAsync(name);

            // ツリーを更新
            var serverNode = treeView.Nodes.Cast<TreeNode>()
                .FirstOrDefault(n => n.Tag is NodeTag t && t.ConnectionName == connectionName);
            if (serverNode != null)
                await LoadDatabasesAsync(serverNode, conn.DbService);

            SetStatus($"データベース '{name}' を作成しました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"作成に失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void DropDatabase(string connectionName, string dbName, TreeNode node)
    {
        var result = MessageBox.Show(
            $"データベース '{dbName}' を削除しますか？\n全テーブルと全データが失われます。この操作は元に戻せません。",
            "データベースの削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            if (!_activeConnections.TryGetValue(connectionName, out var conn)) return;
            await conn.DbService.DropDatabaseAsync(dbName);
            node.Remove();
            SetStatus($"データベース '{dbName}' を削除しました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除に失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =============================================
    // テーブル操作
    // =============================================

    private void CreateTable(string connectionName, string dbName)
    {
        if (!_activeConnections.TryGetValue(connectionName, out var conn)) return;

        var tag = new NodeTag(NodeType.Table, connectionName, dbName, null);
        OpenTableDesigner(tag, true);
    }

    private void OpenTableDesigner(NodeTag tag, bool isNew)
    {
        if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;

        var title = isNew ? "新しいテーブル" : $"{tag.Database}.{tag.Table}";
        var existingTab = FindTab(title);
        if (existingTab != null)
        {
            tabMain.SelectedTab = existingTab;
            return;
        }

        var designer = new TableDesignerControl(conn.DbService, tag.Database!, tag.Table, isNew)
        {
            Dock = DockStyle.Fill
        };

        designer.TableSaved += async (tableName) =>
        {
            // ツリーを更新
            var dbNode = FindDatabaseNode(tag.ConnectionName, tag.Database!);
            if (dbNode != null)
                await LoadTablesAsync(dbNode, conn.DbService);
        };

        var tab = new TabPage(title)
        {
            Tag = title
        };
        tab.Controls.Add(designer);

        AddClosableTab(tab);
        tabMain.SelectedTab = tab;
    }

    private void OpenTableDataViewer(NodeTag tag)
    {
        if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;

        var title = $"データ: {tag.Database}.{tag.Table}";
        var existingTab = FindTab(title);
        if (existingTab != null)
        {
            tabMain.SelectedTab = existingTab;
            return;
        }

        var viewer = new DataBrowserControl(conn.DbService, tag.Database!, tag.Table!)
        {
            Dock = DockStyle.Fill
        };

        var tab = new TabPage(title) { Tag = title };
        tab.Controls.Add(viewer);
        AddClosableTab(tab);
        tabMain.SelectedTab = tab;

        _ = viewer.LoadDataAsync();
    }

    private async void TruncateTable(NodeTag tag)
    {
        var result = MessageBox.Show(
            $"テーブル '{tag.Table}' の全データを削除しますか？\nテーブル構造は保持されます。",
            "テーブルのトランケート",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;
            await conn.DbService.TruncateTableAsync(tag.Database!, tag.Table!);
            SetStatus($"テーブル '{tag.Table}' をトランケートしました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"トランケートに失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =============================================
    // クエリエディタ
    // =============================================

    private void OpenQueryEditor(string? connectionName, string? dbName)
    {
        string title = $"クエリ {tabMain.TabCount + 1}";

        // 接続/DBが未指定の場合はツリービューで現在選択中のノードから取得
        if (connectionName == null && treeView.SelectedNode?.Tag is NodeTag selTag
            && _activeConnections.ContainsKey(selTag.ConnectionName))
        {
            connectionName = selTag.ConnectionName;
            dbName ??= selTag.Database;
        }

        // _activeConnections の参照をそのまま渡す（後から追加された接続も参照可能）
        var editor = new QueryEditorControl(_activeConnections, connectionName, dbName)
        {
            Dock = DockStyle.Fill
        };

        var tab = new TabPage(title) { Tag = title };
        tab.Controls.Add(editor);
        AddClosableTab(tab);
        tabMain.SelectedTab = tab;
    }

    // =============================================
    // インポート / エクスポート
    // =============================================

    private void OpenImportExport(NodeTag tag, ImportExportMode mode)
    {
        if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;

        using var form = new ImportExportForm(conn.DbService, tag.Database!, tag.Table!, mode);
        form.ShowDialog(this);
    }

    private void OpenSqlImportExport(NodeTag tag, SqlIoMode mode)
    {
        if (!_activeConnections.TryGetValue(tag.ConnectionName, out var conn)) return;

        using var form = new SqlImportExportForm(conn.DbService, tag.Database!, mode);
        form.ShowDialog(this);
    }

    // =============================================
    // タブ管理
    // =============================================

    private void AddClosableTab(TabPage tab)
    {
        tabMain.TabPages.Add(tab);
        tabMain.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabMain.DrawItem -= TabMain_DrawItem;
        tabMain.DrawItem += TabMain_DrawItem;
        tabMain.MouseClick -= TabMain_MouseClick;
        tabMain.MouseClick += TabMain_MouseClick;
    }

    private void TabMain_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var tab = tabMain.TabPages[e.Index];
        var text = tab.Text;
        var bounds = tabMain.GetTabRect(e.Index);

        e.Graphics.FillRectangle(
            e.State == DrawItemState.Selected ? SystemBrushes.Window : SystemBrushes.Control,
            bounds);

        // タブタイトル（閉じるボタン分を除いた幅に）
        var textBounds = new Rectangle(bounds.Left + 4, bounds.Top + 3, bounds.Width - 22, bounds.Height - 4);
        TextRenderer.DrawText(e.Graphics, text, tabMain.Font, textBounds, SystemColors.ControlText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 閉じるボタン [x]
        var closeBounds = new Rectangle(bounds.Right - 18, bounds.Top + 3, 16, 16);
        e.Graphics.DrawString("×", new Font("Arial", 7.5f), Brushes.DimGray, closeBounds);
    }

    private void TabMain_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        for (int i = 0; i < tabMain.TabPages.Count; i++)
        {
            var rect = tabMain.GetTabRect(i);
            var closeBounds = new Rectangle(rect.Right - 18, rect.Top + 3, 16, 16);
            if (closeBounds.Contains(e.Location))
            {
                tabMain.TabPages.RemoveAt(i);
                return;
            }
        }
    }

    private TabPage? FindTab(string title) =>
        tabMain.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Tag?.ToString() == title);

    // =============================================
    // 接続情報管理
    // =============================================

    private void EditConnection(string connectionName, TreeNode node)
    {
        var info = GetConnectionInfo(connectionName);
        if (info == null) return;

        using var form = new ConnectionForm(info);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        // 更新
        var idx = _connections.IndexOf(info);
        if (idx >= 0) _connections[idx] = form.Result;
        SaveConnections();

        node.Text = form.Result.ConnectionName;
        if (node.Tag is NodeTag tag)
            node.Tag = new NodeTag(tag.Type, form.Result.ConnectionName, tag.Database, tag.Table);
    }

    private void DeleteConnection(string connectionName, TreeNode node)
    {
        var result = MessageBox.Show(
            $"接続 '{connectionName}' を削除しますか？",
            "接続の削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        DisconnectServer(connectionName);
        var info = _connections.FirstOrDefault(c => c.ConnectionName == connectionName);
        if (info != null) _connections.Remove(info);
        SaveConnections();
        node.Remove();
    }

    private ConnectionInfo? GetConnectionInfo(string connectionName) =>
        _connections.FirstOrDefault(c => c.ConnectionName == connectionName);

    private static string GetConnectionName(TreeNode node)
    {
        var current = node;
        while (current != null)
        {
            if (current.Tag is NodeTag t && t.Type == NodeType.Connection)
                return t.ConnectionName;
            current = current.Parent;
        }
        return string.Empty;
    }

    private TreeNode? FindDatabaseNode(string connectionName, string dbName)
    {
        foreach (TreeNode serverNode in treeView.Nodes)
        {
            if (serverNode.Tag is NodeTag t && t.ConnectionName == connectionName)
            {
                foreach (TreeNode dbNode in serverNode.Nodes)
                {
                    if (dbNode.Tag is NodeTag dt && dt.Database == dbName)
                        return dbNode;
                }
            }
        }
        return null;
    }

    // =============================================
    // 設定の保存/読み込み（ユーザーのアプリデータに保存）
    // =============================================

    private static string GetSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Navirat");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "connections.json");
    }

    // =============================================
    // 接続情報の暗号化 DTO（JSON 保存用）
    // =============================================

    /// <summary>
    /// JSON に書き出す際のデータ転送オブジェクト。
    /// パスワード項目は DPAPI で暗号化した文字列を保持します。
    /// </summary>
    private sealed class ConnectionInfoDto
    {
        public string ConnectionName    { get; set; } = "新しい接続";
        public string Host              { get; set; } = "localhost";
        public int    Port              { get; set; } = 3306;
        public string Username          { get; set; } = "root";
        public string PasswordEncrypted { get; set; } = string.Empty;  // 暗号化済み
        public string DefaultDatabase   { get; set; } = string.Empty;

        // SSH トンネル設定
        public bool   UseSshTunnel          { get; set; } = false;
        public string SshHost               { get; set; } = string.Empty;
        public int    SshPort               { get; set; } = 22;
        public string SshUsername           { get; set; } = string.Empty;
        public string SshPasswordEncrypted  { get; set; } = string.Empty;  // 暗号化済み
        public bool   UseSshPrivateKey      { get; set; } = false;
        public string SshPrivateKeyPath     { get; set; } = string.Empty;
        public string SshPassphraseEncrypted { get; set; } = string.Empty; // 暗号化済み
    }

    private static ConnectionInfoDto ToDto(ConnectionInfo src) => new()
    {
        ConnectionName        = src.ConnectionName,
        Host                  = src.Host,
        Port                  = src.Port,
        Username              = src.Username,
        PasswordEncrypted     = CredentialProtector.Protect(src.Password),
        DefaultDatabase       = src.DefaultDatabase,
        UseSshTunnel          = src.UseSshTunnel,
        SshHost               = src.SshHost,
        SshPort               = src.SshPort,
        SshUsername           = src.SshUsername,
        SshPasswordEncrypted  = CredentialProtector.Protect(src.SshPassword),
        UseSshPrivateKey      = src.UseSshPrivateKey,
        SshPrivateKeyPath     = src.SshPrivateKeyPath,
        SshPassphraseEncrypted = CredentialProtector.Protect(src.SshPassphrase),
    };

    private static ConnectionInfo FromDto(ConnectionInfoDto dto) => new()
    {
        ConnectionName  = dto.ConnectionName,
        Host            = dto.Host,
        Port            = dto.Port,
        Username        = dto.Username,
        Password        = CredentialProtector.Unprotect(dto.PasswordEncrypted),
        DefaultDatabase = dto.DefaultDatabase,
        UseSshTunnel    = dto.UseSshTunnel,
        SshHost         = dto.SshHost,
        SshPort         = dto.SshPort,
        SshUsername     = dto.SshUsername,
        SshPassword     = CredentialProtector.Unprotect(dto.SshPasswordEncrypted),
        UseSshPrivateKey  = dto.UseSshPrivateKey,
        SshPrivateKeyPath = dto.SshPrivateKeyPath,
        SshPassphrase   = CredentialProtector.Unprotect(dto.SshPassphraseEncrypted),
    };

    private void SaveConnections()
    {
        try
        {
            var dtos = _connections.Select(ToDto).ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(dtos,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSettingsFilePath(), json);
        }
        catch { /* 設定保存の失敗は無視 */ }
    }

    private void LoadConnectionsFromSettings()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<ConnectionInfoDto>>(json);
            if (dtos == null) return;

            var loaded = dtos.Select(FromDto).ToList();
            _connections.AddRange(loaded);
            foreach (var info in _connections)
                AddConnectionNode(info);
        }
        catch { /* 設定読み込みの失敗は無視 */ }
    }

    // =============================================
    // ユーティリティ
    // =============================================

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
    }

    private static string ShowInputDialog(string title, string prompt, string defaultValue)
    {
        using var form = new Form
        {
            Text = title,
            Size = new Size(420, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("Meiryo UI", 9f)
        };

        var lbl = new Label { Text = prompt, Left = 12, Top = 16, Width = 380, AutoSize = false };
        var txt = new TextBox { Left = 12, Top = 38, Width = 380, Text = defaultValue };
        var btnOk = new Button { Text = "OK", Left = 220, Top = 70, Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "キャンセル", Left = 312, Top = 70, Width = 80, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange([lbl, txt, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : string.Empty;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        foreach (var key in _activeConnections.Keys.ToList())
            DisconnectServer(key);
    }
}

// =============================================
// ノード種別・タグクラス
// =============================================

public enum NodeType { Connection, Database, Table }

public record NodeTag(NodeType Type, string ConnectionName, string? Database, string? Table);
