using System.Windows;
using ChatDuku.Configuration;

namespace ChatDuku.Remote
{
    /// <summary>
    /// 대상 창의 위치/크기를 제어하는 로직 클래스 (리모컨의 두뇌).
    /// 이동 간격 순환, 9-Grid 스냅, 중심 유지 크기 조절, PIN 적용 등을 담당한다.
    /// WPF Window에만 의존하며 버튼/이벤트 등 UI와 무관하다.
    /// </summary>
    public sealed class WindowController
    {
        private readonly Window _target;
        private int _stepIndex;

        public WindowController(Window target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _stepIndex = Math.Clamp(AppSettings.DefaultStepIndex, 0, AppSettings.MoveStepCycle.Length - 1);
        }

        /// <summary>현재 이동 간격(px).</summary>
        public double Step => AppSettings.MoveStepCycle[_stepIndex];

        /// <summary>이동 간격을 다음 값으로 순환시키고 반환한다.</summary>
        public double CycleStep()
        {
            _stepIndex = (_stepIndex + 1) % AppSettings.MoveStepCycle.Length;
            return Step;
        }

        /// <summary>창을(dx, dy)만큼 이동시킨다. 화면 밖에 완전히 사라지지 않게 클램프한다.</summary>
        public void Nudge(double dx, double dy)
        {
            _target.Left = ClampLeft(_target.Left + dx, _target.ActualWidth);
            _target.Top = ClampTop(_target.Top + dy, SystemParameters.WorkArea);
        }

        /// <summary>창을 작업 공간 중앙에 배치한다.</summary>
        public void Center()
        {
            var wa = SystemParameters.WorkArea;
            _target.Left = wa.Left + (wa.Width - _target.ActualWidth) / 2;
            _target.Top = wa.Top + (wa.Height - _target.ActualHeight) / 2;
        }

        /// <summary>
        /// 9-Grid 스냅. row/col은 0(위/왼쪽), 1(가운데), 2(아래/오른쪽).
        /// </summary>
        public void SnapTo(int row, int col)
        {
            var wa = SystemParameters.WorkArea;
            double w = _target.ActualWidth, h = _target.ActualHeight;

            _target.Left = col switch
            {
                0 => wa.Left,
                2 => wa.Right - w,
                _ => wa.Left + (wa.Width - w) / 2,
            };
            _target.Top = row switch
            {
                0 => wa.Top,
                2 => wa.Bottom - h,
                _ => wa.Top + (wa.Height - h) / 2,
            };
        }

        /// <summary>중심을 유지한 채 창 크기를 (dw, dh)만큼 변경한다.</summary>
        public void ResizeBy(double dw, double dh)
        {
            var wa = SystemParameters.WorkArea;
            double w = Math.Clamp(_target.ActualWidth + dw, _target.MinWidth, wa.Width);
            double h = Math.Clamp(_target.ActualHeight + dh, _target.MinHeight, wa.Height);

            // 중심을 유지한 채로 크기 변경
            _target.Left += (_target.ActualWidth - w) / 2.0;
            _target.Top += (_target.ActualHeight - h) / 2.0;
            _target.Width = w;
            _target.Height = h;
        }

        /// <summary>창을 작업 공간의 fraction 크기로 만든 뒤 중앙에 배치한다.</summary>
        public void SetFraction(double fraction)
        {
            var wa = SystemParameters.WorkArea;
            _target.Width = Math.Max(_target.MinWidth, wa.Width * fraction);
            _target.Height = Math.Max(_target.MinHeight, wa.Height * fraction);
            Center();
        }

        /// <summary>PIN 슬롯의 위치+크기를 창에 적용한다.</summary>
        public void ApplyPin(PinSlot pin)
        {
            _target.Width = pin.W;
            _target.Height = pin.H;

            var wa = SystemParameters.WorkArea;
            int margin = AppSettings.MinVisibleOnScreenPx;
            int topMargin = AppSettings.MinVisibleTopPx;

            _target.Left = Math.Clamp(pin.X, wa.Left - pin.W + margin, wa.Right - margin);
            _target.Top = Math.Clamp(pin.Y, wa.Top - topMargin, wa.Bottom - margin);
        }

        /// <summary>현재 창 상태를 PIN 슬롯 데이터로 captured한다.</summary>
        public PinSlot Capture() =>
            new(_target.Left, _target.Top, _target.ActualWidth, _target.ActualHeight);

        /// <summary>상수 상태로 만드는 상태 문자열 (상태 표시줄용).</summary>
        public string Describe() =>
            $"메인 창: ({(int)_target.Left}, {(int)_target.Top}) {_target.ActualWidth:0}×{_target.ActualHeight:0} · 이동단위 {Step}px";

        private static double ClampLeft(double left, double width)
        {
            var wa = SystemParameters.WorkArea;
            int margin = AppSettings.MinVisibleOnScreenPx;
            return Math.Clamp(left, wa.Left - width + margin, wa.Right - margin);
        }

        private static double ClampTop(double top, Rect wa)
        {
            int margin = AppSettings.MinVisibleOnScreenPx;
            int topMargin = AppSettings.MinVisibleTopPx;
            return Math.Clamp(top, wa.Top - topMargin, wa.Bottom - margin);
        }
    }
}
