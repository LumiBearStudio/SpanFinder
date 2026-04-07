using System;
using System.Collections.Generic;
using System.Text;

namespace Span.Helpers
{
    /// <summary>
    /// 경량 CSV/TSV 파서. 외부 의존 없이 RFC 4180 기본 규격을 처리한다.
    /// 따옴표로 감싼 필드 내 구분자, 줄바꿈, 이스케이프된 따옴표("") 지원.
    /// QuickLook 테이블 미리보기용 (최대 200행 제한).
    /// </summary>
    public static class CsvHelper
    {
        private const int MaxPreviewRows = 200;

        /// <summary>
        /// CSV/TSV 텍스트를 파싱하여 헤더와 데이터 행을 반환한다.
        /// </summary>
        public static (string[] headers, List<string[]> rows) Parse(string text, char delimiter = ',')
        {
            if (string.IsNullOrEmpty(text))
                return (Array.Empty<string>(), new List<string[]>());

            var allRows = ParseRows(text, delimiter, MaxPreviewRows + 1);

            if (allRows.Count == 0)
                return (Array.Empty<string>(), new List<string[]>());

            var headers = allRows[0];
            var rows = allRows.Count > 1 ? allRows.GetRange(1, Math.Min(allRows.Count - 1, MaxPreviewRows)) : new List<string[]>();

            return (headers, rows);
        }

        private static List<string[]> ParseRows(string text, char delimiter, int maxRows)
        {
            var result = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < text.Length && result.Count < maxRows)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Escaped quote ("") or end of quoted field
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                        }
                        else
                        {
                            inQuotes = false;
                            i++;
                        }
                    }
                    else
                    {
                        field.Append(c);
                        i++;
                    }
                }
                else
                {
                    if (c == '"' && field.Length == 0)
                    {
                        inQuotes = true;
                        i++;
                    }
                    else if (c == delimiter)
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        i++;
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        if (fields.Count > 0 && !(fields.Count == 1 && string.IsNullOrEmpty(fields[0])))
                            result.Add(fields.ToArray());
                        fields.Clear();
                        // Skip \r\n
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                            i += 2;
                        else
                            i++;
                    }
                    else
                    {
                        field.Append(c);
                        i++;
                    }
                }
            }

            // Last field/row
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                if (fields.Count > 0 && !(fields.Count == 1 && string.IsNullOrEmpty(fields[0])))
                    result.Add(fields.ToArray());
            }

            return result;
        }
    }
}
