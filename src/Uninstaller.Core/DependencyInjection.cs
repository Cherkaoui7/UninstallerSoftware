using Microsoft.Extensions.DependencyInjection;

namespace Uninstaller.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        return services;
    }
}
