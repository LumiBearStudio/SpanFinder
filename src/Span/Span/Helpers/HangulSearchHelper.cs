using System;
using System.Text;

namespace Span.Helpers
{
    /// <summary>
    /// 한글 검색 헬퍼: 초성 매칭 + 자음/모음 분해 매칭 + 영문 fuzzy 매칭.
    /// Command Palette 등 검색 UI에서 사용.
    /// </summary>
    public static class HangulSearchHelper
    {
        // 한글 음절 범위
        private const int HangulBase = 0xAC00;     // '가'
        private const int HangulEnd = 0xD7A3;      // '힣'
        private const int ChosungCount = 19;
        private const int JungsungCount = 21;
        private const int JongsungCount = 28;

        // 초성 19개
        private static readonly char[] Chosungs = new[]
        {
            'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
            'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
        };

        /// <summary>
        /// 한글 음절을 초성으로 변환. 한글이 아니면 원본 반환.
        /// </summary>
        public static char ToChosung(char ch)
        {
            if (ch >= HangulBase && ch <= HangulEnd)
            {
                int idx = (ch - HangulBase) / (JungsungCount * JongsungCount);
                return Chosungs[idx];
            }
            return ch;
        }

        /// <summary>
        /// 문자열의 모든 한글 음절을 초성으로 변환한 문자열을 반환.
        /// 예: "복사하기" → "ㅂㅅㅎㄱ"
        /// </summary>
        public static string ToChosungString(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
                sb.Append(ToChosung(ch));
            return sb.ToString();
        }

        /// <summary>
        /// 쿼리가 모두 초성(ㄱ~ㅎ)으로 구성되어 있는지 확인.
        /// </summary>
        public static bool IsChosungQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return false;
            foreach (var ch in query)
            {
                if (ch >= 'ㄱ' && ch <= 'ㅎ') continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 텍스트가 쿼리와 매칭되는지 확인 (대소문자 무시, 한글 초성 매칭 지원).
        /// 매칭 우선순위:
        ///  1) 정확 부분 문자열 (대소문자 무시)
        ///  2) 초성 쿼리인 경우 텍스트의 초성 시퀀스에서 부분 문자열
        ///  3) 영문 fuzzy 매칭 (순서대로 모든 글자 등장)
        /// </summary>
        public static bool Match(string text, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            var lowerText = text.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();

            // 1) 직접 부분 문자열
            if (lowerText.Contains(lowerQuery)) return true;

            // 2) 초성만 입력된 경우 → 텍스트의 초성 시퀀스에서 매칭
            if (IsChosungQuery(query))
            {
                var chosungOfText = ToChosungString(text);
                if (chosungOfText.Contains(query)) return true;
            }

            // 3) 한글 단어가 포함된 쿼리: 텍스트에 포함되어 있지 않으면 false
            //    (한글은 fuzzy 매칭 부적합)
            foreach (var ch in lowerQuery)
            {
                if (ch >= HangulBase && ch <= HangulEnd) return false;
                if (ch >= 'ㄱ' && ch <= 'ㅎ') return false;
            }

            // 4) 영문 fuzzy
            int qi = 0;
            foreach (var ch in lowerText)
            {
                if (qi < lowerQuery.Length && ch == lowerQuery[qi])
                    qi++;
            }
            return qi == lowerQuery.Length;
        }

        /// <summary>
        /// 매칭 점수 계산 (높을수록 우선).
        ///  - prefix: 100
        ///  - 초성 prefix: 90
        ///  - contains: 60
        ///  - 초성 contains: 50
        ///  - fuzzy: 10
        /// </summary>
        public static int Score(string text, string query)
        {
            if (string.IsNullOrEmpty(query)) return 0;
            if (string.IsNullOrEmpty(text)) return 0;

            var lowerText = text.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();

            if (lowerText.StartsWith(lowerQuery)) return 100;
            if (lowerText.Contains(lowerQuery)) return 60;

            if (IsChosungQuery(query))
            {
                var chosungOfText = ToChosungString(text);
                if (chosungOfText.StartsWith(query)) return 90;
                if (chosungOfText.Contains(query)) return 50;
            }

            // fuzzy 마지막
            return Match(text, query) ? 10 : 0;
        }
    }
}
