using System;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IUninstallService
{
    Task<UninstallSession> RunUninstallAsync(Application application, CancellationToken cancellationToken = default);
}
