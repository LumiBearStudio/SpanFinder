namespace Span.Helpers
{
    /// <summary>
    /// XAML x:Bind에서 직접 호출하는 정적 헬퍼 메서드 모음.
    /// </summary>
    public static class ConverterHelpers
    {
        /// <summary>사이드바 폴더 트리 확장/접힘 상태에 따른 쉐브론 회전 각도 (확장=0°, 접힘=-90°).</summary>
        public static double ChevronAngle(bool expanded) => expanded ? 0 : -90;
    }
}
