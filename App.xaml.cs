using System.Windows;
using ChatDuku.Configuration;

namespace ChatDuku
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 창이 생성되기 전에 앱 설정(appsettings.json)을 먼저 로드한다
            AppSettings.Load();
            base.OnStartup(e);
        }
    }
}
