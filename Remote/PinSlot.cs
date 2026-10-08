namespace ChatDuku.Remote
{
    /// <summary>
    /// 창의 위치+크기를 저장하는 PIN 슬롯 데이터.
    /// </summary>
    /// <param name="X">창 왼쪽 좌표</param>
    /// <param name="Y">창 위쪽 좌표</param>
    /// <param name="W">창 너비</param>
    /// <param name="H">창 높이</param>
    public sealed record PinSlot(double X, double Y, double W, double H);
}
