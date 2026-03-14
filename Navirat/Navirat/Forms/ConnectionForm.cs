using Navirat.Models;
using Navirat.Services;

namespace Navirat.Forms;

/// <summary>
/// MySQL接続設定ダイアログ
/// </summary>
public class ConnectionForm : Form
{
    private readonly ConnectionInfo _connectionInfo;
    private bool _isEditMode;

    // General タブのコントロール
    private TextBox txtConnectionName = null!;
    private TextBox txtHost = null!;
    private NumericUpDown nudPort = null!;
    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private CheckBox chkShowPassword = null!;
    private TextBox txtDefaultDatabase = null!;

    // SSH タブのコントロール
    private CheckBox chkUseSsh = null!;
    private TextBox txtSshHost = null!;
    private NumericUpDown nudSshPort = null!;
    private TextBox txtSshUsername = null!;
    private RadioButton rdoSshPassword = null!;
    private RadioButton rdoSshPrivateKey = null!;
    private TextBox txtSshPassword = null!;
    private CheckBox chkShowSshPassword = null!;
    private TextBox txtSshPrivateKeyPath = null!;
    private Button btnBrowseKey = null!;
    private TextBox txtSshPassphrase = null!;
    private CheckBox chkShowPassphrase = null!;
    private Panel sshSettingsPanel = null!;

    // ボタン
    private Button btnTest = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    public ConnectionInfo Result => _connectionInfo;

    public ConnectionForm(ConnectionInfo? existingInfo = null)
    {
        _isEditMode = existingInfo != null;
        _connectionInfo = existingInfo != null
            ? CloneConnectionInfo(existingInfo)
            : new ConnectionInfo();

        InitializeComponent();
        LoadValues();
    }

    private static ConnectionInfo CloneConnectionInfo(ConnectionInfo src) => new()
    {
        ConnectionName = src.ConnectionName,
        Host = src.Host,
        Port = src.Port,
        Username = src.Username,
        Password = src.Password,
        DefaultDatabase = src.DefaultDatabase,
        UseSshTunnel = src.UseSshTunnel,
        SshHost = src.SshHost,
        SshPort = src.SshPort,
        SshUsername = src.SshUsername,
        SshPassword = src.SshPassword,
        UseSshPrivateKey = src.UseSshPrivateKey,
        SshPrivateKeyPath = src.SshPrivateKeyPath,
        SshPassphrase = src.SshPassphrase
    };

    private void InitializeComponent()
    {
        Text = _isEditMode ? "接続の編集" : "新しい接続";
        Size = new Size(500, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Meiryo UI", 9f);

        var tabControl = new TabControl
        {
            Dock = DockStyle.Top,
            Height = 420
        };

        // ===== General タブ =====
        var tabGeneral = new TabPage("一般");
        BuildGeneralTab(tabGeneral);
        tabControl.TabPages.Add(tabGeneral);

        // ===== SSH タブ =====
        var tabSsh = new TabPage("SSH トンネル");
        BuildSshTab(tabSsh);
        tabControl.TabPages.Add(tabSsh);

        Controls.Add(tabControl);

        // ===== ボタン =====
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50
        };

        btnTest = new Button
        {
            Text = "接続テスト",
            Left = 12,
            Top = 12,
            Width = 110,
            Height = 30
        };
        btnTest.Click += BtnTest_Click;

        btnOk = new Button
        {
            Text = "OK",
            Left = 290,
            Top = 12,
            Width = 90,
            Height = 30,
            DialogResult = DialogResult.None
        };
        btnOk.Click += BtnOk_Click;

        btnCancel = new Button
        {
            Text = "キャンセル",
            Left = 390,
            Top = 12,
            Width = 90,
            Height = 30,
            DialogResult = DialogResult.Cancel
        };

        buttonPanel.Controls.AddRange([btnTest, btnOk, btnCancel]);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void BuildGeneralTab(TabPage tab)
    {
        int y = 16;
        int labelWidth = 130;
        int inputLeft = 150;
        int inputWidth = 300;
        int rowHeight = 34;

        void AddRow(string label, Control control)
        {
            var lbl = new Label { Text = label, Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
            control.Left = inputLeft;
            control.Top = y;
            tab.Controls.Add(lbl);
            tab.Controls.Add(control);
            y += rowHeight;
        }

        // 接続名
        txtConnectionName = new TextBox { Width = inputWidth };
        AddRow("接続名:", txtConnectionName);

        // ホスト
        txtHost = new TextBox { Width = 220 };
        var portLabel = new Label { Text = "ポート:", Left = inputLeft + 230, Top = y + 3, Width = 50 };
        nudPort = new NumericUpDown { Left = inputLeft + 280, Top = y, Width = 70, Minimum = 1, Maximum = 65535 };
        tab.Controls.Add(portLabel);
        tab.Controls.Add(nudPort);

        var lblHost = new Label { Text = "ホスト:", Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
        txtHost.Left = inputLeft;
        txtHost.Top = y;
        tab.Controls.Add(lblHost);
        tab.Controls.Add(txtHost);
        y += rowHeight;

        // ユーザー名
        txtUsername = new TextBox { Width = inputWidth };
        AddRow("ユーザー名:", txtUsername);

        // パスワード
        txtPassword = new TextBox { Width = inputWidth - 28, PasswordChar = '●' };
        chkShowPassword = new CheckBox { Text = "表示", Left = inputLeft + inputWidth - 25, Top = y + 2, Width = 50, AutoSize = true };
        chkShowPassword.CheckedChanged += (s, e) => txtPassword.PasswordChar = chkShowPassword.Checked ? '\0' : '●';
        var lblPwd = new Label { Text = "パスワード:", Left = 12, Top = y + 3, Width = labelWidth, AutoSize = false };
        txtPassword.Left = inputLeft;
        txtPassword.Top = y;
        tab.Controls.Add(lblPwd);
        tab.Controls.Add(txtPassword);
        tab.Controls.Add(chkShowPassword);
        y += rowHeight;

        // デフォルトデータベース
        txtDefaultDatabase = new TextBox { Width = inputWidth };
        var lblDb = new Label
        {
            Text = "デフォルト DB:",
            Left = 12,
            Top = y + 3,
            Width = labelWidth,
            AutoSize = false
        };
        txtDefaultDatabase.Left = inputLeft;
        txtDefaultDatabase.Top = y;
        tab.Controls.Add(lblDb);
        tab.Controls.Add(txtDefaultDatabase);
        y += rowHeight;

        // 注記
        var noteLabel = new Label
        {
            Text = "※ デフォルト DB は省略可能です。",
            Left = 12,
            Top = y,
            Width = 440,
            ForeColor = Color.Gray,
            AutoSize = false
        };
        tab.Controls.Add(noteLabel);
    }

    private void BuildSshTab(TabPage tab)
    {
        int y = 12;

        chkUseSsh = new CheckBox
        {
            Text = "SSH トンネルを使用する",
            Left = 12,
            Top = y,
            Width = 250,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };
        chkUseSsh.CheckedChanged += ChkUseSsh_CheckedChanged;
        tab.Controls.Add(chkUseSsh);
        y += 34;

        // SSH設定パネル
        sshSettingsPanel = new Panel
        {
            Left = 0,
            Top = y,
            Width = 470,
            Height = 320,
            Enabled = false
        };

        int sy = 0;
        int labelWidth = 130;
        int inputLeft = 150;
        int inputWidth = 290;
        int rowHeight = 34;

        void AddRow(string label, Control control, int w = -1)
        {
            var lbl = new Label { Text = label, Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
            control.Left = inputLeft;
            control.Top = sy;
            control.Width = w > 0 ? w : inputWidth;
            sshSettingsPanel.Controls.Add(lbl);
            sshSettingsPanel.Controls.Add(control);
            sy += rowHeight;
        }

        // SSH ホスト & ポート
        txtSshHost = new TextBox();
        var sshPortLabel = new Label { Text = "ポート:", Left = inputLeft + 195, Top = sy + 3, Width = 50 };
        nudSshPort = new NumericUpDown { Left = inputLeft + 245, Top = sy, Width = 70, Minimum = 1, Maximum = 65535 };
        sshSettingsPanel.Controls.Add(sshPortLabel);
        sshSettingsPanel.Controls.Add(nudSshPort);

        var lblSshHost = new Label { Text = "SSH ホスト:", Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
        txtSshHost.Left = inputLeft;
        txtSshHost.Top = sy;
        txtSshHost.Width = 190;
        sshSettingsPanel.Controls.Add(lblSshHost);
        sshSettingsPanel.Controls.Add(txtSshHost);
        sy += rowHeight;

        // SSH ユーザー名
        txtSshUsername = new TextBox();
        AddRow("SSH ユーザー名:", txtSshUsername);

        // 認証方式
        var lblAuth = new Label { Text = "認証方式:", Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
        rdoSshPassword = new RadioButton { Text = "パスワード", Left = inputLeft, Top = sy, Width = 120, Checked = true };
        rdoSshPrivateKey = new RadioButton { Text = "秘密鍵", Left = inputLeft + 130, Top = sy, Width = 100 };
        rdoSshPassword.CheckedChanged += (s, e) => UpdateSshAuthMode();
        sshSettingsPanel.Controls.AddRange([lblAuth, rdoSshPassword, rdoSshPrivateKey]);
        sy += rowHeight;

        // SSH パスワード
        txtSshPassword = new TextBox { PasswordChar = '●' };
        chkShowSshPassword = new CheckBox { Text = "表示", Left = inputLeft + inputWidth - 25, Top = sy + 2, Width = 50, AutoSize = true };
        chkShowSshPassword.CheckedChanged += (s, e) => txtSshPassword.PasswordChar = chkShowSshPassword.Checked ? '\0' : '●';
        var lblSshPwd = new Label { Text = "SSH パスワード:", Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
        txtSshPassword.Left = inputLeft;
        txtSshPassword.Top = sy;
        txtSshPassword.Width = inputWidth - 28;
        sshSettingsPanel.Controls.AddRange([lblSshPwd, txtSshPassword, chkShowSshPassword]);
        sy += rowHeight;

        // 秘密鍵パス
        txtSshPrivateKeyPath = new TextBox { Width = inputWidth - 36 };
        btnBrowseKey = new Button { Text = "...", Width = 30, Left = inputLeft + inputWidth - 32, Top = sy, Height = 23 };
        btnBrowseKey.Click += BtnBrowseKey_Click;
        var lblKey = new Label { Text = "秘密鍵ファイル:", Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
        txtSshPrivateKeyPath.Left = inputLeft;
        txtSshPrivateKeyPath.Top = sy;
        sshSettingsPanel.Controls.AddRange([lblKey, txtSshPrivateKeyPath, btnBrowseKey]);
        sy += rowHeight;

        // パスフレーズ
        txtSshPassphrase = new TextBox { PasswordChar = '●' };
        chkShowPassphrase = new CheckBox { Text = "表示", Left = inputLeft + inputWidth - 25, Top = sy + 2, Width = 50, AutoSize = true };
        chkShowPassphrase.CheckedChanged += (s, e) => txtSshPassphrase.PasswordChar = chkShowPassphrase.Checked ? '\0' : '●';
        var lblPass = new Label { Text = "パスフレーズ:", Left = 12, Top = sy + 3, Width = labelWidth, AutoSize = false };
        txtSshPassphrase.Left = inputLeft;
        txtSshPassphrase.Top = sy;
        txtSshPassphrase.Width = inputWidth - 28;
        sshSettingsPanel.Controls.AddRange([lblPass, txtSshPassphrase, chkShowPassphrase]);

        tab.Controls.Add(sshSettingsPanel);
        UpdateSshAuthMode();
    }

    private void LoadValues()
    {
        txtConnectionName.Text = _connectionInfo.ConnectionName;
        txtHost.Text = _connectionInfo.Host;
        nudPort.Value = _connectionInfo.Port;
        txtUsername.Text = _connectionInfo.Username;
        txtPassword.Text = _connectionInfo.Password;
        txtDefaultDatabase.Text = _connectionInfo.DefaultDatabase;

        chkUseSsh.Checked = _connectionInfo.UseSshTunnel;
        txtSshHost.Text = _connectionInfo.SshHost;
        nudSshPort.Value = _connectionInfo.SshPort;
        txtSshUsername.Text = _connectionInfo.SshUsername;
        rdoSshPrivateKey.Checked = _connectionInfo.UseSshPrivateKey;
        rdoSshPassword.Checked = !_connectionInfo.UseSshPrivateKey;
        txtSshPassword.Text = _connectionInfo.SshPassword;
        txtSshPrivateKeyPath.Text = _connectionInfo.SshPrivateKeyPath;
        txtSshPassphrase.Text = _connectionInfo.SshPassphrase;
    }

    private void SaveValues()
    {
        _connectionInfo.ConnectionName = txtConnectionName.Text.Trim();
        _connectionInfo.Host = txtHost.Text.Trim();
        _connectionInfo.Port = (int)nudPort.Value;
        _connectionInfo.Username = txtUsername.Text.Trim();
        _connectionInfo.Password = txtPassword.Text;
        _connectionInfo.DefaultDatabase = txtDefaultDatabase.Text.Trim();

        _connectionInfo.UseSshTunnel = chkUseSsh.Checked;
        _connectionInfo.SshHost = txtSshHost.Text.Trim();
        _connectionInfo.SshPort = (int)nudSshPort.Value;
        _connectionInfo.SshUsername = txtSshUsername.Text.Trim();
        _connectionInfo.UseSshPrivateKey = rdoSshPrivateKey.Checked;
        _connectionInfo.SshPassword = txtSshPassword.Text;
        _connectionInfo.SshPrivateKeyPath = txtSshPrivateKeyPath.Text.Trim();
        _connectionInfo.SshPassphrase = txtSshPassphrase.Text;
    }

    private void ChkUseSsh_CheckedChanged(object? sender, EventArgs e)
    {
        sshSettingsPanel.Enabled = chkUseSsh.Checked;
    }

    private void UpdateSshAuthMode()
    {
        bool usePwd = rdoSshPassword.Checked;
        txtSshPassword.Enabled = usePwd;
        chkShowSshPassword.Enabled = usePwd;
        txtSshPrivateKeyPath.Enabled = !usePwd;
        btnBrowseKey.Enabled = !usePwd;
        txtSshPassphrase.Enabled = !usePwd;
        chkShowPassphrase.Enabled = !usePwd;
    }

    private void BtnBrowseKey_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "SSH 秘密鍵ファイルを選択",
            Filter = "秘密鍵ファイル (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|すべてのファイル (*.*)|*.*"
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            txtSshPrivateKeyPath.Text = dlg.FileName;
    }

    private async void BtnTest_Click(object? sender, EventArgs e)
    {
        SaveValues();

        if (string.IsNullOrEmpty(_connectionInfo.Host))
        {
            MessageBox.Show("ホスト名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnTest.Enabled = false;
        btnTest.Text = "接続中...";

        SshTunnelService? sshService = null;
        DatabaseService? dbService = null;

        try
        {
            uint? sshPort = null;

            if (_connectionInfo.UseSshTunnel)
            {
                sshService = new SshTunnelService();
                sshPort = await sshService.ConnectAsync(_connectionInfo);
            }

            dbService = new DatabaseService();
            await dbService.ConnectAsync(_connectionInfo, sshPort);

            MessageBox.Show("接続に成功しました！", "接続テスト", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"接続に失敗しました。\n\n{ex.Message}", "接続エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            dbService?.Disconnect();
            dbService?.Dispose();
            sshService?.Disconnect();
            sshService?.Dispose();
            btnTest.Enabled = true;
            btnTest.Text = "接続テスト";
        }
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        SaveValues();

        if (string.IsNullOrWhiteSpace(_connectionInfo.ConnectionName))
        {
            MessageBox.Show("接続名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_connectionInfo.Host))
        {
            MessageBox.Show("ホスト名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
