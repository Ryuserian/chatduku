using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatDuku.Browser;

namespace ChatDuku.Browser
{
    /// <summary>
    /// URL 입력 TextBox의 "마스킹 동작"을 캡슐화한 헬퍼.
    /// 실제 URL은 Tag에 보관하고, 포커스 상태에 따라 실제 URL/마스킹 텍스트를 전환한다.
    /// 창 코드비하인드는 GotFocus/LostFocus/KeyDown 이벤트를 이 클래스로 위임한다.
    /// </summary>
    public sealed class UrlInputBox
    {
        private readonly TextBox _box;

        /// <summary>Enter 키로 이동이 요청되었을 때 발생 (정규화 이전의 원본 입력 포함).</summary>
        public event Action<string>? NavigateRequested;

        public UrlInputBox(TextBox box)
        {
            _box = box ?? throw new ArgumentNullException(nameof(box));

            _box.GotKeyboardFocus += OnGotFocus;
            _box.LostKeyboardFocus += OnLostFocus;
            _box.TextChanged += OnTextChanged;
            _box.PreviewKeyDown += OnKeyDown;
        }

        /// <summary>현재 입력(또는 Tag에 저장된) 실제 URL을 반환한다.</summary>
        public string GetActualUrl()
        {
            return _box.IsKeyboardFocusWithin
                ? _box.Text
                : (_box.Tag as string ?? _box.Text);
        }

        /// <summary>실제 URL을 입력창에 반영한다 (포커스 여부에 따라 마스킹 여부 결정).</summary>
        public void SetUrl(string url)
        {
            _box.Tag = url;
            _box.Text = _box.IsKeyboardFocusWithin ? url : UrlHelper.Mask(url);
        }

        private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            string real = _box.Tag as string ?? _box.Text;
            if (_box.Text == UrlHelper.Mask(real))
                _box.Text = real;
            _box.SelectAll();
        }

        private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _box.Text = UrlHelper.Mask(_box.Text);
        }

        /// <summary>
        /// 사용자가 직접 입력하는 동안엔 Tag(실제 URL)를 입력값과 동기화한다.
        /// (SourceChanged처럼 프로그램이 텍스트를 갱신할 땐 Tag가 이미 설정된 상태라 해당 없음)
        /// </summary>
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_box.IsKeyboardFocusWithin)
                _box.Tag = _box.Text;
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                NavigateRequested?.Invoke(GetActualUrl().Trim());
        }
    }
}
