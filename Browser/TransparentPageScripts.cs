using ChatDuku.Configuration;

namespace ChatDuku.Browser
{
    /// <summary>
    /// WebView2 페이지에 주입할 JavaScript(배경 조회/투명화/데모 페이지)를 모은 정적 클래스.
    /// 스크립트 전용이라 다른 컴포넌트에 의존하지 않는다.
    /// </summary>
    public static class TransparentPageScripts
    {
        /// <summary>페이지의 배경색을 읽기만 하는 스크립트.</summary>
        public const string BackgroundProbeJs = """
            (() => {
              const pick = (el) => {
                if (!el) return null;
                const cs = getComputedStyle(el);
                if (cs.backgroundColor && cs.backgroundColor !== "transparent" && cs.backgroundColor !== "rgba(0, 0, 0, 0)")
                    return cs.backgroundColor;
                if (cs.backgroundImage && cs.backgroundImage !== "none")
                    return "background-image";
                return null;
              };
              return pick(document.body) || pick(document.documentElement) || "transparent";
            })()
            """;

        /// <summary>페이지 전체의 배경/배경 이미지를 투명화하는 스크립트.</summary>
        public const string TransparentizeJs = """
            (() => {
              const wipe = (el) => {
                el.style.setProperty("background-color", "transparent", "important");
                el.style.setProperty("background-image", "none", "important");
              };
              // 내부 요소 배경 제거
              document.querySelectorAll(
                "div,section,main,header,footer,aside,article,table,tbody,thead,tr,td,th,ul,ol,li,p,span,form").forEach((el) => {
                const cs = getComputedStyle(el);
                const solid = cs.backgroundColor !== "rgba(0, 0, 0, 0)" && cs.backgroundColor !== "transparent";
                const hasImg = !!cs.backgroundImage && cs.backgroundImage !== "none";
                if (solid || hasImg) wipe(el);
              });
              // 최상위(html/body) 배경도 투명
              document.documentElement.style.setProperty("background-color", "transparent", "important");
              document.documentElement.style.setProperty("background-image", "none", "important");
              if (document.body) {
                document.body.style.setProperty("background-color", "transparent", "important");
                document.body.style.setProperty("background-image", "none", "important");
              }
              return true;
            })()
            """;

        /// <summary>배경이 transparent인 기본 데모 페이지.</summary>
        public static string DemoHtml => string.Format(DemoHtmlTemplate, AppSettings.AppDisplayName);

        private const string DemoHtmlTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8" />
                <style>
                    html, body { background: transparent; margin: 0; height: 100%; }
                    body { display: flex; align-items: center; justify-content: center; }
                    .content {
                        color: white; font-family: 'Segoe UI', sans-serif;
                        text-shadow: 0 1px 4px rgba(0,0,0,0.8);
                        padding: 24px;
                        /* 반투명 카드 배경 — 어떤 바탕화면 위에서도 초기 안내 글씨가 잘 보이도록 */
                        background: rgba(0, 0, 0, 0.35);
                        border-radius: 12px;
                        text-align: center;
                    }
                </style>
            </head>
            <body>
                <div class="content">
                    <h2>🪟 {0}</h2>
                    <p>투명 배경 WebView2 — 이 텍스트 뒤로 바탕화면이 보입니다.</p>
                    <p>상단 히트존에 마우스를 올려 툴바를 열고, URL을 입력해 보세요.</p>
                </div>
            </body>
            </html>
            """;
    }
}
