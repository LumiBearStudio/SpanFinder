namespace Span.Models
{
    /// <summary>
    /// SHQueryRecycleBin 결과를 담는 불변 모델.
    /// 사이드바 아이콘 전환 및 상태바 표시에 사용.
    /// </summary>
    public record RecycleBinInfo(long TotalSize, long ItemCount)
    {
        public bool IsEmpty => ItemCount == 0;

        public string SizeDescription => TotalSize switch
        {
            >= 1L << 30 => $"{TotalSize / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{TotalSize / (double)(1L << 20):F1} MB",
            >= 1L << 10 => $"{TotalSize / (double)(1L << 10):F1} KB",
            _ => $"{TotalSize} B"
        };
    }
}
