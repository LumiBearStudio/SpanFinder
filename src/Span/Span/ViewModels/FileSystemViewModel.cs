using CommunityToolkit.Mvvm.ComponentModel;
using Span.Models;

namespace Span.ViewModels
{
    public partial class FileSystemViewModel : ObservableObject
    {
        protected readonly IFileSystemItem _model;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isRenaming;

        [ObservableProperty]
        private string _editableName = string.Empty;

        public string Name => _model.Name;
        public string Path => _model.Path;
        public virtual string IconGlyph => _model.IconGlyph;
        public virtual Microsoft.UI.Xaml.Media.Brush IconBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        public IFileSystemItem Model => _model;

        public FileSystemViewModel(IFileSystemItem model)
        {
            _model = model;
        }

        /// <summary>
        /// F2 인라인 이름 변경 시작.
        /// </summary>
        public void BeginRename()
        {
            EditableName = System.IO.Path.GetFileNameWithoutExtension(Name);
            IsRenaming = true;
        }

        /// <summary>
        /// 이름 변경 커밋 (Enter).
        /// </summary>
        public bool CommitRename()
        {
            IsRenaming = false;
            string newName = EditableName.Trim();
            if (string.IsNullOrEmpty(newName) || newName == System.IO.Path.GetFileNameWithoutExtension(Name))
                return false;

            // 확장자 유지
            string ext = System.IO.Path.GetExtension(Name);
            string fullNewName = newName + ext;

            string dir = System.IO.Path.GetDirectoryName(Path)!;
            string newPath = System.IO.Path.Combine(dir, fullNewName);

            try
            {
                if (this is FolderViewModel)
                    System.IO.Directory.Move(Path, newPath);
                else
                    System.IO.File.Move(Path, newPath);

                // 모델 업데이트
                _model.Name = fullNewName;
                _model.Path = newPath;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Path));
                return true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Rename error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// XAML x:Bind 함수 바인딩용 (IsRenaming의 반전).
        /// </summary>
        public static Microsoft.UI.Xaml.Visibility NotRenaming(bool isRenaming)
            => isRenaming ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        /// <summary>
        /// 이름 변경 취소 (Esc).
        /// </summary>
        public void CancelRename()
        {
            IsRenaming = false;
        }
    }
}
