using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class FolderContextLauncherTests
{
    // ── IsCandidatePath: 경로 형태만 (디스크·레지스트리 접근 없음) ──

    [DataTestMethod]
    [DataRow(@"C:\Photos\a.jpg")]
    [DataRow(@"D:\사진 폴더 #1\사진 1.JPG")]
    [DataRow(@"E:/camera/IMG_0001.heic")]
    [DataRow(@"C:\raw\DSC0001.ARW")]
    public void Candidate_LocalImage_True(string path)
        => Assert.IsTrue(FolderContextLauncher.IsCandidatePath(path));

    [DataTestMethod]
    [DataRow(@"\server\share\a.jpg")]          // UNC
    [DataRow(@"archive://D:\a.zip/dir/b.jpg")]  // 압축 내부
    [DataRow("sftp://user@host:22/home/a.jpg")] // 원격
    [DataRow(@"C:\docs\a.txt")]                 // 이미지 아님
    [DataRow(@"C:\videos\a.mp4")]               // 1차 범위 밖
    [DataRow(@"C:\Photos\a.jpg.lnk")]           // 바로가기
    [DataRow(@"a.jpg")]                         // 상대 경로
    [DataRow("")]
    [DataRow(null)]
    public void Candidate_Others_False(string? path)
        => Assert.IsFalse(FolderContextLauncher.IsCandidatePath(path));

    // ── BuildSortColumns: SPAN 정렬 → Windows 속성 정렬 ──

    [TestMethod]
    public void Sort_NameAscending()
    {
        var s = FolderContextLauncher.BuildSortColumns("Name", true);
        Assert.AreEqual(1, s.Length);
        Assert.AreEqual(10u, s[0].Key.PropertyId); // PKEY_ItemNameDisplay
        Assert.AreEqual(1, s[0].Direction);
    }

    [TestMethod]
    public void Sort_DateModifiedDescending()
    {
        var s = FolderContextLauncher.BuildSortColumns("DateModified", false);
        Assert.AreEqual(14u, s[0].Key.PropertyId); // PKEY_DateModified
        Assert.AreEqual(-1, s[0].Direction);
    }

    [TestMethod]
    public void Sort_SizeAndType()
    {
        Assert.AreEqual(12u, FolderContextLauncher.BuildSortColumns("Size", true)[0].Key.PropertyId);
        Assert.AreEqual(4u, FolderContextLauncher.BuildSortColumns("Type", true)[0].Key.PropertyId);
    }

    [TestMethod]
    public void Sort_Unknown_NoSortColumns()
    {
        Assert.AreEqual(0, FolderContextLauncher.BuildSortColumns(null, true).Length);
        Assert.AreEqual(0, FolderContextLauncher.BuildSortColumns("Whatever", true).Length);
    }

    // ── DetectDisplayedSort: 화면 순서와 맞는 정렬 판별 ──

    private static FolderContextLauncher.SortProbe P(string name, int hour, long size = 1, string type = "JPG")
        => new(name, new DateTime(2026, 10, 1, hour, 0, 0), size, type);

    [TestMethod]
    public void Detect_PreferredMatches_KeepsPreferred()
    {
        var files = new[] { P("img1.jpg", 1), P("img2.jpg", 2), P("img10.jpg", 3) };
        Assert.AreEqual(("Name", true), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
        // 이름순이면서 날짜순이기도 하지만, 폴더 상태(DateModified)가 맞으면 그것
        Assert.AreEqual(("DateModified", true), FolderContextLauncher.DetectDisplayedSort(files, "DateModified", true));
    }

    [TestMethod]
    public void Detect_DetailsDateDescending_WhileFolderSaysName()
    {
        // Details 보기에서 날짜 내림차순으로 바꾼 상태 — 폴더 정렬 상태는 이름 오름차순 그대로
        var files = new[] { P("b.jpg", 5), P("c.jpg", 4), P("a.jpg", 3) };
        Assert.AreEqual(("DateModified", false), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void Detect_NameDescending()
    {
        var files = new[] { P("img10.jpg", 1), P("img2.jpg", 3), P("img1.jpg", 2) };
        Assert.AreEqual(("Name", false), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void Detect_SizeAscending()
    {
        var files = new[] { P("c.jpg", 1, 10), P("a.jpg", 3, 20), P("b.jpg", 2, 30) };
        Assert.AreEqual(("Size", true), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void Detect_NoMatch_FallsBackToPreferred()
    {
        // 이름·날짜·크기·종류 어느 것으로도 설명되지 않는 순서
        var files = new[] { P("b.jpg", 2, 30, "JPG"), P("a.jpg", 3, 10, "PNG"), P("c.jpg", 1, 20, "GIF") };
        Assert.AreEqual(("Name", true), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void Detect_SingleFile_Preferred()
        => Assert.AreEqual(("DateModified", false),
            FolderContextLauncher.DetectDisplayedSort(new[] { P("a.jpg", 1) }, "DateModified", false));

    [TestMethod]
    public void Detect_AllSameType_DoesNotClaimTypeSort()
    {
        // 모두 JPG — 종류순은 판별력이 없으므로 크기순(실제 순서)을 골라야 한다
        var files = new[] { P("c.jpg", 2, 10), P("a.jpg", 3, 20), P("b.jpg", 1, 30) };
        Assert.AreEqual(("Size", true), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void Detect_TiesAreAllowed()
    {
        // 같은 수정 시각(복사본 등)은 순서를 따지지 않는다
        var files = new[] { P("z.jpg", 5), P("a.jpg", 5), P("m.jpg", 4) };
        Assert.AreEqual(("DateModified", false), FolderContextLauncher.DetectDisplayedSort(files, "Name", true));
    }

    [TestMethod]
    public void SortColumn_MatchesNativeLayout()
    {
        // SORTCOLUMN = PROPERTYKEY(16+4) + int = 24 bytes, PROPERTYKEY = 20 bytes
        Assert.AreEqual(24, System.Runtime.InteropServices.Marshal.SizeOf<SortColumn>());
        Assert.AreEqual(20, System.Runtime.InteropServices.Marshal.SizeOf<PropertyKey>());
    }
}
