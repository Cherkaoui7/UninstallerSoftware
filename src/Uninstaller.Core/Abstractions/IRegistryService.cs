using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Models;

namespace Uninstaller.Core.Abstractions;

public interface IRegistryService
{
    Task<IReadOnlyList<RawRegistryApplication>> GetUninstallEntriesAsync(CancellationToken cancellationToken);
    bool KeyExists(string root, string path);
    bool ValueExists(string root, string path, string valueName);
}
