using System.Collections.Generic;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Services;

public interface IApplicationDeduplicator
{
    IEnumerable<Application> Deduplicate(IEnumerable<Application> applications);
}
