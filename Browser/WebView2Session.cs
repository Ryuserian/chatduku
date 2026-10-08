using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
using ChatDuku.Configuration;

namespace ChatDuku.Browser
{
    /// <summary>
    /// WebView2的生命周期(초기화/내비게이션/배경 투명화)을 캡슐화한 래퍼 클래스.
    /// Window에서 브라우저 관련 코드를 분리해, UI는 이벤트 구독과 표시만 담당한다.
    /// </summary>
    public sealed class WebView2Session
    {
        private readonly WebView2 _webView;

        /// <summary>내비게이션 완료 시 발생 (isSuccess, originalColor).</summary>
        public event Action<bool, string?>? NavigationCompleted;

        /// <summary>현재 소스 URL이 변경될 때 발생.</summary>
        public event Action<string>? SourceChanged;

        /// <summary>뒤로/앞으로 가능 여부가 변경될 때 발생.</summary>
        public event Action<bool, bool>? NavigationStateUpdated;

        public WebView2Session(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        }

        /// <summary>WebView2를 초기화하고 기본 데모 페이지로 이동한다.</summary>
        public async Task InitializeAsync()
        {
            // WebView2 사용자 데이터 폴더를 앱 실행 위치에 생성
            string userDataFolder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, AppSettings.WebViewUserDataFolderName);

            // 투명 배경 활성화 (페이지 배경을 투명화하면 창 너머가 보임)
            _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(null, userDataFolder, null);
            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.NavigationCompleted += OnCoreNavigationCompleted;
            _webView.CoreWebView2.SourceChanged += OnCoreSourceChanged;

            // 투명 배경 데모 페이지로 시작 (투명 효과를 바로 확인할 수 있도록)
            _webView.CoreWebView2.NavigateToString(TransparentPageScripts.DemoHtml);
        }

        /// <summary>URL을 정규화해 이동한다. 빈 입력이면 아무것도 하지 않는다.</summary>
        public void Navigate(string? rawInput)
        {
            if (_webView.CoreWebView2 is null)
                return;

            string? url = UrlHelper.Normalize(rawInput);
            if (url is not null)
                _webView.CoreWebView2.Navigate(url);
        }

        public void GoBack()
        {
            if (_webView.CoreWebView2?.CanGoBack == true)
                _webView.CoreWebView2.GoBack();
        }

        public void GoForward()
        {
            if (_webView.CoreWebView2?.CanGoForward == true)
                _webView.CoreWebView2.GoForward();
        }

        public void Reload() => _webView.CoreWebView2?.Reload();

        public bool CanGoBack => _webView.CoreWebView2?.CanGoBack == true;

        public bool CanGoForward => _webView.CoreWebView2?.CanGoForward == true;

        public string? CurrentSource => _webView.CoreWebView2?.Source;

        private void OnCoreSourceChanged(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2SourceChangedEventArgs e)
        {
            string source = _webView.CoreWebView2.Source;
            SourceChanged?.Invoke(source);
        }

        private async void OnCoreNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            NavigationStateUpdated?.Invoke(CanGoBack, CanGoForward);

            if (!e.IsSuccess)
            {
                NavigationCompleted?.Invoke(false, null);
                return;
            }

            // 초기 데모 페이지(data:/about:blank)는 배경을 제거하지 않는다 —
            // 안내 카드 배경까지 지워져 글씨가 안 보이게 된다.
            if (!UrlHelper.HasScheme(_webView.CoreWebView2.Source ?? ""))
            {
                NavigationCompleted?.Invoke(true, null);
                return;
            }

            string? originalColor = await StripPageBackgroundAsync();
            NavigationCompleted?.Invoke(true, originalColor);
        }

        /// <summary>
        /// 페이지의 배경색을 조회한 뒤, 배경색/배경이미지를 투명화한다.
        /// 원래 배경색을 반환하며(실패 시 null), 크로스도메인 정책 등으로 실패해도 예외를 던지지 않는다.
        /// </summary>
        public async Task<string?> StripPageBackgroundAsync()
        {
            try
            {
                // 1) 페이지의 배경색 조회 (body → html 순으로 probe)
                string probeResult = await _webView.CoreWebView2.ExecuteScriptAsync(TransparentPageScripts.BackgroundProbeJs);
                string originalColor = JsonSerializer.Deserialize<string>(probeResult) ?? "transparent";

                // 2) 조회한 배경색을 포함한 모든 배경을 transparent로 덮어쓰기
                await _webView.CoreWebView2.ExecuteScriptAsync(TransparentPageScripts.TransparentizeJs);

                // 3) 늦게 로드되는 CSS가 다시 배경을 입힐 수 있어 한 번 더 수행
                await Task.Delay(AppSettings.BackgroundReapplyDelayMs);
                await _webView.CoreWebView2.ExecuteScriptAsync(TransparentPageScripts.TransparentizeJs);

                return originalColor;
            }
            catch
            {
                // 크로스도메인 정책 등으로 실패할 수 있어 조용히 무시
                return null;
            }
        }
    }
}
