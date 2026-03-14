namespace Navirat.Models;

/// <summary>
/// MySQL接続情報を保持するモデルクラス
/// </summary>
public class ConnectionInfo
{
    public string ConnectionName { get; set; } = "新しい接続";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
    public string DefaultDatabase { get; set; } = string.Empty;

    // SSH トンネル設定
    public bool UseSshTunnel { get; set; } = false;
    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = string.Empty;
    public string SshPassword { get; set; } = string.Empty;
    public bool UseSshPrivateKey { get; set; } = false;
    public string SshPrivateKeyPath { get; set; } = string.Empty;
    public string SshPassphrase { get; set; } = string.Empty;

    public override string ToString() => ConnectionName;
}
