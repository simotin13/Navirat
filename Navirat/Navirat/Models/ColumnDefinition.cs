namespace Navirat.Models;

/// <summary>
/// テーブルカラム定義
/// </summary>
public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "VARCHAR";
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; } = false;
    public bool IsAutoIncrement { get; set; } = false;
    public bool IsUnsigned { get; set; } = false;
    public string? DefaultValue { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? CharacterSet { get; set; }
    public string? Collation { get; set; }

    /// <summary>
    /// SQLのカラム型定義文字列を生成する
    /// </summary>
    public string GetTypeDefinition()
    {
        var typeName = DataType.ToUpper();
        string typeDef;

        if (Length.HasValue && !IsDateTimeType(typeName) && !IsNoLengthType(typeName))
        {
            if (Precision.HasValue && Scale.HasValue)
                typeDef = $"{typeName}({Precision},{Scale})";
            else
                typeDef = $"{typeName}({Length})";
        }
        else
        {
            typeDef = typeName;
        }

        if (IsUnsigned && IsNumericType(typeName))
            typeDef += " UNSIGNED";

        return typeDef;
    }

    private static bool IsDateTimeType(string type) =>
        type is "DATE" or "DATETIME" or "TIMESTAMP" or "TIME" or "YEAR";

    private static bool IsNoLengthType(string type) =>
        type is "TEXT" or "MEDIUMTEXT" or "LONGTEXT" or "TINYTEXT"
            or "BLOB" or "MEDIUMBLOB" or "LONGBLOB" or "TINYBLOB"
            or "JSON" or "GEOMETRY";

    private static bool IsNumericType(string type) =>
        type is "INT" or "INTEGER" or "TINYINT" or "SMALLINT" or "MEDIUMINT"
            or "BIGINT" or "FLOAT" or "DOUBLE" or "DECIMAL" or "NUMERIC";
}

/// <summary>
/// よく使われるMySQLデータ型の一覧
/// </summary>
public static class MySqlDataTypes
{
    public static readonly string[] All =
    [
        // 数値型
        "INT", "TINYINT", "SMALLINT", "MEDIUMINT", "BIGINT",
        "FLOAT", "DOUBLE", "DECIMAL", "NUMERIC",
        // 文字列型
        "VARCHAR", "CHAR", "TEXT", "TINYTEXT", "MEDIUMTEXT", "LONGTEXT",
        // バイナリ型
        "BINARY", "VARBINARY", "BLOB", "TINYBLOB", "MEDIUMBLOB", "LONGBLOB",
        // 日時型
        "DATE", "DATETIME", "TIMESTAMP", "TIME", "YEAR",
        // その他
        "ENUM", "SET", "JSON", "GEOMETRY", "BOOLEAN", "BIT"
    ];
}
