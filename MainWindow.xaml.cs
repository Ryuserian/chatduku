using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatDuku.Browser;
using ChatDuku.Configuration;
using ChatDuku.Remote;
using ChatDuku.UI;

namespace ChatDuku
{
    /// <summary>
    /// 투명 배경 WebView2 브라우저 메인 창.
    /// 각 기능은 전용 클래스에 위임하고, 이 클래스는 조합(와iring)과 UI 표시만 담당한다:
    /// - WebView2Session: WebView2 초기화/내비게이션/배경 투명화
    /// - UrlInputBox     : URL 입력 마스킹
    /// - AutoHideToolbar : 히트존 기반 툴바 자동 숨김
    /// - WindowResizeHelper: 경계 없는 창의 리사이즈/드래그
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly WebView2Session _session;
        private readonly UrlInputBox _urlInput;
        private readonly AutoHideToolbar _autoHide;
        private readonly WindowResizeHelper _resizeHelper;

        // 리모컨 창 (열림/닫힘 토글)
        private RemoteControlWindow? _remoteWin;

        // README 스크린샷 자동화 모드 (CHATDUKU_SHOT=1): 자동 숨김 비활성 + 시나리오 실행
        private readonly bool _screenshotMode;

        public MainWindow()
        {
            InitializeComponent();

            // 창 치수/타이틀을 설정에서 적용 (XAML 하드코딩 제거)
            Title = AppSettings.AppDisplayName;
            Width = AppSettings.MainWindowWidth;
            Height = AppSettings.MainWindowHeight;
            MinWidth = AppSettings.MainWindowMinWidth;
            MinHeight = AppSettings.MainWindowMinHeight;

            _session = new WebView2Session(webView);
            _urlInput = new UrlInputBox(txtUrl);
            _autoHide = new AutoHideToolbar(toolbar, () => txtUrl.IsKeyboardFocusWithin || _screenshotMode);
            _resizeHelper = new WindowResizeHelper(this);

            _screenshotMode = Environment.GetEnvironmentVariable("CHATDUKU_SHOT") == "1";

            ApplyFrameStyle();

            _session.NavigationStateUpdated += (canBack, canForward) =>
            {
                btnBack.IsEnabled = canBack;
                btnForward.IsEnabled = canForward;
            };
            _session.SourceChanged += source =>
            {
                // 실제 URL은 Tag(태그)에 두고, 입력창에 포커스가 없으면 마스킹해서 표시
                txtUrl.Tag = source;
                if (!txtUrl.IsKeyboardFocusWithin)
                    txtUrl.Text = UrlHelper.Mask(source);

                // 외곽 사각형: 초기(데모) 상태에서만 표시
                UpdateFrameVisibility(source);
            };
            _session.NavigationCompleted += (_, originalColor) =>
            {
                // 원래 배경색을 타이틀에 표시
                if (originalColor is not null)
                    Title = $"{AppSettings.AppDisplayName} — 원래 배경색: {originalColor}";
            };

            _urlInput.NavigateRequested += url => _session.Navigate(url);

            _ = InitializeAsync();

            // 문서용 자동 시나리오 (기본에선 절대 실행되지 않음)
            if (_screenshotMode)
                _ = RunScreenshotScenarioAsync();
        }

        /// <summary>
        /// README 스크린샷 자동화를 위한 데모 시나리오. CHATDUKU_SHOT=1일 때만 실행.
        /// 마우스 시뮬레이션 없이 정해진 시간에 툴바→리모컨을 스스로 열어
        /// 일관된 캡처를 가능하게 한다.
        /// (초기: 0~5s / 툴바 노출: 5s~ / 리모컨 오픈: 8s~)
        /// </summary>
        private async Task RunScreenshotScenarioAsync()
        {
            await Task.Delay(5000);
            _autoHide.Show();
            await Task.Delay(3000);
            btnRemote_Click(this, new RoutedEventArgs());
        }

        private async Task InitializeAsync()
        {
            await _session.InitializeAsync();
        }

        /// <summary>
        /// 리모컨 창에서 URL 이동을 요청했을 때 호출되는 진입점.
        /// </summary>
        public void NavigateFromRemote(string url)
        {
            _session.Navigate(url);
            // URL 입력창에도 반영 (SourceChanged가 갱신해주지만 선반영해서 즉시 표시)
            Dispatcher.InvokeAsync(() => _urlInput.SetUrl(url.Trim()));
        }

        // ===== 외곽 사각형 (투명 창의 경계/초기 상태 표시) =====

        /// <summary>외곽 테두리 색/두께를 설정에서 적용한다.</summary>
        private void ApplyFrameStyle()
        {
            frame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.WindowFrameColor));
            frame.BorderThickness = new Thickness(AppSettings.WindowFrameThickness);
        }

        /// <summary>
        /// 초기 데모 상태(about:blank 또는 NavigateToString의 data: URL)일 때만
        /// 외곽 사각형을 표시하고, 실제 http(s) 페이지로 이동하면 숨긴다.
        /// </summary>
        private void UpdateFrameVisibility(string? source)
        {
            frame.Visibility = UrlHelper.HasScheme(source ?? "")
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // ===== 툴바 버튼 핸들러 =====

        private void btnBack_Click(object sender, RoutedEventArgs e) => _session.GoBack();

        private void btnForward_Click(object sender, RoutedEventArgs e) => _session.GoForward();

        private void btnRefresh_Click(object sender, RoutedEventArgs e) => _session.Reload();

        private void btnGo_Click(object sender, RoutedEventArgs e) => _session.Navigate(_urlInput.GetActualUrl());

        // ===== 리모컨 창 토글 =====

        private void btnRemote_Click(object sender, RoutedEventArgs e)
        {
            if (_remoteWin is { IsLoaded: true })
            {
                _remoteWin.Close();
                _remoteWin = null;
                return;
            }

            _remoteWin = new RemoteControlWindow(this);
            RemoteWindowPlacer.PlaceBeside(this, _remoteWin);

            _remoteWin.Closed += (_, _) => _remoteWin = null;
            _remoteWin.Show();
        }

        // ===== 툴바 자동 숨김 (AutoHideToolbar에 위임) =====

        private void HotZone_MouseEnter(object sender, MouseEventArgs e) => _autoHide.Show();

        private void Toolbar_MouseEnter(object sender, MouseEventArgs e)
        {
            // 툴바 위에 있는 동안엔 숨김 취소
            _autoHide.CancelHide();
            _autoHide.Show();
        }

        private void Toolbar_MouseLeave(object sender, MouseEventArgs e)
        {
            // 툴바를 벗어나면 잠시 후 자동 숨김
            _autoHide.ScheduleHide();
        }

        // ===== 항상 위에 / 닫기 =====

        private void btnTopMost_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            btnTopMost.FontWeight = Topmost ? FontWeights.Bold : FontWeights.Normal;
            btnTopMost.ToolTip = Topmost ? "위 고정 해제" : "항상 위에 고정";
        }

        private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ===== 창 이동 / 리사이즈 =====

        // WindowStyle=None이므로 툴바 빈 곳을 드래그하면 창 이동
        private void Toolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            WindowResizeHelper.DragMoveUnlessInteractive(sender, e);

        // 히트존도 드래그로 창 이동 가능
        private void HotZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            WindowResizeHelper.DragMoveOnPress(sender, e);

        // WM_NCHITTEST 처리로 경계 없는 창 리사이즈 지원
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _resizeHelper.Attach();
        }
    }
}
