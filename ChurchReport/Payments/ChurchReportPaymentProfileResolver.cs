using Microsoft.Extensions.Configuration;

namespace ChurchReport.Payments;

public sealed class ChurchReportPaymentProfileResolver
{
    private readonly IConfiguration _configuration;

    public ChurchReportPaymentProfileResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ResolveProfileName(string? requestedProfileName = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfileName))
        {
            return requestedProfileName;
        }

        var providerProfile = _configuration["PAY_PROVIDER"] switch
        {
            "永豐金流" => "JesusTest",
            "高鉅金流" => "MyPayProduction",
            "台新金流" => "TaishinSandbox",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(providerProfile))
        {
            return providerProfile;
        }

        var defaultProfile = _configuration["Payment:DefaultProfile"];
        return !string.IsNullOrWhiteSpace(defaultProfile)
            ? defaultProfile
            : "JesusTest";
    }
}
