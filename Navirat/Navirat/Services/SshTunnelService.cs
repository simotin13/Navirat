using Navirat.Models;

// Renci.SshNet の型を個別にエイリアスで導入（ConnectionInfo の名前衝突を回避）
using SshClient            = Renci.SshNet.SshClient;
using ForwardedPortLocal   = Renci.SshNet.ForwardedPortLocal;
using AuthenticationMethod = Renci.SshNet.AuthenticationMethod;
using PasswordAuth         = Renci.SshNet.PasswordAuthenticationMethod;
using PrivateKeyAuth       = Renci.SshNet.PrivateKeyAuthenticationMethod;
using PrivateKeyFile       = Renci.SshNet.PrivateKeyFile;
using SshConnectionInfo    = Renci.SshNet.ConnectionInfo;

namespace Navirat.Services;

/// <summary>
/// SSH トンネルを管理するサービス
/// </summary>
public class SshTunnelService : IDisposable
{
    private SshClient? _sshClient;
    private ForwardedPortLocal? _forwardedPort;
    private bool _disposed;

    public bool IsConnected => _sshClient?.IsConnected == true;
    public uint LocalPort { get; private set; }

    /// <summary>
    /// SSH トンネルを確立し、ローカルポートを返す
    /// </summary>
    public async Task<uint> ConnectAsync(ConnectionInfo info, CancellationToken cancellationToken = default)
    {
        if (!info.UseSshTunnel)
            throw new InvalidOperationException("SSH トンネルが有効化されていません。");

        await Task.Run(() =>
        {
            AuthenticationMethod authMethod;

            if (info.UseSshPrivateKey && !string.IsNullOrEmpty(info.SshPrivateKeyPath))
            {
                // 秘密鍵認証
                PrivateKeyFile keyFile = string.IsNullOrEmpty(info.SshPassphrase)
                    ? new PrivateKeyFile(info.SshPrivateKeyPath)
                    : new PrivateKeyFile(info.SshPrivateKeyPath, info.SshPassphrase);

                authMethod = new PrivateKeyAuth(info.SshUsername, keyFile);
            }
            else
            {
                // パスワード認証
                authMethod = new PasswordAuth(info.SshUsername, info.SshPassword);
            }

            var sshConnInfo = new SshConnectionInfo(
                info.SshHost,
                info.SshPort,
                info.SshUsername,
                authMethod)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _sshClient = new SshClient(sshConnInfo);
            _sshClient.Connect();

            if (!_sshClient.IsConnected)
                throw new Exception("SSH サーバーへの接続に失敗しました。");

            // ローカルのランダムポートから MySQL サーバーへポートフォワード
            _forwardedPort = new ForwardedPortLocal(
                "127.0.0.1",
                0u,              // 0 = ランダムポートを自動割り当て
                info.Host,
                (uint)info.Port);

            _sshClient.AddForwardedPort(_forwardedPort);
            _forwardedPort.Start();

            LocalPort = _forwardedPort.BoundPort;
        }, cancellationToken);

        return LocalPort;
    }

    /// <summary>
    /// SSH トンネルを切断する
    /// </summary>
    public void Disconnect()
    {
        _forwardedPort?.Stop();
        _sshClient?.Disconnect();
        LocalPort = 0;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Disconnect();
            _forwardedPort?.Dispose();
            _sshClient?.Dispose();
        }

        _disposed = true;
    }
}
