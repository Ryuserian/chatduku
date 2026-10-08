using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ChatDuku.Configuration;

namespace ChatDuku.UI
{
    /// <summary>
    /// 툴바의 자동 숨김/페이드 표시 동작을 캡슐화한 클래스.
    /// 히트존 진입 시 표시, 이탈 후 일정 시간 뒤 자동 숨김한다.
    /// </summary>
    public sealed class AutoHideToolbar
    {
        private readonly FrameworkElement _toolbar;
        private readonly Func<bool>? _keepVisible;
        private readonly DispatcherTimer _hideTimer;
        private bool _shown;

        /// <param name="toolbar">표시/숨김 대상 요소</param>
        /// <param name="keepVisible">true를 반환하는 동안엔 자동 숨김을 보류 (예: 입력창 포커스)</param>
        public AutoHideToolbar(FrameworkElement toolbar, Func<bool>? keepVisible = null)
        {
            _toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
            _keepVisible = keepVisible;

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppSettings.ToolbarHideDelayMs) };
            _hideTimer.Tick += OnHideTimerTick;
        }

        /// <summary>표시 상태를 유지해야 하는지 묻는 콜백 결과를 반영해 숨김을 예약한다.</summary>
        public void ScheduleHide()
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        public void CancelHide() => _hideTimer.Stop();

        public void Show()
        {
            CancelHide();
            if (_shown && _toolbar.Visibility == Visibility.Visible)
                return;

            _shown = true;
            _toolbar.Visibility = Visibility.Visible;
            _toolbar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(AppSettings.ToolbarFadeInMs)));
        }

        public void Hide()
        {
            if (!_shown)
                return;

            _shown = false;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(AppSettings.ToolbarFadeOutMs));
            anim.Completed += (_, _) =>
            {
                if (!_shown)
                    _toolbar.Visibility = Visibility.Collapsed;
            };
            _toolbar.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void OnHideTimerTick(object? sender, EventArgs e)
        {
            _hideTimer.Stop();
            // 입력창에 포커스가 있으면 숨기지 않음
            if (_keepVisible?.Invoke() == true)
                _hideTimer.Start();
            else
                Hide();
        }
    }
}
