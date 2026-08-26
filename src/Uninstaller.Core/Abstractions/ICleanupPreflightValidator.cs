using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface ICleanupPreflightValidator
{
    Task<PreflightValidationResult> ValidateAsync(CleanupPlanItem item, Application application, CancellationToken cancellationToken = default);
}
