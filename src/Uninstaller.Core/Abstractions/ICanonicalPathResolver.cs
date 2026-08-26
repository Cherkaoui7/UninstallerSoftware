using System.Threading;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface ICanonicalPathResolver
{
    PathSafetyResult ResolveAndVerify(string path, string? expectedRoot = null, CancellationToken cancellationToken = default);
    bool IsPathContainedWithin(string path, string rootPath);
}
