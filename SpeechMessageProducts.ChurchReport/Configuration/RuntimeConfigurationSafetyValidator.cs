using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ChurchReport.Configuration;

public static class RuntimeConfigurationSafetyValidator
{
    private static readonly string[] SensitiveKeyManifest =
    [
        "LineMessaging:Jesus:ChannelAccessToken",
        "LineMessaging:JesusBack:ChannelAccessToken",
        "LineLogin:ChannelSecret",
        "MiniApp:ChannelSecret",
        "CrmConnection:Username",
        "CrmConnection:Password",
        "LinePay:ChannelSecret",
        "Payment:Profiles:JesusTest:Credentials:ShopNo",
        "Payment:Profiles:JesusTest:Credentials:A1",
        "Payment:Profiles:JesusTest:Credentials:A2",
        "Payment:Profiles:JesusTest:Credentials:B1",
        "Payment:Profiles:JesusTest:Credentials:B2",
        "Payment:Profiles:JesusTest:Credentials:XKeyId",
        "Payment:Profiles:MyPayProduction:Credentials:Key",
        "Payment:Profiles:MyPayProduction:Credentials:IV",
        "Sinopac:A1",
        "Sinopac:A2",
        "Sinopac:B1",
        "Sinopac:B2",
        "Sinopac:XKeyID",
        "MyPay:Key"
    ];

    private static readonly string[] PlaceholderMarkers =
    [
        "placeholder",
        "replace",
        "runtime_secret",
        "your_",
        "_here",
        "todo",
        "dummy",
        "example",
        "sample",
        "changeme"
    ];

    public static void Validate(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var errors = new List<string>();

        RequireBoolean(configuration, "Security:EnforceGlobalAuthorization", expected: true, errors);
        RequireBoolean(configuration, "Security:AllowSessionIdentityFallback", expected: false, errors);
        RequireBoolean(configuration, "LinePay:IsSandbox", expected: false, errors);
        RequireProductionEnvironment(configuration, "Cash_Environment", errors);
        RequireExact(configuration, "PAY_PROVIDER", "高鉅金流", errors);
        RequireExact(configuration, "Payment:DefaultProfile", "MyPayProduction", errors);
        RequireExact(configuration, "Payment:Profiles:MyPayProduction:Environment", "Production", errors);
        RequireBoolean(configuration, "TSPG:TestMode", expected: false, errors);

        foreach (var key in SensitiveKeyManifest)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"missing-secret:{key}");
            }
            else if (IsPlaceholder(value))
            {
                errors.Add($"placeholder-secret:{key}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Production runtime configuration safety validation failed. "
                + string.Join("; ", errors));
        }
    }

    private static void RequireBoolean(
        IConfiguration configuration,
        string key,
        bool expected,
        ICollection<string> errors)
    {
        if (!bool.TryParse(configuration[key], out var actual) || actual != expected)
        {
            errors.Add($"unsafe-control:{key}");
        }
    }

    private static void RequireExact(
        IConfiguration configuration,
        string key,
        string expected,
        ICollection<string> errors)
    {
        if (!string.Equals(configuration[key], expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"unsafe-control:{key}");
        }
    }

    private static void RequireProductionEnvironment(
        IConfiguration configuration,
        string key,
        ICollection<string> errors)
    {
        var value = configuration[key];
        var isProduction = string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "正式環境", StringComparison.Ordinal);

        if (!isProduction)
        {
            errors.Add($"unsafe-control:{key}");
        }
    }

    private static bool IsPlaceholder(string value)
    {
        return PlaceholderMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
