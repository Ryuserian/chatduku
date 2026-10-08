using ChatDuku.Configuration;

namespace ChatDuku.Browser
{
    /// <summary>
    /// 입력 문자열을 이동 가능한 URL로 정규화하는 순수 로직 클래스.
    /// 스킴이 없는 도메인은 https://를 붙이고, 순수 텍스트는 검색 URL로 변환한다.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// 입력값을 이동 대상 URL로 변환한다. 빈 입력이면 null을 반환한다.
        /// </summary>
        public static string? Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string url = input.Trim();

            if (HasScheme(url))
                return url;

            // "."이 포함되고 공백이 없으면 도메인으로 간주
            if (url.Contains('.') && !url.Contains(' '))
                return "https://" + url;

            return string.Format(AppSettings.SearchUrlFormat, Uri.EscapeDataString(url));
        }

        /// <summary>http:// 또는 https:// 스킴 여부를 판별한다.</summary>
        public static bool HasScheme(string url) =>
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// URL을 비밀번호처럼 가린다. 스킴(http://, https://)만 남기고 나머지는 마스킹 문자로 치환.
        /// </summary>
        public static string Mask(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            int keep = schemeEnd >= 0 ? schemeEnd + 3 : 0;
            return url[..keep] + new string(AppSettings.UrlMaskChar, Math.Max(url.Length - keep, 0));
        }
    }
}
