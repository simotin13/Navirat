using Navirat.Models;
using MySqlConnector;
using System.Data;
using System.Text;

namespace Navirat.Services;

/// <summary>
/// MySQL データベース操作サービス。
/// 接続プールを使用し、各操作で独立した接続を開く。
/// </summary>
public class DatabaseService : IDisposable
{
    private string _connectionString = string.Empty;
    private bool _disposed;

    public bool IsConnected => !string.IsNullOrEmpty(_connectionString);
    public string CurrentDatabase { get; private set; } = string.Empty;

    // =============================================
    // 接続管理
    // =============================================

    public async Task ConnectAsync(ConnectionInfo info, uint? sshLocalPort = null,
        CancellationToken cancellationToken = default)
    {
        string host = sshLocalPort.HasValue ? "127.0.0.1" : info.Host;
        int port    = sshLocalPort.HasValue ? (int)sshLocalPort.Value : info.Port;

        var builder = new MySqlConnectionStringBuilder
        {
            Server                = host,
            Port                  = (uint)port,
            UserID                = info.Username,
            Password              = info.Password,
            ConnectionTimeout     = 30,
            DefaultCommandTimeout = 60,
            AllowZeroDateTime     = true,
            ConvertZeroDateTime   = true,
            CharacterSet          = "utf8mb4",
            SslMode               = MySqlSslMode.Preferred,
            // 接続プールを有効化
            Pooling               = true,
            MinimumPoolSize       = 1,
            MaximumPoolSize       = 20,
        };

        if (!string.IsNullOrEmpty(info.DefaultDatabase))
        {
            builder.Database = info.DefaultDatabase;
            CurrentDatabase  = info.DefaultDatabase;
        }

        _connectionString = builder.ConnectionString;

        // 接続確認（疎通テスト）
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
    }

    public void Disconnect()
    {
        // 接続プールをクリア
        if (!string.IsNullOrEmpty(_connectionString))
            MySqlConnection.ClearAllPools();

        _connectionString = string.Empty;
        CurrentDatabase   = string.Empty;
    }

    /// <summary>各操作で新しい接続を開いて返す。呼び出し元で await using する。</summary>
    private async Task<MySqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("データベースに接続されていません。");

        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    // =============================================
    // データベース管理
    // =============================================

    public async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand("SHOW DATABASES", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            databases.Add(reader.GetString(0));
        return databases;
    }

    public async Task CreateDatabaseAsync(string dbName, string charset = "utf8mb4",
        string collation = "utf8mb4_general_ci", CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        var sql = $"CREATE DATABASE `{Esc(dbName)}` CHARACTER SET {charset} COLLATE {collation}";
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DropDatabaseAsync(string dbName, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand($"DROP DATABASE `{Esc(dbName)}`", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UseDatabaseAsync(string dbName, CancellationToken cancellationToken = default)
    {
        // 接続文字列のデフォルト DB を書き換えて以降の接続に反映させる
        var builder      = new MySqlConnectionStringBuilder(_connectionString) { Database = dbName };
        _connectionString = builder.ConnectionString;
        CurrentDatabase  = dbName;

        // 疎通確認
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand($"USE `{Esc(dbName)}`", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // =============================================
    // テーブル管理
    // =============================================

    public async Task<List<(string Name, string Type, long Rows, string Engine)>> GetTablesAsync(
        string dbName, CancellationToken cancellationToken = default)
    {
        var tables = new List<(string, string, long, string)>();
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT TABLE_NAME, TABLE_TYPE, TABLE_ROWS, ENGINE
                             FROM INFORMATION_SCHEMA.TABLES
                             WHERE TABLE_SCHEMA = @db
                             ORDER BY TABLE_TYPE, TABLE_NAME";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", dbName);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add((reader.GetString(0),
                        reader.IsDBNull(1) ? "BASE TABLE" : reader.GetString(1),
                        reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3)));
        return tables;
    }

    public async Task<List<Models.ColumnDefinition>> GetColumnsAsync(
        string dbName, string tableName, CancellationToken cancellationToken = default)
    {
        var columns = new List<Models.ColumnDefinition>();
        await using var conn = await OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT,
                                    EXTRA, COLUMN_COMMENT, CHARACTER_SET_NAME, COLLATION_NAME,
                                    COLUMN_KEY, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                                    NUMERIC_PRECISION, NUMERIC_SCALE
                             FROM INFORMATION_SCHEMA.COLUMNS
                             WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tbl
                             ORDER BY ORDINAL_POSITION";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db",  dbName);
        cmd.Parameters.AddWithValue("@tbl", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var col = new Models.ColumnDefinition
            {
                Name            = reader.GetString(0),
                DataType        = reader.IsDBNull(9)  ? "VARCHAR" : reader.GetString(9).ToUpper(),
                IsNullable      = reader.GetString(2) == "YES",
                DefaultValue    = reader.IsDBNull(3)  ? null : reader.GetString(3),
                IsAutoIncrement = !reader.IsDBNull(4) && reader.GetString(4).Contains("auto_increment"),
                Comment         = reader.IsDBNull(5)  ? "" : reader.GetString(5),
                CharacterSet    = reader.IsDBNull(6)  ? null : reader.GetString(6),
                Collation       = reader.IsDBNull(7)  ? null : reader.GetString(7),
                IsPrimaryKey    = !reader.IsDBNull(8) && reader.GetString(8) == "PRI",
                Length          = reader.IsDBNull(10) ? null : (int?)reader.GetInt64(10),
                Precision       = reader.IsDBNull(11) ? null : (int?)reader.GetInt64(11),
                Scale           = reader.IsDBNull(12) ? null : (int?)reader.GetInt64(12),
            };
            if (!reader.IsDBNull(1))
                col.IsUnsigned = reader.GetString(1).Contains("unsigned", StringComparison.OrdinalIgnoreCase);
            columns.Add(col);
        }
        return columns;
    }

    public async Task CreateTableAsync(string dbName, string tableName,
        List<Models.ColumnDefinition> columns, string engine = "InnoDB",
        string charset = "utf8mb4", string comment = "",
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE `{Esc(dbName)}`.`{Esc(tableName)}` (");

        var primaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => $"`{Esc(c.Name)}`").ToList();
        var defs = new List<string>();

        foreach (var col in columns)
        {
            var d = new StringBuilder($"  `{Esc(col.Name)}` {col.GetTypeDefinition()}");
            if (col.CharacterSet != null) d.Append($" CHARACTER SET {col.CharacterSet}");
            if (col.Collation    != null) d.Append($" COLLATE {col.Collation}");
            d.Append(col.IsNullable ? " NULL" : " NOT NULL");
            if (col.IsAutoIncrement)              d.Append(" AUTO_INCREMENT");
            else if (col.DefaultValue != null)    d.Append($" DEFAULT '{EscVal(col.DefaultValue)}'");
            if (!string.IsNullOrEmpty(col.Comment)) d.Append($" COMMENT '{EscVal(col.Comment)}'");
            defs.Add(d.ToString());
        }

        sb.Append(string.Join(",\n", defs));
        if (primaryKeys.Count > 0)
            sb.Append($",\n  PRIMARY KEY ({string.Join(", ", primaryKeys)})");

        sb.AppendLine();
        sb.Append($") ENGINE={engine} DEFAULT CHARSET={charset}");
        if (!string.IsNullOrEmpty(comment)) sb.Append($" COMMENT='{EscVal(comment)}'");

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sb.ToString(), conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DropTableAsync(string dbName, string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand($"DROP TABLE `{Esc(dbName)}`.`{Esc(tableName)}`", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TruncateTableAsync(string dbName, string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand($"TRUNCATE TABLE `{Esc(dbName)}`.`{Esc(tableName)}`", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 指定DBの全テーブルを外部キー制約を一時無効にして DROP します。
    /// 成功数・失敗数とエラー一覧を返します。
    /// </summary>
    public async Task<(int Dropped, int Failed, List<string> Errors)> DropAllTablesAsync(
        string dbName,
        CancellationToken cancellationToken = default)
    {
        var tables = await GetTablesAsync(dbName, cancellationToken);
        int dropped = 0, failed = 0;
        var errors  = new List<string>();

        await using var conn = await OpenConnectionAsync(cancellationToken);

        // 外部キー制約を一時無効化
        await using (var fkOff = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0", conn))
            await fkOff.ExecuteNonQueryAsync(cancellationToken);

        foreach (var (name, _, _, _) in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var cmd = new MySqlCommand(
                    $"DROP TABLE `{Esc(dbName)}`.`{Esc(name)}`", conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                dropped++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"`{name}`: {ex.Message}");
            }
        }

        // 外部キー制約を再有効化
        await using (var fkOn = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 1", conn))
            await fkOn.ExecuteNonQueryAsync(CancellationToken.None);

        return (dropped, failed, errors);
    }

    public async Task AddColumnAsync(string dbName, string tableName,
        Models.ColumnDefinition column, string? afterColumn = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder(
            $"ALTER TABLE `{Esc(dbName)}`.`{Esc(tableName)}` ADD COLUMN `{Esc(column.Name)}` {column.GetTypeDefinition()}");
        if (column.CharacterSet != null) sb.Append($" CHARACTER SET {column.CharacterSet}");
        sb.Append(column.IsNullable ? " NULL" : " NOT NULL");
        if (column.IsAutoIncrement)             sb.Append(" AUTO_INCREMENT");
        else if (column.DefaultValue != null)   sb.Append($" DEFAULT '{EscVal(column.DefaultValue)}'");
        if (!string.IsNullOrEmpty(column.Comment)) sb.Append($" COMMENT '{EscVal(column.Comment)}'");
        if (afterColumn != null) sb.Append($" AFTER `{Esc(afterColumn)}`");
        if (column.IsPrimaryKey) sb.Append($", ADD PRIMARY KEY (`{Esc(column.Name)}`)");

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sb.ToString(), conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ModifyColumnAsync(string dbName, string tableName,
        string oldColumnName, Models.ColumnDefinition column,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder(
            $"ALTER TABLE `{Esc(dbName)}`.`{Esc(tableName)}` CHANGE COLUMN `{Esc(oldColumnName)}` `{Esc(column.Name)}` {column.GetTypeDefinition()}");
        if (column.CharacterSet != null) sb.Append($" CHARACTER SET {column.CharacterSet}");
        sb.Append(column.IsNullable ? " NULL" : " NOT NULL");
        if (column.IsAutoIncrement)            sb.Append(" AUTO_INCREMENT");
        else if (column.DefaultValue != null)  sb.Append($" DEFAULT '{EscVal(column.DefaultValue)}'");
        if (!string.IsNullOrEmpty(column.Comment)) sb.Append($" COMMENT '{EscVal(column.Comment)}'");

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sb.ToString(), conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DropColumnAsync(string dbName, string tableName, string columnName,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(
            $"ALTER TABLE `{Esc(dbName)}`.`{Esc(tableName)}` DROP COLUMN `{Esc(columnName)}`", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // =============================================
    // データ取得
    // =============================================

    public async Task<(DataTable Data, long TotalCount)> GetTableDataAsync(
        string dbName, string tableName, int page, int pageSize = 1000,
        string? orderBy = null, CancellationToken cancellationToken = default)
    {
        int offset = (page - 1) * pageSize;

        await using var conn = await OpenConnectionAsync(cancellationToken);

        long totalCount = 0;
        await using (var countCmd = new MySqlCommand(
            $"SELECT COUNT(*) FROM `{Esc(dbName)}`.`{Esc(tableName)}`", conn))
        {
            totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));
        }

        var orderClause = string.IsNullOrEmpty(orderBy) ? "" : $" ORDER BY {orderBy}";
        var sql = $"SELECT * FROM `{Esc(dbName)}`.`{Esc(tableName)}`{orderClause} LIMIT {pageSize} OFFSET {offset}";

        using var adapter = new MySqlDataAdapter(sql, conn);
        var dt = new DataTable();
        await Task.Run(() => adapter.Fill(dt), cancellationToken);
        return (dt, totalCount);
    }

    public async Task<DataTable> GetAllTableDataAsync(string dbName, string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        var sql = $"SELECT * FROM `{Esc(dbName)}`.`{Esc(tableName)}`";
        using var adapter = new MySqlDataAdapter(sql, conn);
        var dt = new DataTable();
        await Task.Run(() => adapter.Fill(dt), cancellationToken);
        return dt;
    }

    // =============================================
    // クエリ実行
    // =============================================

    public async Task<DataTable> ExecuteQueryAsync(string sql,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sql, conn) { CommandTimeout = 300 };
        using var adapter = new MySqlDataAdapter(cmd);
        var dt = new DataTable();
        await Task.Run(() => adapter.Fill(dt), cancellationToken);
        return dt;
    }

    public async Task<int> ExecuteNonQueryAsync(string sql,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sql, conn) { CommandTimeout = 300 };
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>パラメータ付きの非クエリ SQL を実行する（INSERT / UPDATE / DELETE 用）。</summary>
    public async Task<int> ExecuteNonQueryWithParamsAsync(
        string sql,
        IEnumerable<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd  = new MySqlCommand(sql, conn) { CommandTimeout = 300 };
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? (object)DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<(string Sql, DataTable? Result, int AffectedRows, string? Error, TimeSpan Elapsed)>>
        ExecuteMultipleAsync(string fullSql, CancellationToken cancellationToken = default)
    {
        var results    = new List<(string, DataTable?, int, string?, TimeSpan)>();
        var statements = SplitSqlStatements(fullSql);

        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var trimmed = stmt.Trim();

                // コメント行（-- ...）と空行をスキップして最初の実SQL行でステートメント種別を判定
                var firstSqlLine = trimmed.Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0
                                        && !l.StartsWith("--")
                                        && !l.StartsWith("/*"))
                    ?? trimmed;
                var upper = firstSqlLine.ToUpperInvariant();

                if (upper.StartsWith("SELECT") || upper.StartsWith("SHOW") ||
                    upper.StartsWith("DESCRIBE") || upper.StartsWith("EXPLAIN") ||
                    upper.StartsWith("DESC"))
                {
                    var dt = await ExecuteQueryAsync(trimmed, cancellationToken);
                    sw.Stop();
                    results.Add((trimmed, dt, 0, null, sw.Elapsed));
                }
                else
                {
                    var affected = await ExecuteNonQueryAsync(trimmed, cancellationToken);
                    sw.Stop();
                    results.Add((trimmed, null, affected, null, sw.Elapsed));
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add((stmt.Trim(), null, 0, ex.Message, sw.Elapsed));
            }
        }

        return results;
    }

    // =============================================
    // エクスポート用
    // =============================================

    public async Task<string> GetCreateTableSqlAsync(string dbName, string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var conn   = await OpenConnectionAsync(cancellationToken);
        await using var cmd    = new MySqlCommand(
            $"SHOW CREATE TABLE `{Esc(dbName)}`.`{Esc(tableName)}`", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? reader.GetString(1) : string.Empty;
    }

    public async Task BulkInsertAsync(string dbName, string tableName, DataTable data,
        CancellationToken cancellationToken = default)
    {
        if (data.Rows.Count == 0) return;

        await using var conn = await OpenConnectionAsync(cancellationToken);
        var columns    = data.Columns.Cast<DataColumn>().Select(c => $"`{Esc(c.ColumnName)}`").ToList();
        var paramNames = data.Columns.Cast<DataColumn>().Select((_, i) => $"@p{i}").ToList();
        var sql        = $"INSERT INTO `{Esc(dbName)}`.`{Esc(tableName)}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)})";

        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var cmd = new MySqlCommand(sql, conn, transaction);
            for (int i = 0; i < data.Columns.Count; i++)
                cmd.Parameters.Add(new MySqlParameter($"@p{i}", DBNull.Value));

            foreach (DataRow row in data.Rows)
            {
                for (int i = 0; i < data.Columns.Count; i++)
                    cmd.Parameters[$"@p{i}"].Value = row[i] ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // =============================================
    // SQL スクリプト実行（インポート用）
    // =============================================

    /// <summary>
    /// SQL スクリプトを1接続で順次実行する。インポート処理に使用。
    /// DML (INSERT/UPDATE/DELETE/REPLACE) は BatchSize 件ずつ1回の COM_QUERY で送信し、
    /// CommitEvery 件ごとにまとめてコミットすることで高速化します。
    /// バッチ送信に失敗した場合は1件ずつ個別実行にフォールバックします。
    /// </summary>
    public async Task<(int Success, int Failed, List<string> Errors)> ExecuteScriptAsync(
        string sql,
        bool continueOnError = true,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // コメントのみのステートメントを除外して事前にリスト化
        var statements = SplitSqlStatements(sql)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !IsCommentOnly(s))
            .ToList();

        int success = 0, failed = 0;
        var errors = new List<string>();

        // DML を何件まとめて1回の COM_QUERY で送るか（マルチステートメント送信）
        const int BatchSize = 50;
        // DML を何件ごとにコミットするか
        const int CommitEvery = 10_000;

        var dmlBatch = new List<string>(BatchSize);
        int uncommittedDml = 0;

        // MySqlConnector はマルチステートメントをデフォルトでサポートするため通常の接続で OK
        await using var conn = await OpenConnectionAsync(cancellationToken);

        // autocommit を無効化
        await using (var acCmd = new MySqlCommand("SET autocommit = 0", conn))
            await acCmd.ExecuteNonQueryAsync(cancellationToken);

        // 未コミット DML をコミットするローカル関数
        async Task CommitPendingAsync()
        {
            if (uncommittedDml == 0) return;
            await using var commitCmd = new MySqlCommand("COMMIT", conn);
            await commitCmd.ExecuteNonQueryAsync(CancellationToken.None);
            uncommittedDml = 0;
        }

        // DML バッチを1回の COM_QUERY で送信するローカル関数
        // 失敗した場合は1件ずつ個別実行にフォールバックする
        async Task FlushDmlBatchAsync()
        {
            if (dmlBatch.Count == 0) return;

            var snapshot = dmlBatch.ToList();
            dmlBatch.Clear();

            // バッチが1件なら SAVEPOINT 不要で直接実行
            if (snapshot.Count == 1)
            {
                try
                {
                    await using var singleCmd = new MySqlCommand(snapshot[0], conn) { CommandTimeout = 300 };
                    await singleCmd.ExecuteNonQueryAsync(cancellationToken);
                    success++;
                    uncommittedDml++;
                    if (uncommittedDml >= CommitEvery) await CommitPendingAsync();
                }
                catch (Exception ex)
                {
                    failed++;
                    var preview = snapshot[0].Length > 120 ? snapshot[0][..120] + "..." : snapshot[0];
                    errors.Add($"{ex.Message}  →  {preview}");
                    if (!continueOnError)
                    {
                        try { await using var rb = new MySqlCommand("ROLLBACK", conn); await rb.ExecuteNonQueryAsync(CancellationToken.None); } catch { }
                        throw new Exception($"エラーが発生しました:\n{ex.Message}");
                    }
                }
                return;
            }

            // SAVEPOINT を設定してバッチ送信
            const string SavepointName = "_nb";
            try
            {
                await using (var spCmd = new MySqlCommand($"SAVEPOINT {SavepointName}", conn))
                    await spCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // SAVEPOINT が失敗した場合は個別実行にフォールバック
                foreach (var s in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await using var fb = new MySqlCommand(s, conn) { CommandTimeout = 300 };
                        await fb.ExecuteNonQueryAsync(cancellationToken);
                        success++; uncommittedDml++;
                        if (uncommittedDml >= CommitEvery) await CommitPendingAsync();
                    }
                    catch (Exception ex2)
                    {
                        failed++;
                        var p = s.Length > 120 ? s[..120] + "..." : s;
                        errors.Add($"{ex2.Message}  →  {p}");
                        if (!continueOnError)
                        {
                            try { await using var rb = new MySqlCommand("ROLLBACK", conn); await rb.ExecuteNonQueryAsync(CancellationToken.None); } catch { }
                            throw new Exception($"エラーが発生しました:\n{ex2.Message}");
                        }
                    }
                }
                return;
            }

            // マルチステートメントを1回の COM_QUERY で送信
            var batchSql = string.Join(";\n", snapshot);
            bool batchOk = false;
            try
            {
                await using var batchCmd = new MySqlCommand(batchSql, conn) { CommandTimeout = 300 };
                await batchCmd.ExecuteNonQueryAsync(cancellationToken);
                batchOk = true;
            }
            catch
            {
                // バッチ失敗 → SAVEPOINT までロールバック
                try
                {
                    await using var rbSp = new MySqlCommand($"ROLLBACK TO SAVEPOINT {SavepointName}", conn);
                    await rbSp.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch { }
            }

            // SAVEPOINT を解放
            try
            {
                await using var relCmd = new MySqlCommand($"RELEASE SAVEPOINT {SavepointName}", conn);
                await relCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { }

            if (batchOk)
            {
                success += snapshot.Count;
                uncommittedDml += snapshot.Count;
                if (uncommittedDml >= CommitEvery) await CommitPendingAsync();
                return;
            }

            // バッチ失敗時は1件ずつ個別実行にフォールバック
            foreach (var s in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var fallbackCmd = new MySqlCommand(s, conn) { CommandTimeout = 300 };
                    await fallbackCmd.ExecuteNonQueryAsync(cancellationToken);
                    success++;
                    uncommittedDml++;
                    if (uncommittedDml >= CommitEvery) await CommitPendingAsync();
                }
                catch (Exception ex)
                {
                    failed++;
                    var preview = s.Length > 120 ? s[..120] + "..." : s;
                    errors.Add($"{ex.Message}  →  {preview}");
                    if (!continueOnError)
                    {
                        try { await using var rb = new MySqlCommand("ROLLBACK", conn); await rb.ExecuteNonQueryAsync(CancellationToken.None); } catch { }
                        throw new Exception($"エラーが発生しました:\n{ex.Message}");
                    }
                }
            }
        }

        try
        {
            for (int i = 0; i < statements.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 進捗は 20 件ごとに通知（体感速度とUIオーバーヘッドのバランス）
                if (i % 20 == 0 || i == statements.Count - 1)
                    progress?.Report((i + 1, statements.Count));

                var stmt = statements[i];
                bool isDml = IsDmlStatement(stmt);

                if (isDml)
                {
                    // DML はバッチに積む
                    dmlBatch.Add(stmt);
                    if (dmlBatch.Count >= BatchSize)
                        await FlushDmlBatchAsync();
                }
                else
                {
                    // 非DML（DDL / USE / SET 等）の前にバッチをフラッシュしてコミット
                    await FlushDmlBatchAsync();
                    if (uncommittedDml > 0) await CommitPendingAsync();

                    try
                    {
                        await using var cmd = new MySqlCommand(stmt, conn) { CommandTimeout = 300 };
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        var preview = stmt.Length > 120 ? stmt[..120] + "..." : stmt;
                        errors.Add($"[{i + 1}] {ex.Message}  →  {preview}");

                        if (!continueOnError)
                        {
                            try
                            {
                                await using var rollCmd = new MySqlCommand("ROLLBACK", conn);
                                await rollCmd.ExecuteNonQueryAsync(CancellationToken.None);
                            }
                            catch { }
                            throw new Exception($"ステートメント {i + 1} でエラーが発生しました:\n{ex.Message}");
                        }
                    }
                }
            }

            // 残りのバッチをフラッシュしてコミット
            await FlushDmlBatchAsync();
            await CommitPendingAsync();
        }
        catch (OperationCanceledException)
        {
            // キャンセル時はロールバック
            try
            {
                await using var rollCmd = new MySqlCommand("ROLLBACK", conn);
                await rollCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
        finally
        {
            // autocommit を元の状態に戻す
            try
            {
                await using var restoreCmd = new MySqlCommand("SET autocommit = 1", conn);
                await restoreCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { }
        }

        return (success, failed, errors);
    }

    /// <summary>コメント行と空行だけで構成されるステートメントか判定する。</summary>
    private static bool IsCommentOnly(string stmt)
    {
        // 高速パス: 先頭が英字・数字ならSQL文なので即 false（Split を回避）
        var span = stmt.AsSpan().TrimStart();
        if (span.IsEmpty) return true;
        char first = span[0];
        if (char.IsAsciiLetter(first) || char.IsAsciiDigit(first) || first == '`' || first == '(')
            return false;
        // 先頭がコメント記号でなければ false
        if (!span.StartsWith("--", StringComparison.Ordinal) &&
            !span.StartsWith("/*", StringComparison.Ordinal))
            return false;

        // 低速パス: 全行をスキャン（コメントで始まるステートメントのみ到達）
        foreach (var line in stmt.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0
                && !t.StartsWith("--", StringComparison.Ordinal)
                && !t.StartsWith("/*", StringComparison.Ordinal)
                && !t.StartsWith("*/", StringComparison.Ordinal)
                && !t.StartsWith("* ",  StringComparison.Ordinal)
                && t != "*")
                return false;
        }
        return true;
    }

    /// <summary>DML ステートメント (INSERT/UPDATE/DELETE/REPLACE) か判定する。</summary>
    private static bool IsDmlStatement(string stmt)
    {
        // 高速パス: Span で先頭をチェック（Split/LINQ を回避）
        var span = stmt.AsSpan().TrimStart();
        if (span.IsEmpty) return false;

        // 先頭がコメントでなければ直接判定できる（99%のケース）
        if (!span.StartsWith("--", StringComparison.Ordinal) &&
            !span.StartsWith("/*", StringComparison.Ordinal))
        {
            return span.StartsWith("INSERT",  StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("UPDATE",  StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("DELETE",  StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase);
        }

        // 低速パス: コメントで始まるステートメントの場合に最初の非コメント行を探す
        foreach (var line in stmt.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.Length == 0 ||
                t.StartsWith("--", StringComparison.Ordinal) ||
                t.StartsWith("/*", StringComparison.Ordinal)) continue;

            return t.StartsWith("INSERT",  StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("UPDATE",  StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("DELETE",  StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // =============================================
    // ユーティリティ
    // =============================================

    private static string Esc(string id)    => id.Replace("`", "``");
    private static string EscVal(string v)  => v.Replace("\\", "\\\\").Replace("'", "\\'");

    private static List<string> SplitSqlStatements(string sql)
    {
        var statements   = new List<string>();
        var current      = new StringBuilder();
        bool inSingle    = false;
        bool inDouble    = false;
        bool inBacktick  = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c    = sql[i];
            char prev = i > 0 ? sql[i - 1] : '\0';

            if      (c == '\'' && !inDouble && !inBacktick && prev != '\\') inSingle   = !inSingle;
            else if (c == '"'  && !inSingle && !inBacktick && prev != '\\') inDouble   = !inDouble;
            else if (c == '`'  && !inSingle && !inDouble)                   inBacktick = !inBacktick;

            if (c == ';' && !inSingle && !inDouble && !inBacktick)
            {
                var s = current.ToString().Trim();
                if (!string.IsNullOrEmpty(s)) statements.Add(s);
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrEmpty(last)) statements.Add(last);
        return statements;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) Disconnect();
        _disposed = true;
    }
}
