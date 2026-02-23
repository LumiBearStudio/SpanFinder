using Span.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Span.Services
{
    public interface IFileSystemService
    {
        Task<List<DriveItem>> GetDrivesAsync();
        Task<List<IFileSystemItem>> GetItemsAsync(string path);
    }
}
