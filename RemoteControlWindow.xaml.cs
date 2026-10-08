using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatDuku.Browser;
using ChatDuku.Configuration;
using ChatDuku.Remote;

namespace ChatDuku
{
    /// <summary>
    /// MainWindow를 원격으로 조종하는 리모컨 창.
    /// 위치 이동 / 9-Grid 스냅 / 크기 조절 / URL 이동 / PIN(위치+크기 즐겨찾기) 을 제공한다.
    /// 실제 동작 로직은 WindowController(위치·크기)와 PinRepository(영속화)에 위임하고,
    /// 이 클래스는 버튼 이벤트 처리와 상태 표시만 담당한다.
    /// </summary>
    public partial class RemoteControlWindow : Window
    {
        private readonly MainWindow _target;
        private readonly WindowController _controller;
        private readonly PinRepository _pins;

        private Button[] _pinGo = null!;
        private Button[] _pinSave = null!;
        private Button[] _pinClear = null!;

        public RemoteControlWindow(MainWindow target)
        {
            InitializeComponent();
            _target = target;
            _controller = new WindowController(target);
            _pins = new PinRepository(AppSettings.PinSlotCount);

            _pinGo = [pinGo0, pinGo1, pinGo2, pinGo3];
            _pinSave = [pinSave0, pinSave1, pinSave2, pinSave3];
            _pinClear = [pinClear0, pinClear1, pinClear2, pinClear3];

            _pins.Load();
            RefreshPinUi();
            btnStep.Content = $"간격 {(int)_controller.Step}px";
            UpdateStatus();
        }

        // ===== 위치 이동 =====

        private void Nudge(double dx, double dy)
        {
            _controller.Nudge(dx, dy);
            UpdateStatus();
        }

        private void CenterTarget()
        {
            _controller.Center();
            UpdateStatus();
        }

        private void btnUp_Click(object sender, RoutedEventArgs e) => Nudge(0, -_controller.Step);
        private void btnDown_Click(object sender, RoutedEventArgs e) => Nudge(0, _controller.Step);
        private void btnLeft_Click(object sender, RoutedEventArgs e) => Nudge(-_controller.Step, 0);
        private void btnRight_Click(object sender, RoutedEventArgs e) => Nudge(_controller.Step, 0);
        private void btnCenter_Click(object sender, RoutedEventArgs e) => CenterTarget();

        private void btnStep_Click(object sender, RoutedEventArgs e)
        {
            btnStep.Content = $"간격 {(int)_controller.CycleStep()}px";
            UpdateStatus();
        }

        // Tag "r,c" (0=위/왼쪽, 1=가운데, 2=아래/오른쪽)
        private void Snap_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not string tag)
                return;

            var parts = tag.Split(',');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0].Trim(), out int row) ||
                !int.TryParse(parts[1].Trim(), out int col))
                return;

            _controller.SnapTo(row, col);
            UpdateStatus();
        }

        // ===== 크기 조절 =====

        private void btnWMinus_Click(object sender, RoutedEventArgs e) => ResizeBy(-_controller.Step, 0);
        private void btnWPlus_Click(object sender, RoutedEventArgs e) => ResizeBy(_controller.Step, 0);
        private void btnHMinus_Click(object sender, RoutedEventArgs e) => ResizeBy(0, -_controller.Step);
        private void btnHPlus_Click(object sender, RoutedEventArgs e) => ResizeBy(0, _controller.Step);

        private void ResizeBy(double dw, double dh)
        {
            _controller.ResizeBy(dw, dh);
            UpdateStatus();
        }

        private void btnP25_Click(object sender, RoutedEventArgs e) => SetFraction(0);
        private void btnP50_Click(object sender, RoutedEventArgs e) => SetFraction(1);
        private void btnP75_Click(object sender, RoutedEventArgs e) => SetFraction(2);
        private void btnPMax_Click(object sender, RoutedEventArgs e) => SetFraction(3);

        /// <summary>AppSettings.SizeFractions 프리셋 인덱스를 적용하고 중앙 배치한다.</summary>
        private void SetFraction(int presetIndex)
        {
            if (presetIndex < 0 || presetIndex >= AppSettings.SizeFractions.Length)
                return;

            _controller.SetFraction(AppSettings.SizeFractions[presetIndex]);
            UpdateStatus();
        }

        // ===== URL 이동 =====

        private void NavigateRemote()
        {
            string url = txtRemoteUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
                return;

            _target.NavigateFromRemote(url);
            txtStatus.Text = $"이동 중… {url}";
        }

        private void btnRemoteGo_Click(object sender, RoutedEventArgs e) => NavigateRemote();

        private void txtRemoteUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                NavigateRemote();
        }

        // ===== PIN (위치+크기 즐겨찾기) =====

        private void PinSave_Click(object sender, RoutedEventArgs e)
        {
            int idx = IndexOf(_pinSave, sender);
            if (idx < 0)
                return;

            _pins[idx] = _controller.Capture();
            _pins.Save();
            RefreshPinUi();
            txtStatus.Text = $"PIN {idx + 1}에 현재 위치+크기를 저장했습니다";
        }

        private void PinGo_Click(object sender, RoutedEventArgs e)
        {
            int idx = IndexOf(_pinGo, sender);
            if (idx < 0)
                return;

            var p = _pins[idx];
            if (p is null)
            {
                txtStatus.Text = $"PIN {idx + 1} 슬롯이 비어 있습니다 — 💾로 현재 위치를 저장하세요";
                return;
            }

            _controller.ApplyPin(p);
            UpdateStatus();
        }

        private void PinClear_Click(object sender, RoutedEventArgs e)
        {
            int idx = IndexOf(_pinClear, sender);
            if (idx < 0)
                return;

            _pins[idx] = null;
            _pins.Save();
            RefreshPinUi();
            txtStatus.Text = $"PIN {idx + 1} 슬롯을 비웠습니다";
        }

        private static int IndexOf(Button[] arr, object sender) => Array.IndexOf(arr, sender);

        private void RefreshPinUi()
        {
            for (int i = 0; i < _pins.Count; i++)
            {
                var p = _pins[i];
                _pinGo[i].Content = p is null ? $"📌 PIN {i + 1}" : $"📌 PIN {i + 1} · {(int)p.X},{(int)p.Y}";
                _pinGo[i].ToolTip = p is null
                    ? "저장된 위치 없음 — 💾 버튼으로 현재 위치+크기를 저장"
                    : $"({(int)p.X},{(int)p.Y}) 크기 {(int)p.W}×{(int)p.H}";
                _pinClear[i].IsEnabled = p is not null;
            }
        }

        // ===== 리모컨 창 자체 =====

        // 헤더 빈 곳을 드래그해 리모컨 창을 옮긴다
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

        // 키보드: 방향키=이동, Shift+방향키=크기, +/-=확대/축소, C=중앙, Esc=닫기
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // URL 입력 중에는 키를 가로채지 않음
            if (Keyboard.FocusedElement is TextBox)
                return;

            double s = _controller.Step;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            switch (e.Key)
            {
                case Key.Up when shift: ResizeBy(0, -s); break;
                case Key.Down when shift: ResizeBy(0, s); break;
                case Key.Left when shift: ResizeBy(-s, 0); break;
                case Key.Right when shift: ResizeBy(s, 0); break;
                case Key.Up: Nudge(0, -s); break;
                case Key.Down: Nudge(0, s); break;
                case Key.Left: Nudge(-s, 0); break;
                case Key.Right: Nudge(s, 0); break;
                case Key.C: CenterTarget(); break;
                case Key.Add:
                case Key.OemPlus: ResizeBy(s, s); break;
                case Key.Subtract:
                case Key.OemMinus: ResizeBy(-s, -s); break;
                case Key.Escape:
                    Close();
                    return;
                default:
                    return;
            }

            e.Handled = true;
        }

        private void UpdateStatus() => txtStatus.Text = _controller.Describe();
    }
}
