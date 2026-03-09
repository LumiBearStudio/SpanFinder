namespace Span.Models
{
    /// <summary>
    /// 미리보기 패널에서 파일 콘텐츠를 렌더링할 때 사용하는 미리보기 유형.
    /// <see cref="Services.PreviewService"/>가 파일 확장자를 기반으로 결정한다.
    /// </summary>
    public enum PreviewType
    {
        /// <summary>미리보기 불가 또는 선택 없음.</summary>
        None,
        /// <summary>이미지 파일 (JPEG, PNG, GIF, BMP, WebP, TIFF 등).</summary>
        Image,
        /// <summary>텍스트 파일 (TXT, JSON, XML, CSV, MD 등 30+ 확장자).</summary>
        Text,
        /// <summary>PDF 문서 (첫 페이지 미리보기).</summary>
        Pdf,
        /// <summary>비디오/오디오 파일 (MP4, MKV, MP3, AAC 등 + 메타데이터).</summary>
        Media,
        /// <summary>폴더 정보 (하위 항목 수, 크기 등).</summary>
        Folder,
        /// <summary>바이너리 파일 Hex 덤프 (첫 512바이트, 16진수 + ASCII).</summary>
        HexBinary,
        /// <summary>폰트 파일 미리보기.</summary>
        Font,
        /// <summary>아카이브 파일 (ZIP, 7z, RAR, TAR 등).</summary>
        Archive,
        /// <summary>기타 파일 (기본 정보만 표시).</summary>
        Generic
    }
}
