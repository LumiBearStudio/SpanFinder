using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Span.Helpers
{
    /// <summary>
    /// 경량 마크다운 → HTML 변환기.
    /// 외부 라이브러리 의존 없이 기본적인 마크다운 문법을 처리한다.
    /// 지원: 헤더, 볼드/이탈릭, 코드블록, 인라인코드, 목록, 링크, 이미지, 인용, 수평선, 테이블.
    /// </summary>
    public static class MarkdownHelper
    {
        public static string ToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            bool inCodeBlock = false;
            bool inList = false;
            bool inOrderedList = false;
            bool inBlockquote = false;
            bool inTable = false;
            string codeBlockLang = "";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // === Fenced code block ===
                if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                {
                    if (inCodeBlock)
                    {
                        sb.AppendLine("</code></pre>");
                        inCodeBlock = false;
                        continue;
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeBlockLang = line.TrimStart().TrimStart('`', '~').Trim();
                        var langAttr = !string.IsNullOrEmpty(codeBlockLang)
                            ? $" class=\"language-{Escape(codeBlockLang)}\""
                            : "";
                        sb.AppendLine($"<pre><code{langAttr}>");
                        continue;
                    }
                }

                if (inCodeBlock)
                {
                    sb.AppendLine(Escape(line));
                    continue;
                }

                // Close open lists/blockquote if line doesn't continue them
                var trimmed = line.TrimStart();

                // === Table ===
                if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                {
                    // Check if separator row
                    if (Regex.IsMatch(trimmed, @"^\|[\s\-:|]+\|$"))
                        continue; // skip separator

                    if (!inTable)
                    {
                        CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                        sb.AppendLine("<table>");
                        inTable = true;
                        // First row = header
                        var cells = ParseTableCells(trimmed);
                        sb.Append("<tr>");
                        foreach (var cell in cells)
                            sb.Append($"<th>{InlineFormat(cell.Trim())}</th>");
                        sb.AppendLine("</tr>");
                        continue;
                    }
                    else
                    {
                        var cells = ParseTableCells(trimmed);
                        sb.Append("<tr>");
                        foreach (var cell in cells)
                            sb.Append($"<td>{InlineFormat(cell.Trim())}</td>");
                        sb.AppendLine("</tr>");
                        continue;
                    }
                }
                else if (inTable)
                {
                    sb.AppendLine("</table>");
                    inTable = false;
                }

                // === Blank line ===
                if (string.IsNullOrWhiteSpace(line))
                {
                    CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                    continue;
                }

                // === Horizontal rule ===
                if (Regex.IsMatch(trimmed, @"^[-*_]{3,}\s*$"))
                {
                    CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                    sb.AppendLine("<hr/>");
                    continue;
                }

                // === Headers ===
                var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                if (headerMatch.Success)
                {
                    CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                    int level = headerMatch.Groups[1].Length;
                    sb.AppendLine($"<h{level}>{InlineFormat(headerMatch.Groups[2].Value.TrimEnd('#', ' '))}</h{level}>");
                    continue;
                }

                // === Blockquote ===
                if (trimmed.StartsWith(">"))
                {
                    if (!inBlockquote)
                    {
                        CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                        sb.AppendLine("<blockquote>");
                        inBlockquote = true;
                    }
                    var content = trimmed.Length > 1 ? trimmed[1..].TrimStart() : "";
                    sb.AppendLine($"<p>{InlineFormat(content)}</p>");
                    continue;
                }

                // === Unordered list ===
                if (Regex.IsMatch(trimmed, @"^[-*+]\s+"))
                {
                    if (inOrderedList) { sb.AppendLine("</ol>"); inOrderedList = false; }
                    if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                    var content = Regex.Replace(trimmed, @"^[-*+]\s+", "");
                    sb.AppendLine($"<li>{InlineFormat(content)}</li>");
                    continue;
                }

                // === Ordered list ===
                if (Regex.IsMatch(trimmed, @"^\d+\.\s+"))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    if (!inOrderedList) { sb.AppendLine("<ol>"); inOrderedList = true; }
                    var content = Regex.Replace(trimmed, @"^\d+\.\s+", "");
                    sb.AppendLine($"<li>{InlineFormat(content)}</li>");
                    continue;
                }

                // === Paragraph ===
                CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);
                sb.AppendLine($"<p>{InlineFormat(line)}</p>");
            }

            // Close any open blocks
            if (inCodeBlock) sb.AppendLine("</code></pre>");
            if (inTable) sb.AppendLine("</table>");
            CloseOpenBlocks(sb, ref inList, ref inOrderedList, ref inBlockquote);

            return sb.ToString();
        }

        private static void CloseOpenBlocks(StringBuilder sb, ref bool inList, ref bool inOrderedList, ref bool inBlockquote)
        {
            if (inList) { sb.AppendLine("</ul>"); inList = false; }
            if (inOrderedList) { sb.AppendLine("</ol>"); inOrderedList = false; }
            if (inBlockquote) { sb.AppendLine("</blockquote>"); inBlockquote = false; }
        }

        /// <summary>인라인 서식 변환 (bold, italic, code, link, image, strikethrough).</summary>
        private static string InlineFormat(string text)
        {
            text = Escape(text);

            // Image: ![alt](url)
            text = Regex.Replace(text, @"!\[([^\]]*)\]\(([^)]+)\)", "<img src=\"$2\" alt=\"$1\" style=\"max-width:100%\"/>");
            // Link: [text](url)
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
            // Bold+Italic: ***text*** or ___text___
            text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
            text = Regex.Replace(text, @"___(.+?)___", "<strong><em>$1</em></strong>");
            // Bold: **text** or __text__
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__(.+?)__", "<strong>$1</strong>");
            // Italic: *text* or _text_
            text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
            text = Regex.Replace(text, @"(?<!\w)_(.+?)_(?!\w)", "<em>$1</em>");
            // Strikethrough: ~~text~~
            text = Regex.Replace(text, @"~~(.+?)~~", "<del>$1</del>");
            // Inline code: `code`
            text = Regex.Replace(text, @"`([^`]+)`", "<code>$1</code>");

            return text;
        }

        private static string Escape(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string[] ParseTableCells(string line)
        {
            // Remove leading/trailing pipes and split
            var inner = line.Trim().Trim('|');
            return inner.Split('|');
        }

        /// <summary>다크/라이트 테마에 맞는 전체 HTML 문서를 생성한다.</summary>
        public static string WrapInHtmlDocument(string bodyHtml, bool isDark)
        {
            var bg = isDark ? "#1e1e1e" : "#ffffff";
            var fg = isDark ? "#d4d4d4" : "#1e1e1e";
            var codeBg = isDark ? "#2d2d2d" : "#f5f5f5";
            var borderColor = isDark ? "#404040" : "#e0e0e0";
            var linkColor = isDark ? "#4fc3f7" : "#0366d6";
            var blockquoteBorder = isDark ? "#555" : "#ddd";
            var thBg = isDark ? "#2a2a2a" : "#f0f0f0";

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  body {{
    font-family: -apple-system, 'Segoe UI', sans-serif;
    font-size: 14px;
    line-height: 1.6;
    color: {fg};
    background: {bg};
    padding: 16px 20px;
    margin: 0;
    word-wrap: break-word;
  }}
  h1 {{ font-size: 1.8em; border-bottom: 1px solid {borderColor}; padding-bottom: 6px; margin-top: 20px; }}
  h2 {{ font-size: 1.5em; border-bottom: 1px solid {borderColor}; padding-bottom: 4px; margin-top: 18px; }}
  h3 {{ font-size: 1.25em; margin-top: 16px; }}
  h4, h5, h6 {{ margin-top: 14px; }}
  code {{
    font-family: 'Cascadia Mono', Consolas, monospace;
    background: {codeBg};
    padding: 2px 5px;
    border-radius: 3px;
    font-size: 0.9em;
  }}
  pre {{
    background: {codeBg};
    padding: 12px;
    border-radius: 6px;
    overflow-x: auto;
    border: 1px solid {borderColor};
  }}
  pre code {{
    background: none;
    padding: 0;
  }}
  blockquote {{
    margin: 8px 0;
    padding: 4px 16px;
    border-left: 4px solid {blockquoteBorder};
    color: {(isDark ? "#aaa" : "#666")};
  }}
  blockquote p {{ margin: 4px 0; }}
  a {{ color: {linkColor}; text-decoration: none; }}
  a:hover {{ text-decoration: underline; }}
  hr {{ border: none; border-top: 1px solid {borderColor}; margin: 16px 0; }}
  table {{
    border-collapse: collapse;
    width: 100%;
    margin: 12px 0;
  }}
  th, td {{
    border: 1px solid {borderColor};
    padding: 6px 10px;
    text-align: left;
  }}
  th {{ background: {thBg}; font-weight: 600; }}
  img {{ max-width: 100%; }}
  ul, ol {{ padding-left: 24px; }}
  li {{ margin: 2px 0; }}
  del {{ opacity: 0.6; }}
</style>
</head>
<body>
{bodyHtml}
</body>
</html>";
        }
    }
}
