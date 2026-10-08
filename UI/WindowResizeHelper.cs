using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using ChatDuku.Configuration;

namespace ChatDuku.UI
{
    /// <summary>
    /// WindowStyle=None 창에서 WM_NCHITTEST를 처리해 창 가장자리로 크기 조절을 가능하게 하는 헬퍼,
    /// 그리고 TextBox/Button 위의 클릭을 제외하고 창을 드래그로 이동시키는 헬퍼.
    /// </summary>
    public sealed class WindowResizeHelper
    {
        // WM_NCHITTEST 결과 코드
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private readonly Window _window;

        public WindowResizeHelper(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// 창의 HwndSource에 WM_NCHITTEST 훅을 설치한다.
        /// OnSourceInitialized 시점에 호출해야 한다.
        /// </summary>
        public void Attach()
        {
            if (PresentationSource.FromVisual(_window) is HwndSource source)
                source.AddHook(WndProc);
        }

        /// <summary>
        /// ToolBar/StackPanel 등 빈 곳을 드래그하면 창을 이동한다.
        /// 단, TextBox/Button 등 컨트롤 위의 클릭은 DragMove가 가로채지 않도록 제외한다.
        /// </summary>
        public static void DragMoveUnlessInteractive(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            var src = e.OriginalSource as DependencyObject;
            while (src != null && !ReferenceEquals(src, sender))
            {
                if (src is TextBoxBase || src is ButtonBase)
                    return;
                src = VisualTreeHelper.GetParent(src);
            }

            (sender as Window ?? Window.GetWindow(sender as DependencyObject))?.DragMove();
        }

        /// <summary>히트존 등 단순 요소도 드래그로 창을 이동시킨다.</summary>
        public static void DragMoveOnPress(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                Window.GetWindow(sender as DependencyObject)?.DragMove();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST)
                return IntPtr.Zero;

            // lParam: 화면 좌표(물리 픽셀) -> 창 기준 DIP 좌표로 변환
            int rawX = (short)(lParam.ToInt32() & 0xFFFF);
            int rawY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            Point p = _window.PointFromScreen(new Point(rawX, rawY));

            int hit = ResolveHitCode(p, _window.ActualWidth, _window.ActualHeight, AppSettings.ResizeBorderThickness);

            if (hit != HTCLIENT)
            {
                handled = true;
                return new IntPtr(hit);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 창 내부 좌표와 가장자리 두께를 받아 리사이즈 히트 코드를 판별한다.
        /// 순수 함수라 단위 테스트가 가능하다.
        /// </summary>
        public static int ResolveHitCode(Point p, double width, double height, int border)
        {
            bool left = p.X <= border;
            bool right = p.X >= width - border;
            bool top = p.Y <= border;
            bool bottom = p.Y >= height - border;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return HTCLIENT;
        }
    }
}
