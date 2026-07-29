using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.ProductClient.Configuration;

/// <summary>
/// 驗證產品端 Gateway 的啟動邊界。
/// Central Gateway 與 Local Gateway 共用同一份契約，只能由受控的 HTTPS Endpoint 區分；
/// 產品設定不得直接指向 CRM Web API、SOAP Endpoint，也不得同時啟用 Embedded 分支。
/// </summary>
public sealed class GatewayProductDynamicsOptionsValidator
    : IValidateOptions<ProductDynamicsOptions>
{
    private const int MaximumProfileAliasLength = 128;
    private const int MaximumApiPrefixLength = 64;

    public ValidateOptionsResult Validate(string? name, ProductDynamicsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.ExecutionMode != DynamicsExecutionMode.Gateway)
        {
            failures.Add("ExecutionMode must be Gateway.");
        }

        if (!IsValidProfileAlias(options.ProfileAlias))
        {
            failures.Add("ProfileAlias must be 1-128 letters, digits, '.', '_' or '-'.");
        }

        if (options.Embedded is not null)
        {
            failures.Add("Embedded options are forbidden when ExecutionMode=Gateway.");
        }

        if (options.Gateway is null)
        {
            failures.Add("Gateway options are required.");
        }
        else
        {
            ValidateEndpoint(options.Gateway.Endpoint, failures);
            ValidateApiPrefix(options.Gateway.ApiPrefix, failures);
            if (options.Gateway.MaxResponseBytes is
                < GatewayProductClientLimits.MinimumResponseBytes or
                > GatewayProductClientLimits.MaximumResponseBytes)
            {
                failures.Add(
                    $"Gateway MaxResponseBytes must be between " +
                    $"{GatewayProductClientLimits.MinimumResponseBytes} and " +
                    $"{GatewayProductClientLimits.MaximumResponseBytes}.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidProfileAlias(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Length <= MaximumProfileAliasLength
               && value.All(static character =>
                   char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static void ValidateEndpoint(string? value, ICollection<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add(
                "Gateway Endpoint must be an absolute HTTPS URI without user-info, query, or fragment.");
            return;
        }

        var path = endpoint.AbsolutePath;
        if (path.Contains("/api/data/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/xrmservices/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Organization.svc", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Gateway Endpoint cannot be a raw Dynamics endpoint.");
        }
    }

    private static void ValidateApiPrefix(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumApiPrefixLength
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('?')
            || value.Contains('#')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal))
        {
            failures.Add(
                "Gateway ApiPrefix must be one bounded absolute path without traversal, query, or fragment.");
        }
    }
}
