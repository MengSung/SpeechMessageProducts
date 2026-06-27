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

        var defaultProfile = _configuration["Payment:DefaultProfile"];
        if (!string.IsNullOrWhiteSpace(defaultProfile))
        {
            return defaultProfile;
        }

        return _configuration["PAY_PROVIDER"] switch
        {
            "高鉅金流" => "MyPayProduction",
            "台新金流" => "TaishinSandbox",
            _ => "JesusTest"
        };
    }
}
