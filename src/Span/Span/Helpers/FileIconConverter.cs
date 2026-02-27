using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using Span.Models;
using System;

namespace Span.Helpers
{
    /// <summary>
    /// 파일 시스템 항목을 아이콘 글리프 문자열로 변환하는 XAML 컨버터.
    /// FolderItem/FolderViewModel → 폴더 아이콘, FileItem/FileViewModel → 확장자별 아이콘 (App.xaml 리소스).
    /// </summary>
    public class FileIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (value is FolderItem || value is Span.ViewModels.FolderViewModel)
                {
                    return Application.Current.Resources["Icon_Folder"] as string;
                }
                else if (value is FileItem file)
                {
                    return GetIconForFile(file.Name);
                }
                else if (value is Span.ViewModels.FileViewModel fileVm)
                {
                    return GetIconForFile(fileVm.Name);
                }
                else if (value is string fileName)
                {
                    return GetIconForFile(fileName);
                }

                return Application.Current.Resources["Icon_File_Default"] as string;
            }
            catch
            {
                // Return safe default or empty if resource access fails (e.g. shutdown)
                return "\uECE0";
            }
        }

        private string GetIconForFile(string fileName)
        {
            try
            {
                string ext = System.IO.Path.GetExtension(fileName).ToLower().TrimStart('.');
                string resourceKey = "Icon_File_Default";

                switch (ext)
                {
                    case "cs": resourceKey = "Icon_File_CS"; break;
                    case "xaml": resourceKey = "Icon_File_XAML"; break;
                    case "json": resourceKey = "Icon_File_JSON"; break;
                    case "md": resourceKey = "Icon_File_MD"; break;
                    case "sln":
                    case "csproj": resourceKey = "Icon_File_SLN"; break;
                    case "png":
                    case "jpg":
                    case "jpeg": resourceKey = "Icon_File_Image"; break;
                }

                if (Application.Current.Resources.TryGetValue(resourceKey, out object resource))
                {
                    return resource as string;
                }
                return Application.Current.Resources["Icon_File_Default"] as string;
            }
            catch
            {
                return "\uECE0";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
