using Uninstaller.Core.Models;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Services;

public interface IApplicationNormalizer
{
    Application? Normalize(RawRegistryApplication rawApp);
}
