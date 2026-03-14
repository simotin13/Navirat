# MySQL Manager

Navicat for MySQL と同様のMySQLデータベース管理ツールです。
C# / WinForms / .NET 8 で実装されています。

---

## 必要な環境

| 項目 | 要件 |
|------|------|
| OS | Windows 10 / 11 |
| .NET | .NET 8 SDK |
| IDE | Visual Studio 2022 (17.8 以降推奨) |
| MySQL | MySQL 5.7 以降 |

---

## セットアップ

### 1. ソリューションを開く

```
MySQLManager.sln
```
を Visual Studio でダブルクリックして開きます。

### 2. NuGet パッケージの復元

Visual Studio が自動的に復元しますが、手動で実行する場合:

```
dotnet restore
```

### 3. ビルド・実行

`F5` でデバッグ実行、または `Ctrl+F5` でリリース実行します。

---

## 使い方

### 接続を追加する

1. ツールバーの「**接続追加**」ボタンをクリック
2. 接続名・ホスト・ポート・ユーザー名・パスワードを入力
3. SSH トンネルを使う場合は「**SSH トンネル**」タブを設定
4. 「**接続テスト**」で疎通確認後、「**OK**」

### データベースを管理する

- ツリービューの接続ノードを**ダブルクリック**して接続
- 接続ノードを**右クリック** → 「データベースを作成」
- DBノードを**右クリック** → 「データベースを削除」

### テーブルを管理する

| 操作 | 手順 |
|------|------|
| テーブル一覧 | DB ノードを展開 |
| テーブルを作成 | DB ノード右クリック → 「テーブルを作成」 |
| 構造を変更 | テーブルノード右クリック → 「テーブル構造を編集」 |
| テーブルを削除 | テーブルノード右クリック → 「テーブルを削除」 |
| カラム追加/削除 | テーブル構造エディタのツールバー |

### クエリを実行する

1. ツールバーの「**クエリ**」またはテーブル右クリック → 「クエリを開く」
2. SQL エディタに SQL を入力
3. **F5** または `▶ 実行` ボタンで実行
4. 結果は下部タブに表示（SELECT: データグリッド、その他: 影響行数）

> **ヒント**: テキストを選択した状態で実行すると、選択部分のみ実行されます。

### データを表示する（ページング）

- テーブルノードを**ダブルクリック**または右クリック → 「データを表示」
- 1ページ **1,000 件**ずつ表示
- `◀ / ▶` ボタンまたはページ番号を直接入力して移動

### インポート / エクスポート

テーブルノードを右クリック → 「データのインポート」または「データのエクスポート」

| フォーマット | 説明 |
|---|---|
| CSV | カンマ区切り |
| TSV | タブ区切り |
| SQL | CREATE TABLE + INSERT 文 |

---

## プロジェクト構成

```
MySQLManager/
├── MySQLManager.sln                  # ソリューションファイル
└── MySQLManager/
    ├── MySQLManager.csproj           # プロジェクトファイル
    ├── Program.cs                    # エントリーポイント
    ├── Models/
    │   ├── ConnectionInfo.cs         # 接続情報モデル
    │   └── ColumnDefinition.cs       # カラム定義モデル
    ├── Services/
    │   ├── SshTunnelService.cs       # SSH トンネル管理
    │   └── DatabaseService.cs        # MySQL 操作サービス
    └── Forms/
        ├── MainForm.cs               # メインウィンドウ
        ├── ConnectionForm.cs         # 接続設定ダイアログ
        ├── TableDesignerControl.cs   # テーブル設計コントロール
        ├── QueryEditorControl.cs     # クエリエディタ
        ├── DataBrowserControl.cs     # データブラウザ（ページング）
        └── ImportExportForm.cs       # インポート/エクスポート
```

---

## 使用ライブラリ

| ライブラリ | バージョン | 用途 |
|---|---|---|
| [MySqlConnector](https://mysqlconnector.net/) | 2.3.7 | MySQL 接続 |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2024.2.0 | SSH トンネル |
| [CsvHelper](https://joshclose.github.io/CsvHelper/) | 33.0.1 | CSV 読み書き |

---

## 接続情報の保存場所

接続設定は以下のパスに JSON 形式で保存されます:

```
%APPDATA%\MySQLManager\connections.json
```

> **注意**: パスワードはプレーンテキストで保存されます。
> セキュリティが重要な環境では、Windows DPAPI などによる暗号化の追加実装を推奨します。

---

## ライセンス

MIT License
