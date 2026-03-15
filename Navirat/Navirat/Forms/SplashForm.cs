namespace Navirat.Forms;

/// <summary>
/// アプリ起動時に表示するスプラッシュスクリーン
/// </summary>
public class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private Image? _splashImage;
    private float _alpha = 0f;
    private bool _fadingOut = false;
    private readonly System.Windows.Forms.Timer _fadeTimer;

    public SplashForm()
    {
        // ウィンドウ設定
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.Black;          // TransparencyKey と合わせる
        TransparencyKey = Color.Black;
        ShowInTaskbar   = false;
        TopMost         = true;
        Size            = new Size(480, 220);

        // スプラッシュ画像を読み込む
        var imgPath = Path.Combine(AppContext.BaseDirectory, "Resources", "splash.png");
        if (File.Exists(imgPath))
            _splashImage = Image.FromFile(imgPath);

        // フェードイン/アウト タイマー
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _fadeTimer.Tick += FadeTimer_Tick;

        // 表示時間タイマー（2.5秒後にフェードアウト開始）
        _timer = new System.Windows.Forms.Timer { Interval = 2500 };
        _timer.Tick += (s, e) =>
        {
            _timer.Stop();
            _fadingOut = true;
        };

        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _alpha = 0f;
        _fadeTimer.Start();
        _timer.Start();
    }

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        if (_fadingOut)
        {
            _alpha -= 0.06f;
            if (_alpha <= 0f)
            {
                _alpha = 0f;
                _fadeTimer.Stop();
                Close();
                return;
            }
        }
        else
        {
            _alpha += 0.06f;
            if (_alpha >= 1f) _alpha = 1f;
        }

        Opacity = _alpha;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_splashImage == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_splashImage, 0, 0, ClientSize.Width, ClientSize.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _fadeTimer.Dispose();
            _splashImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
