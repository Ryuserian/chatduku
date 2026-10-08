using System.IO;
using System.Text.Json;
using ChatDuku.Configuration;

namespace ChatDuku.Remote
{
    /// <summary>
    /// PIN 슬롯 목록을 JSON 파일로 저장/불러오는 저장소 클래스.
    /// UI와 무관하므로 어떤 창에서도 재사용할 수 있다.
    /// </summary>
    public sealed class PinRepository
    {
        private readonly PinSlot?[] _slots;

        public PinRepository(int slotCount)
        {
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            _slots = new PinSlot?[slotCount];
        }

        public int Count => _slots.Length;

        public PinSlot? this[int index]
        {
            get => _slots[index];
            set => _slots[index] = value;
        }

        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSettings.PinFileName);

        /// <summary>
        /// 저장된 PIN 파일을 읽어 슬롯을 채운다. 파일이 없거나 손상되면 기본값(비어 있음)을 유지한다.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                var arr = JsonSerializer.Deserialize<PinSlot?[]>(File.ReadAllText(FilePath));
                if (arr is null)
                    return;

                for (int i = 0; i < _slots.Length && i < arr.Length; i++)
                    _slots[i] = arr[i];
            }
            catch
            {
                // 손상된 설정은 무시하고 기본값으로 시작
            }
        }

        /// <summary>현재 슬롯 상태를 파일로 저장한다. 쓰기 실패는 조용히 무시한다(이번 세션에서만 유지).</summary>
        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_slots));
            }
            catch
            {
                // 쓰기 실패는 조용히 무시
            }
        }
    }
}
