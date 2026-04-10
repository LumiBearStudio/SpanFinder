namespace Span.Models
{
    /// <summary>
    /// 탭 상태를 JSON으로 직렬화하기 위한 경량 DTO.
    /// MainViewModel.SaveTabsToJson / LoadTabsFromJson, WorkspaceService 등에서 사용.
    /// 순수 record로 WinUI 의존이 없어 단위 테스트 프로젝트(Span.Tests)에서 직접 링크된다.
    /// </summary>
    public record TabStateDto(string Id, string Header, string Path, int ViewMode, int IconSize);
}
