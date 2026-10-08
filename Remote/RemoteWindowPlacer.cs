using System.Windows;
using ChatDuku.Configuration;

namespace ChatDuku.Remote
{
    /// <summary>
    /// 보조 창(리모컨)을 기준 창 옆에 배치하는 위치 계산 헬퍼.
    /// 오른쪽 공간이 부족하면 왼쪽에 배치한다.
    /// </summary>
    public static class RemoteWindowPlacer
    {
        public static void PlaceBeside(Window main, Window remote)
        {
            double remoteWidth = double.IsFinite(remote.Width) ? remote.Width : AppSettings.RemoteWindowWidth;
            var wa = SystemParameters.WorkArea;

            double x = main.Left + main.Width + AppSettings.RemoteWindowGap;
            if (x + remoteWidth > wa.Right)
                x = Math.Max(wa.Left, main.Left - remoteWidth);

            remote.Left = x;
            remote.Top = Math.Max(wa.Top, main.Top);
        }
    }
}
