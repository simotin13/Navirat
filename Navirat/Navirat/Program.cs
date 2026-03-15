using Navirat.Forms;

namespace Navirat;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // スプラッシュスクリーンを別スレッドで表示しながら MainForm を初期化
        using var splash = new SplashForm();
        splash.Show();
        Application.DoEvents();

        var mainForm = new MainForm();

        // スプラッシュが閉じるまで待機してから MainForm を表示
        while (splash.Visible)
            Application.DoEvents();

        Application.Run(mainForm);
    }
}
