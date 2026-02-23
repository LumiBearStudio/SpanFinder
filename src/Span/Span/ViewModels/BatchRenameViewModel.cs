using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Span.ViewModels
{
    /// <summary>
    /// 배치 이름 변경 다이얼로그 ViewModel.
    /// 3가지 모드: 찾기/바꾸기, 접두사/접미사, 번호 매기기.
    /// </summary>
    public partial class BatchRenameViewModel : ObservableObject
    {
        public enum RenameMode { FindReplace, PrefixSuffix, Numbering }

        private readonly List<FileSystemViewModel> _items;

        [ObservableProperty]
        private RenameMode _selectedMode = RenameMode.FindReplace;

        // 찾기/바꾸기
        [ObservableProperty]
        private string _findText = string.Empty;

        [ObservableProperty]
        private string _replaceText = string.Empty;

        [ObservableProperty]
        private bool _caseSensitive = false;

        [ObservableProperty]
        private bool _useRegex = false;

        // 접두사/접미사
        [ObservableProperty]
        private string _prefix = string.Empty;

        [ObservableProperty]
        private string _suffix = string.Empty;

        // 번호 매기기
        [ObservableProperty]
        private string _numberingPattern = "{name}_{n}";

        [ObservableProperty]
        private int _startNumber = 1;

        [ObservableProperty]
        private int _increment = 1;

        [ObservableProperty]
        private int _digits = 2;

        /// <summary>미리보기 항목 목록</summary>
        public ObservableCollection<RenamePreviewItem> PreviewItems { get; } = new();

        /// <summary>충돌 여부</summary>
        public bool HasConflicts => PreviewItems.Any(p => p.HasConflict);

        /// <summary>변경 사항 존재 여부</summary>
        public bool HasChanges => PreviewItems.Any(p => p.NewName != p.OldName);

        public BatchRenameViewModel(List<FileSystemViewModel> items)
        {
            _items = items;
            UpdatePreview();
        }

        partial void OnSelectedModeChanged(RenameMode value) => UpdatePreview();
        partial void OnFindTextChanged(string value) => UpdatePreview();
        partial void OnReplaceTextChanged(string value) => UpdatePreview();
        partial void OnCaseSensitiveChanged(bool value) => UpdatePreview();
        partial void OnUseRegexChanged(bool value) => UpdatePreview();
        partial void OnPrefixChanged(string value) => UpdatePreview();
        partial void OnSuffixChanged(string value) => UpdatePreview();
        partial void OnNumberingPatternChanged(string value) => UpdatePreview();
        partial void OnStartNumberChanged(int value) => UpdatePreview();
        partial void OnIncrementChanged(int value) => UpdatePreview();
        partial void OnDigitsChanged(int value) => UpdatePreview();

        public void UpdatePreview()
        {
            PreviewItems.Clear();

            var newNames = new List<string>();

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                string oldName = item.Name;
                string newName = ApplyRename(oldName, i);
                newNames.Add(newName);
            }

            // 충돌 감지
            var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in newNames)
            {
                nameCount.TryGetValue(name, out var count);
                nameCount[name] = count + 1;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                var oldName = _items[i].Name;
                var newName = newNames[i];
                bool hasConflict = nameCount.TryGetValue(newName, out var cnt) && cnt > 1;

                PreviewItems.Add(new RenamePreviewItem
                {
                    OldName = oldName,
                    NewName = newName,
                    HasConflict = hasConflict,
                    IsChanged = !oldName.Equals(newName, StringComparison.Ordinal)
                });
            }

            OnPropertyChanged(nameof(HasConflicts));
            OnPropertyChanged(nameof(HasChanges));
        }

        private string ApplyRename(string fileName, int index)
        {
            return SelectedMode switch
            {
                RenameMode.FindReplace => ApplyFindReplace(fileName),
                RenameMode.PrefixSuffix => ApplyPrefixSuffix(fileName),
                RenameMode.Numbering => ApplyNumbering(fileName, index),
                _ => fileName
            };
        }

        private string ApplyFindReplace(string fileName)
        {
            if (string.IsNullOrEmpty(FindText)) return fileName;

            try
            {
                if (UseRegex)
                {
                    var options = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.Replace(fileName, FindText, ReplaceText ?? "", options);
                }
                else
                {
                    var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    // 모든 매칭 바꾸기
                    int pos = 0;
                    var result = fileName;
                    while (true)
                    {
                        int idx = result.IndexOf(FindText, pos, comparison);
                        if (idx < 0) break;
                        result = result.Remove(idx, FindText.Length).Insert(idx, ReplaceText ?? "");
                        pos = idx + (ReplaceText?.Length ?? 0);
                    }
                    return result;
                }
            }
            catch
            {
                return fileName; // regex 오류 시 원본 유지
            }
        }

        private string ApplyPrefixSuffix(string fileName)
        {
            var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);

            // 폴더인지 확인 (확장자 없음)
            bool isFolder = _items.Any(i => i.Name == fileName && i is FolderViewModel);
            if (isFolder)
                return (Prefix ?? "") + fileName + (Suffix ?? "");

            return (Prefix ?? "") + nameWithoutExt + (Suffix ?? "") + ext;
        }

        private string ApplyNumbering(string fileName, int index)
        {
            var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);

            int num = StartNumber + (index * Increment);
            string numStr = num.ToString().PadLeft(Digits, '0');

            var pattern = NumberingPattern ?? "{name}_{n}";
            var result = pattern
                .Replace("{name}", nameWithoutExt)
                .Replace("{n}", numStr)
                .Replace("{ext}", ext.TrimStart('.'));

            // 패턴에 {ext}가 없으면 확장자 추가
            bool isFolder = _items.Any(i => i.Name == fileName && i is FolderViewModel);
            if (!isFolder && !pattern.Contains("{ext}") && !string.IsNullOrEmpty(ext))
                result += ext;

            return result;
        }

        /// <summary>
        /// 실행할 (OldPath, NewName) 목록 반환. 변경 없는 항목은 제외.
        /// </summary>
        public List<(string OldPath, string NewName)> GetRenameList()
        {
            var result = new List<(string, string)>();
            for (int i = 0; i < _items.Count && i < PreviewItems.Count; i++)
            {
                var preview = PreviewItems[i];
                if (preview.IsChanged && !preview.HasConflict)
                {
                    result.Add((_items[i].Path, preview.NewName));
                }
            }
            return result;
        }
    }

    public class RenamePreviewItem
    {
        public string OldName { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public bool HasConflict { get; set; }
        public bool IsChanged { get; set; }
        public string Arrow => " → ";
    }
}
