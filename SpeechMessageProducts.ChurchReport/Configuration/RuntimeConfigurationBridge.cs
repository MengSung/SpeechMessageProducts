using System;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Configuration;

public sealed class RuntimeConfigurationBridge
{
    private IConfiguration? _configuration;

    public IConfiguration Current
    {
        get
        {
            return Volatile.Read(ref _configuration)
                ?? throw new InvalidOperationException("Runtime configuration bridge is not initialized.");
        }
    }

    public void Initialize(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var existing = Interlocked.CompareExchange(ref _configuration, configuration, null);
        if (existing is not null && !ReferenceEquals(existing, configuration))
        {
            throw new InvalidOperationException("Runtime configuration bridge is already initialized.");
        }
    }
}

public static class RuntimeConfiguration
{
    private static readonly RuntimeConfigurationBridge s_bridge = new();

    public static IConfiguration Current => s_bridge.Current;

    public static void Initialize(IConfiguration configuration)
    {
        s_bridge.Initialize(configuration);
    }
}
