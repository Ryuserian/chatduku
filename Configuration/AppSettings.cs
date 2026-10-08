using System.IO;
using System.Text.Json;

namespace ChatDuku.Configuration
{
    /// <summary>
    /// 앱 전역 설정. 기본값을 제공하며, 실행 폴더에 appsettings.json이 있으면 값이 덮어써진다.
    /// 하드코딩을 제거하고 모든 매직 넘버/문자열의 단일 출처(single source of truth)가 된다.
    /// </summary>
    public static class AppSettings
    {
        public const string ConfigFileName = "appsettings.json";

        // ===== 일반 =====
        public static string AppDisplayName { get; private set; } = "ChatDuku";

        // ===== 메인 창 치수 =====
        public static double MainWindowWidth { get; private set; } = 900;
        public static double MainWindowHeight { get; private set; } = 600;
        public static double MainWindowMinWidth { get; private set; } = 420;
        public static double MainWindowMinHeight { get; private set; } = 320;

        // ===== 브라우저 =====
        public static string WebViewUserDataFolderName { get; private set; } = "WebView2UserData";
        public static string SearchUrlFormat { get; private set; } = "https://www.bing.com/search?q={0}";
        public static int BackgroundReapplyDelayMs { get; private set; } = 800;
        public static char UrlMaskChar { get; private set; } = '●';

        // ===== 창 외곽 테두리 =====
        public static string WindowFrameColor { get; private set; } = "#66FFFFFF";
        public static int WindowFrameThickness { get; private set; } = 2;

        // ===== 자동 숨김 툴바 =====
        public static int ToolbarHideDelayMs { get; private set; } = 1500;
        public static int ToolbarFadeInMs { get; private set; } = 150;
        public static int ToolbarFadeOutMs { get; private set; } = 250;

        // ===== 창 리사이즈 =====
        public static int ResizeBorderThickness { get; private set; } = 8;

        // ===== 리모컨 =====
        public static int RemoteWindowWidth { get; private set; } = 300;
        public static int RemoteWindowGap { get; private set; } = 16;
        public static int MinVisibleOnScreenPx { get; private set; } = 60;
        public static int MinVisibleTopPx { get; private set; } = 24;
        public static int PinSlotCount { get; private set; } = 4;
        public static string PinFileName { get; private set; } = "pins.json";

        // ===== 이동 간격 / 크기 프리셋 =====
        public static int[] MoveStepCycle { get; private set; } = [10, 25, 50, 100];
        public static int DefaultStepIndex { get; private set; } = 1;
        public static double[] SizeFractions { get; private set; } = [0.25, 0.50, 0.75, 0.95];

        /// <summary>
        /// appsettings.json을 읽어 있으면 기본값을 덮어쓴다. 파일이 없거나 손상되면 기본값을 유지한다.
        /// </summary>
        public static void Load()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                if (!File.Exists(path))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                AppDisplayName = Get(root, nameof(AppDisplayName), AppDisplayName);
                WebViewUserDataFolderName = Get(root, nameof(WebViewUserDataFolderName), WebViewUserDataFolderName);
                SearchUrlFormat = Get(root, nameof(SearchUrlFormat), SearchUrlFormat);
                BackgroundReapplyDelayMs = Get(root, nameof(BackgroundReapplyDelayMs), BackgroundReapplyDelayMs);
                UrlMaskChar = Get(root, nameof(UrlMaskChar), UrlMaskChar);
                ToolbarHideDelayMs = Get(root, nameof(ToolbarHideDelayMs), ToolbarHideDelayMs);
                ToolbarFadeInMs = Get(root, nameof(ToolbarFadeInMs), ToolbarFadeInMs);
                ToolbarFadeOutMs = Get(root, nameof(ToolbarFadeOutMs), ToolbarFadeOutMs);
                ResizeBorderThickness = Get(root, nameof(ResizeBorderThickness), ResizeBorderThickness);
                WindowFrameColor = Get(root, nameof(WindowFrameColor), WindowFrameColor);
                WindowFrameThickness = Get(root, nameof(WindowFrameThickness), WindowFrameThickness);
                RemoteWindowWidth = Get(root, nameof(RemoteWindowWidth), RemoteWindowWidth);
                RemoteWindowGap = Get(root, nameof(RemoteWindowGap), RemoteWindowGap);
                MinVisibleOnScreenPx = Get(root, nameof(MinVisibleOnScreenPx), MinVisibleOnScreenPx);
                MinVisibleTopPx = Get(root, nameof(MinVisibleTopPx), MinVisibleTopPx);
                PinSlotCount = Get(root, nameof(PinSlotCount), PinSlotCount);
                PinFileName = Get(root, nameof(PinFileName), PinFileName);
                MainWindowWidth = Get(root, nameof(MainWindowWidth), MainWindowWidth);
                MainWindowHeight = Get(root, nameof(MainWindowHeight), MainWindowHeight);
                MainWindowMinWidth = Get(root, nameof(MainWindowMinWidth), MainWindowMinWidth);
                MainWindowMinHeight = Get(root, nameof(MainWindowMinHeight), MainWindowMinHeight);
                MoveStepCycle = Get(root, nameof(MoveStepCycle), MoveStepCycle);
                DefaultStepIndex = Get(root, nameof(DefaultStepIndex), DefaultStepIndex);
                SizeFractions = Get(root, nameof(SizeFractions), SizeFractions);
            }
            catch
            {
                // 손상된 설정은 무시하고 기본값으로 동작
            }
        }

        private static string Get(JsonElement root, string name, string fallback) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback
                : fallback;

        private static int Get(JsonElement root, string name, int fallback) =>
            root.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

        private static double Get(JsonElement root, string name, double fallback) =>
            root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;

        private static char Get(JsonElement root, string name, char fallback) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s[0] : fallback;

        private static int[] Get(JsonElement root, string name, int[] fallback)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() == 0)
                return fallback;
            return v.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        }

        private static double[] Get(JsonElement root, string name, double[] fallback)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() == 0)
                return fallback;
            return v.EnumerateArray().Select(e => e.GetDouble()).ToArray();
        }
    }
}
