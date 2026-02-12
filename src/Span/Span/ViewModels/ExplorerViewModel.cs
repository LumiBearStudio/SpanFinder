using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Span.Models;
using Span.Services;

namespace Span.ViewModels
{
    public partial class ExplorerViewModel : ObservableObject
    {
        // Columns for Miller View
        public ObservableCollection<FolderViewModel> Columns { get; } = new();

        // 브레드크럼 세그먼트 (주소 표시줄)
        public ObservableCollection<PathSegment> PathSegments { get; } = new();

        // Current active path (for address bar)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentFolderName))]
        private string _currentPath = string.Empty;

        public string CurrentFolderName => System.IO.Path.GetFileName(CurrentPath) is string s && !string.IsNullOrEmpty(s) ? s : CurrentPath;

        private readonly FileSystemService _fileService;

        public ExplorerViewModel(FolderItem rootItem, FileSystemService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// CurrentPath 변경 시 PathSegments를 자동 갱신.
        /// </summary>
        partial void OnCurrentPathChanged(string value)
        {
            UpdatePathSegments(value);
        }

        private void UpdatePathSegments(string path)
        {
            PathSegments.Clear();
            if (string.IsNullOrWhiteSpace(path)) return;

            var parts = path.Split(System.IO.Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);
            string accumulated = string.Empty;

            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0 && parts[i].EndsWith(":"))
                {
                    accumulated = parts[i] + "\\";
                }
                else
                {
                    accumulated = System.IO.Path.Combine(accumulated, parts[i]);
                }

                PathSegments.Add(new PathSegment(parts[i], accumulated, isLast: i == parts.Length - 1));
            }
        }

        /// <summary>
        /// Navigate to a folder from sidebar (reset all columns).
        /// </summary>
        public async void NavigateTo(FolderItem folder)
        {
            // Cleanup all
            foreach (var col in Columns)
                col.PropertyChanged -= FolderVm_PropertyChanged;
            Columns.Clear();

            var rootVm = new FolderViewModel(folder, _fileService);
            await rootVm.EnsureChildrenLoadedAsync();

            AddColumn(rootVm);
            CurrentPath = rootVm.Path;
        }

        /// <summary>
        /// 문자열 경로로 직접 탐색 (주소 표시줄 편집, 브레드크럼 클릭).
        /// </summary>
        public async void NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!System.IO.Directory.Exists(path)) return;

            var folderItem = new FolderItem { Name = System.IO.Path.GetFileName(path), Path = path };
            NavigateTo(folderItem);
        }

        /// <summary>
        /// 브레드크럼 세그먼트 클릭 시 해당 경로까지 탐색.
        /// Finder 스타일: 이미 열려있는 컬럼 내의 경로라면 컬럼을 유지하고 하위만 정리.
        /// </summary>
        public void NavigateToSegment(PathSegment segment)
        {
            if (segment == null) return;

            // 1. 현재 컬럼들 중에서 해당 경로와 일치하는 폴더가 있는지 확인
            //    (마지막 컬럼은 선택된 '파일'이 뷰모델일 수도 있으므로, 폴더인 것들만 비교)
            int index = -1;
            for (int i = 0; i < Columns.Count; i++)
            {
                // 대소문자 무시하고 경로 비교
                if (string.Equals(Columns[i].Path, segment.FullPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                // 2. 일치하는 컬럼이 발견되면, 그 이후의 컬럼들을 모두 제거 (Truncate)
                //    여기서는 "해당 폴더를 선택한 상태"가 되어야 함.
                //    ExplorerViewModel 로직상, RemoveColumnsFrom(index + 1)를 하면
                //    Columns[0..index]는 남고, 그 뒤가 사라짐.
                //    그리고 CurrentPath를 갱신.

                // 만약 현재 마지막 컬럼(이미 선택된 끝점)과 같다면 아무것도 안 해도 됨 (단, CurrentPath는 보장)
                if (index == Columns.Count - 1 && CurrentPath.Equals(segment.FullPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                RemoveColumnsFrom(index + 1);

                // 선택된 폴더의 SelectedChild를 null로 초기화 해야 하위가 안보임? 
                // 아니, NavigateTo 로직상 보통 부모에서 얘를 선택한 상태여야 하는데...
                // 브레드크럼 클릭은 "그 폴더로 이동" 이므로, 그 폴더의 내용을 보여주는게 목적이 아니라
                // 그 폴더가 "선택된 상태" (= 그 폴더의 내용이 다음 컬럼에 나와야 함?)
                // 아니면 "그 폴더가 루트/현재위치"가 되는 것?

                // Finder 동작: A > B > C 클릭 시:
                // A, B, C 컬럼이 보이고, C가 'Active' 상태. C의 내용물은 다음 컬럼(아직 선택안함)에 표시될 준비.
                // 즉 C로 이동.

                CurrentPath = segment.FullPath;

                // UI 갱신을 위해 PropertyChanged 알림이 필요할 수 있음.
                // RemoveColumnsFrom 내부에서 CollectionChanged가 발생하므로 UI는 줄어듦.
            }
            else
            {
                // 3. 컬럼에 없다면 (완전히 다른 경로로 점프하는 경우) 기존 방식대로 전체 이동
                NavigateToPath(segment.FullPath);
            }
        }

        public void SetActiveColumn(FolderViewModel activeVm)
        {
            foreach (var col in Columns)
            {
                col.IsActive = (col == activeVm);
            }
        }

        private void AddColumn(FolderViewModel folderVm)
        {
            folderVm.PropertyChanged += FolderVm_PropertyChanged;
            Columns.Add(folderVm);
        }

        /// <summary>
        /// Remove columns from index+1 onwards (keep columns[0..index]).
        /// </summary>
        private void RemoveColumnsFrom(int startIndex)
        {
            for (int i = Columns.Count - 1; i >= startIndex; i--)
            {
                Columns[i].PropertyChanged -= FolderVm_PropertyChanged;
                Columns.RemoveAt(i);
            }
        }

        private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
            if (sender is not FolderViewModel parentFolder) return;

            int parentIndex = Columns.IndexOf(parentFolder);
            if (parentIndex == -1) return;

            int nextIndex = parentIndex + 1;

            if (parentFolder.SelectedChild is FolderViewModel selectedFolder)
            {
                // === Folder selected ===

                // 1. Load children of the selected folder
                await selectedFolder.EnsureChildrenLoadedAsync();

                // 2. Remove columns from nextIndex+1 onwards (keep slot for N+1)
                RemoveColumnsFrom(nextIndex + 1);

                // 3. Replace or add column at nextIndex
                if (nextIndex < Columns.Count)
                {
                    // REPLACE in-place: unsubscribe old, swap, subscribe new
                    Columns[nextIndex].PropertyChanged -= FolderVm_PropertyChanged;
                    selectedFolder.PropertyChanged += FolderVm_PropertyChanged;
                    Columns[nextIndex] = selectedFolder;  // Triggers Replace notification (no remove+add jitter)
                }
                else
                {
                    // ADD new column
                    AddColumn(selectedFolder);
                }

                // 4. Update path
                CurrentPath = selectedFolder.Path;
            }
            else if (parentFolder.SelectedChild is FileViewModel fileVm)
            {
                // === File selected: remove all columns after parent ===
                RemoveColumnsFrom(nextIndex);
                CurrentPath = fileVm.Path;
            }
            else
            {
                // === Selection cleared ===
                RemoveColumnsFrom(nextIndex);
                CurrentPath = parentFolder.Path;
            }
        }
    }
}
