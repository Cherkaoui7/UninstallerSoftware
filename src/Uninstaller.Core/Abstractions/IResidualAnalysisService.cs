using System;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IResidualAnalysisService
{
    Task<ResidualAnalysisSession> RunAnalysisAsync(UninstallSession uninstallSession, Application application, CancellationToken cancellationToken = default);
}
