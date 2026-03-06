using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.StartScreen;

namespace Span.Services
{
    /// <summary>
    /// Windows 작업표시줄 Jump List 관리. 최근 폴더와 빠른 작업을 표시한다.
    /// </summary>
    public class JumpListService
    {
        private readonly LocalizationService _loc;

        public JumpListService(LocalizationService loc)
        {
            _loc = loc;
        }

        /// <summary>
        /// Update the Jump List with recent folders and standard tasks.
        /// </summary>
        public async Task UpdateAsync(IEnumerable<(string Name, string Path)> recentFolders)
        {
            try
            {
                if (!JumpList.IsSupported()) return;

                var jumpList = await JumpList.LoadCurrentAsync();
                jumpList.Items.Clear();
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

                // Recent folders (max 10)
                foreach (var folder in recentFolders.Take(10))
                {
                    var item = JumpListItem.CreateWithArguments(
                        folder.Path,
                        folder.Name);
                    item.GroupName = _loc.Get("JumpList_RecentFolders");
                    item.Logo = new Uri("ms-appx:///Assets/Square44x44Logo.png");
                    jumpList.Items.Add(item);
                }

                // Tasks
                var newWindow = JumpListItem.CreateWithArguments(
                    "--new-window",
                    _loc.Get("JumpList_NewWindow"));
                newWindow.Logo = new Uri("ms-appx:///Assets/Square44x44Logo.png");
                jumpList.Items.Add(newWindow);

                await jumpList.SaveAsync();
                Helpers.DebugLogger.Log($"[JumpListService] Updated with {recentFolders.Count()} recent folders");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[JumpListService] Update failed: {ex.Message}");
            }
        }
    }
}
