using Microsoft.Data.Sqlite;

namespace Span.Thumbs;

/// <summary>
/// Issue #56: CLIP STUDIO PAINT(.clip) 파일에서 내장 미리보기 PNG를 추출.
///
/// .clip 파일 구조 (clean-room 리버스 엔지니어링 기준):
///   "CSFCHUNK"(8B) + 파일길이(8B, big-endian) + 첫 청크 오프셋(8B, big-endian)
///   이후 청크 반복 = 태그(8B) + 데이터길이(8B, big-endian) + 데이터
///   그중 "CHNKSQLi" 청크 = SQLite DB 통째로 임베드.
///   그 DB의 CanvasPreview.ImageData 컬럼 = CSP가 미리 렌더해 넣은 완성 PNG.
///
/// 셸 썸네일 핸들러(IThumbnailProvider)를 CSP가 등록하지 않으므로 Shell 경로로는
/// .clip 썸네일이 나오지 않는다. 이 추출기는 셸 핸들러 없이 전 사용자에게 동작한다.
///
/// 대용량(.clip은 수백MB~GB) 대비: 청크 헤더(16B)만 읽고 데이터는 Seek로 건너뛰며
/// 순회하므로 파일 전체를 메모리에 올리지 않는다. CHNKSQLi 청크(대개 수 MB)만 읽는다.
/// 손상/미지원 스키마/예외는 모두 null 반환 → 썸네일 없음(현상 유지). 격리 워커
/// (Span.Thumbs.exe)에서만 호출되므로 어떤 실패도 메인 앱에 영향을 주지 않는다.
/// </summary>
internal static class ClipThumbnailExtractor
{
    private static readonly byte[] Magic = "CSFCHUNK"u8.ToArray();
    private static readonly byte[] SqliTag = "CHNKSQLi"u8.ToArray();
    private static readonly byte[] FootTag = "CHNKFoot"u8.ToArray();

    // 방어 한계: SQLite 섹션이 이보다 크면 손상/비정상으로 간주하고 포기.
    private const long MaxSqliteBytes = 256L * 1024 * 1024; // 256MB

    /// <summary>.clip에서 CanvasPreview PNG 바이트를 추출. 실패 시 null.</summary>
    public static byte[]? TryExtractPreviewPng(string filePath, CancellationToken ct)
    {
        try
        {
            byte[]? sqliteBytes = ExtractSqliteSection(filePath, ct);
            if (sqliteBytes == null) return null;
            return ReadCanvasPreview(sqliteBytes, ct);
        }
        catch
        {
            return null; // 손상 파일 / 미지원 스키마 → 조용히 포기
        }
    }

    /// <summary>청크를 순회해 CHNKSQLi 청크의 SQLite 바이트를 떼어낸다.</summary>
    private static byte[]? ExtractSqliteSection(string filePath, CancellationToken ct)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> hdr = stackalloc byte[16];

        // CSFCHUNK 매직
        if (!ReadExactly(fs, hdr.Slice(0, 8)) || !hdr.Slice(0, 8).SequenceEqual(Magic))
            return null;

        // 파일길이(8B) skip → 첫 청크 오프셋(8B)
        if (!ReadExactly(fs, hdr.Slice(0, 8))) return null; // 파일길이 (미사용)
        if (!ReadExactly(fs, hdr.Slice(0, 8))) return null; // 첫 청크 오프셋
        long pos = ReadU64BE(hdr.Slice(0, 8));
        if (pos < 24 || pos >= fs.Length) return null;

        while (pos + 16 <= fs.Length)
        {
            ct.ThrowIfCancellationRequested();
            fs.Position = pos;
            if (!ReadExactly(fs, hdr)) return null;

            long len = ReadU64BE(hdr.Slice(8, 8));
            if (len < 0) return null; // 오버플로/손상

            if (hdr.Slice(0, 8).SequenceEqual(SqliTag))
            {
                if (len < 16 || len > MaxSqliteBytes) return null;
                var buf = new byte[len];
                if (!ReadExactly(fs, buf)) return null;
                // SQLite 파일 매직 검증 ("SQLite format 3\0")
                if (buf.Length < 16 || buf[0] != (byte)'S' || buf[1] != (byte)'Q' ||
                    buf[2] != (byte)'L' || buf[3] != (byte)'i')
                    return null;
                return buf;
            }

            if (hdr.Slice(0, 8).SequenceEqual(FootTag))
                return null; // CHNKSQLi 없이 파일 끝

            // 정상 청크 태그("CHNK...")가 아니면 손상으로 간주하고 중단
            if (hdr[0] != (byte)'C' || hdr[1] != (byte)'H' || hdr[2] != (byte)'N' || hdr[3] != (byte)'K')
                return null;

            pos = pos + 16 + len; // 다음 청크로 점프
        }
        return null;
    }

    /// <summary>추출한 SQLite 바이트를 열어 CanvasPreview.ImageData(PNG)를 읽는다.</summary>
    private static byte[]? ReadCanvasPreview(byte[] sqliteBytes, CancellationToken ct)
    {
        // Microsoft.Data.Sqlite는 파일 경로 기반 → 임시 파일에 쓰고 읽기 전용으로 연다.
        // (CanvasPreview 섹션은 대개 수 MB로 작다 — 레이어 원본은 CHNKExta에 별도 저장됨)
        string tmp = Path.Combine(Path.GetTempPath(), "span_clip_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            File.WriteAllBytes(tmp, sqliteBytes);
            ct.ThrowIfCancellationRequested();

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadOnly,
            };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ImageData FROM CanvasPreview WHERE ImageData IS NOT NULL LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0)) return null;

            using var blob = reader.GetStream(0);
            using var ms = new MemoryStream();
            blob.CopyTo(ms);
            var png = ms.ToArray();

            // PNG 시그니처 확인 (89 50 4E 47)
            if (png.Length < 8 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
                return null;
            return png;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 임시파일 정리 실패 무시 */ }
        }
    }

    private static bool ReadExactly(Stream s, Span<byte> buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = s.Read(buf.Slice(read));
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    /// <summary>big-endian 8바이트를 long으로. long 범위 초과 시 -1(손상 신호).</summary>
    private static long ReadU64BE(ReadOnlySpan<byte> b)
    {
        ulong v = ((ulong)b[0] << 56) | ((ulong)b[1] << 48) | ((ulong)b[2] << 40) | ((ulong)b[3] << 32)
                | ((ulong)b[4] << 24) | ((ulong)b[5] << 16) | ((ulong)b[6] << 8) | b[7];
        return v > long.MaxValue ? -1 : (long)v;
    }
}
