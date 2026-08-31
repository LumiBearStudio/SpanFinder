namespace Span.Thumbs;

/// <summary>
/// Issue #56: Photoshop(.psd) 파일에서 내장 미리보기 JPEG를 추출.
///
/// 최신 Photoshop은 Windows에 셸 썸네일 핸들러(IThumbnailProvider)를 등록하지 않는다.
/// 실제로 확인: HKCR\.psd 에 {E357FCCD-A995-4576-B01F-234630154E96} 등록 없음 →
/// 탐색기 자체도 .psd 썸네일을 못 만든다. 즉 셸 경로로는 대다수 사용자에게 실패한다.
/// 이 추출기는 셸 핸들러 없이 전 사용자에게 동작한다.
///
/// .psd 구조 (Adobe 공식 파일 포맷 스펙 기준):
///   "8BPS"(4B) + 버전(2B, 1=PSD/2=PSB) + 예약(6B) + 채널(2B) + 높이(4B) + 너비(4B)
///   + 심도(2B) + 컬러모드(2B)                                    ... 여기까지 26B 고정 헤더
///   + ColorModeData 길이(4B) + 데이터
///   + ImageResources 길이(4B) + 이미지 리소스 블록(IRB) 반복
///   IRB = "8BIM"(4B) + ID(2B) + 파스칼 문자열 이름(짝수 패딩) + 길이(4B) + 데이터(짝수 패딩)
///   그중 ID 1036 = Photoshop 5.0+ 썸네일 리소스 = 28B 헤더 + JPEG 원본 바이트.
///
/// 병합 컴포지트(파일 끝의 RLE 이미지 데이터)는 파싱하지 않는다 — SPAN은 썸네일을 항상
/// 96px로 요청하는데(FileViewModel.LoadThumbnailAsync 기본값) IRB 1036 썸네일은 긴 변이
/// 통상 128~160px라 언제나 "축소"만 일어나 화질 손해가 없기 때문이다.
/// ※ 요청 크기를 96px보다 크게 바꾸는 날에는 이 가정이 깨진다 — 그때는 컴포지트 폴백 필요.
///
/// 대용량(.psd는 수백MB) 대비: 고정 헤더와 IRB 헤더만 읽고 데이터는 Seek로 건너뛰며
/// 순회하므로 파일 전체를 메모리에 올리지 않는다. 썸네일 IRB(대개 수 KB)만 읽는다.
/// 손상/미지원 스키마/예외는 모두 null 반환 → 호출자가 셸 경로로 폴백. 격리 워커
/// (Span.Thumbs.exe)에서만 호출되므로 어떤 실패도 메인 앱에 영향을 주지 않는다.
/// </summary>
internal static class PsdThumbnailExtractor
{
    private static readonly byte[] Magic = "8BPS"u8.ToArray();
    private static readonly byte[] IrbTag = "8BIM"u8.ToArray();

    // Photoshop 5.0+ 썸네일 리소스. 1033(4.0판)은 JPEG 데이터가 BGR 순서라 색이 뒤바뀌므로
    // 의도적으로 쓰지 않는다 — 없으면 셸 폴백이 낫다.
    private const ushort ThumbnailResourceId = 1036;

    // IRB 1036 고정 헤더: format(4) width(4) height(4) widthBytes(4) totalSize(4)
    //                     compressedSize(4) bitsPerPixel(2) planes(2)
    private const int ThumbHeaderSize = 28;
    private const uint FormatJpegRgb = 1; // 0 = kRawRGB(미지원), 1 = kJpegRGB

    // 방어 한계: 정상 썸네일은 수 KB~수십 KB다. 이보다 크면 손상으로 간주하고 포기.
    private const long MaxThumbBytes = 16L * 1024 * 1024;   // 16MB
    // 이미지 리소스 섹션 전체 한계 (XMP/ICC 포함해도 통상 수 MB).
    private const long MaxResourcesBytes = 256L * 1024 * 1024; // 256MB

    /// <summary>.psd에서 내장 썸네일 JPEG 바이트를 추출. 실패 시 null.</summary>
    public static byte[]? TryExtractPreviewImage(string filePath, CancellationToken ct)
    {
        try
        {
            byte[]? jpeg = Extract(filePath, ct, out string stage);
            if (jpeg == null)
                WorkerLogger.Log($"[PsdExtract] FAIL ({stage}): {System.IO.Path.GetFileName(filePath)}");
            return jpeg;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            WorkerLogger.Log($"[PsdExtract] EXCEPTION {ex.GetType().Name}: {ex.Message} — {System.IO.Path.GetFileName(filePath)}");
            return null; // 손상 파일 / 미지원 스키마 → 셸 폴백
        }
    }

    /// <summary>이미지 리소스 블록을 순회해 ID 1036의 JPEG를 떼어낸다. stage에 실패 단계 기록.</summary>
    private static byte[]? Extract(string filePath, CancellationToken ct, out string stage)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> buf = stackalloc byte[ThumbHeaderSize];

        // ── 고정 헤더 26B ──
        if (!ReadExactly(fs, buf.Slice(0, 26)))
        {
            stage = "eof-in-header";
            return null;
        }
        if (!buf.Slice(0, 4).SequenceEqual(Magic))
        {
            stage = $"no-8BPS-magic (head={ToAscii(buf.Slice(0, 4))})";
            return null;
        }
        ushort version = ReadU16BE(buf.Slice(4, 2));
        if (version != 1 && version != 2) // 1=PSD, 2=PSB — IRB 레이아웃은 동일
        {
            stage = $"unsupported-version ({version})";
            return null;
        }

        // ── ColorModeData: 길이(4B) 읽고 통째로 건너뜀 ──
        if (!ReadExactly(fs, buf.Slice(0, 4))) { stage = "eof-at-colormode-len"; return null; }
        long colorModeLen = ReadU32BE(buf.Slice(0, 4));
        long pos = 26 + 4 + colorModeLen;
        if (pos < 0 || pos + 4 > fs.Length) { stage = $"colormode-len-out-of-range ({colorModeLen})"; return null; }

        // ── ImageResources: 길이(4B) + IRB 반복 ──
        fs.Position = pos;
        if (!ReadExactly(fs, buf.Slice(0, 4))) { stage = "eof-at-resources-len"; return null; }
        long resourcesLen = ReadU32BE(buf.Slice(0, 4));
        if (resourcesLen <= 0 || resourcesLen > MaxResourcesBytes)
        {
            stage = $"resources-len-out-of-range ({resourcesLen})";
            return null;
        }
        pos += 4;
        long resourcesEnd = pos + resourcesLen;
        if (resourcesEnd > fs.Length) { stage = $"resources-past-eof ({resourcesEnd} > {fs.Length})"; return null; }

        // 진단용: 어떤 리소스가 들어있는지 (Photoshop 2026 등 신버전 형태 파악에 필요)
        var seenIds = new List<ushort>();

        while (pos + 12 <= resourcesEnd) // 최소 IRB 크기: 4+2+2+4
        {
            ct.ThrowIfCancellationRequested();
            fs.Position = pos;

            if (!ReadExactly(fs, buf.Slice(0, 6))) { stage = $"eof-in-irb-walk (pos={pos})"; return null; }
            if (!buf.Slice(0, 4).SequenceEqual(IrbTag))
            {
                stage = $"bad-irb-signature ({ToAscii(buf.Slice(0, 4))} at {pos}, seen={FormatIds(seenIds)})";
                return null;
            }
            ushort id = ReadU16BE(buf.Slice(4, 2));
            pos += 6;

            // 파스칼 문자열 이름: 길이 1B + 내용, 전체가 짝수가 되도록 패딩
            fs.Position = pos;
            if (!ReadExactly(fs, buf.Slice(0, 1))) { stage = $"eof-at-irb-name (pos={pos})"; return null; }
            int nameLen = buf[0];
            int nameField = 1 + nameLen;
            if ((nameField & 1) != 0) nameField++;
            pos += nameField;

            if (pos + 4 > resourcesEnd) { stage = $"eof-at-irb-len (pos={pos})"; return null; }
            fs.Position = pos;
            if (!ReadExactly(fs, buf.Slice(0, 4))) { stage = $"eof-at-irb-len (pos={pos})"; return null; }
            long dataLen = ReadU32BE(buf.Slice(0, 4));
            pos += 4;
            if (dataLen < 0 || pos + dataLen > resourcesEnd)
            {
                stage = $"irb-len-out-of-range (id={id}, len={dataLen})";
                return null;
            }

            if (seenIds.Count < 64) seenIds.Add(id);

            if (id == ThumbnailResourceId)
            {
                if (dataLen <= ThumbHeaderSize || dataLen > MaxThumbBytes)
                {
                    stage = $"irb1036-len-out-of-range ({dataLen})";
                    return null;
                }
                fs.Position = pos;
                if (!ReadExactly(fs, buf.Slice(0, ThumbHeaderSize))) { stage = "eof-in-irb1036-header"; return null; }
                long format = ReadU32BE(buf.Slice(0, 4));
                long tw = ReadU32BE(buf.Slice(4, 4));
                long th = ReadU32BE(buf.Slice(8, 4));
                if (format != FormatJpegRgb)
                {
                    // kRawRGB — 실사용에서 거의 없고 별도 디코딩이 필요하다. 셸 폴백이 낫다.
                    stage = $"irb1036-not-jpeg-format (fmt={format}, {tw}x{th})";
                    return null;
                }

                var jpeg = new byte[dataLen - ThumbHeaderSize];
                if (!ReadExactly(fs, jpeg)) { stage = "eof-in-irb1036-data"; return null; }

                // JPEG SOI 검증 (FF D8)
                if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
                {
                    stage = $"irb1036-not-jpeg (len={jpeg.Length}, sig={(jpeg.Length >= 4 ? Convert.ToHexString(jpeg.AsSpan(0, 4)) : "short")})";
                    return null;
                }

                stage = "ok";
                return jpeg;
            }

            pos += dataLen;
            if ((dataLen & 1) != 0) pos++; // 데이터도 짝수 패딩
        }

        stage = $"no-thumbnail-irb (ids={FormatIds(seenIds)})";
        return null;
    }

    private static string FormatIds(List<ushort> ids) =>
        ids.Count == 0 ? "none" : string.Join(',', ids);

    private static string ToAscii(ReadOnlySpan<byte> b)
    {
        var sb = new System.Text.StringBuilder(b.Length);
        foreach (var c in b) sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '?');
        return sb.ToString();
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

    private static ushort ReadU16BE(ReadOnlySpan<byte> b) => (ushort)((b[0] << 8) | b[1]);

    /// <summary>big-endian 4바이트를 long으로 (부호 없는 값을 그대로 담기 위해 long 사용).</summary>
    private static long ReadU32BE(ReadOnlySpan<byte> b) =>
        ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
}
