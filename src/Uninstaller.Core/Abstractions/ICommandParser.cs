using Uninstaller.Domain.Entities;
using Uninstaller.Core.Models;

namespace Uninstaller.Core.Abstractions;

public interface ICommandParser
{
    StructuredCommand Parse(Application application);
}
