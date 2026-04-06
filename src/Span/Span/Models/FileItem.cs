using System;
using System.Text;

namespace Span.Models
{
    /// <summary>
    /// 단일 파일을 나타내는 데이터 모델.
    /// <see cref="IFileSystemItem"/>을 구현하며, FolderViewModel.Children 컬렉션에 포함된다.
    /// </summary>
    public class FileItem : IFileSystemItem
    {
        private string _name = string.Empty;

        /// <summary>
        /// 파일명 (확장자 포함).
        /// setter에서 NFC 정규화를 자동 적용하여 NFD 파일명(macOS 유래 등)의 표시 깨짐을 방지한다.
        /// </summary>
        public string Name
        {
            get => _name;
            set => _name = NfcNormalize(value);
        }

        /// <summary>파일 전체 경로.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>파일 크기 (바이트 단위).</summary>
        public long Size { get; set; }

        /// <summary>마지막 수정 시각.</summary>
        public DateTime DateModified { get; set; }

        /// <summary>현재 아이콘 팩의 기본 파일 아이콘 글리프. IconService.Current를 통해 런타임에 결정된다.</summary>
        public string IconGlyph => Span.Services.IconService.Current?.FileDefaultGlyph ?? "\uECE0";

        /// <summary>파일 타입 설명 문자열 (예: "Text Document", "PNG Image").</summary>
        public string FileType { get; set; } = string.Empty;

        /// <summary>숨김 파일 여부.</summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// NFD 문자열을 NFC로 정규화한다. 이미 NFC이면 원본 그대로 반환 (성능 최적화).
        /// macOS/웹에서 다운로드한 파일명의 결합 문자(탁음, 자모 등) 표시 깨짐을 방지한다.
        /// </summary>
        internal static string NfcNormalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.IsNormalized(NormalizationForm.FormC) ? value : value.Normalize(NormalizationForm.FormC);
        }
    }
}
