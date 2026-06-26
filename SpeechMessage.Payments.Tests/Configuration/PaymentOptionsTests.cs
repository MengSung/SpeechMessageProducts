using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Configuration;

public sealed class PaymentOptionsTests
{
    [Fact]
    public void Options_bind_multiple_named_profiles()
    {
        var configuration = BuildPaymentConfiguration();
        var options = new PaymentOptions();

        configuration.GetSection("Payment").Bind(options);

        options.DefaultProfile.Should().Be("JesusTest");
        options.Profiles["JesusTest"].Provider.Should().Be(PaymentProviderKind.Sinopac);
        options.Profiles["MyPayProduction"].Provider.Should().Be(PaymentProviderKind.MyPay);
        options.Profiles["MyPayProduction"].Environment.Should().Be(PaymentEnvironment.Production);
        options.Profiles["MyPayProduction"].Credentials["StoreId"].Should().Be("130544850001");
    }

    [Fact]
    public void Resolver_uses_default_profile_when_requested_name_is_empty()
    {
        var options = new PaymentOptions();
        BuildPaymentConfiguration().GetSection("Payment").Bind(options);
        var resolver = new OptionsPaymentProfileResolver(Options.Create(options));

        var profile = resolver.Resolve("");

        profile.Name.Should().Be("JesusTest");
        profile.Provider.Should().Be(PaymentProviderKind.Sinopac);
    }

    [Fact]
    public void Resolver_throws_configuration_exception_when_profile_cannot_be_resolved()
    {
        var options = new PaymentOptions();
        BuildPaymentConfiguration().GetSection("Payment").Bind(options);
        var resolver = new OptionsPaymentProfileResolver(Options.Create(options));

        var act = () => resolver.Resolve("MissingProfile");

        act.Should()
            .Throw<PaymentConfigurationException>()
            .WithMessage("*MissingProfile*");
    }

    private static IConfiguration BuildPaymentConfiguration()
    {
        const string json = """
        {
          "Payment": {
            "DefaultProfile": "JesusTest",
            "Profiles": {
              "JesusTest": {
                "Provider": "Sinopac",
                "Environment": "Sandbox",
                "Credentials": { "ShopNo": "NA0149_001", "A1": "a", "A2": "b", "B1": "c", "B2": "d", "XKeyId": "x" },
                "Endpoints": { "ApiBaseUrl": "https://sandbox.sinopac.test/api/" }
              },
              "MyPayProduction": {
                "Provider": "MyPay",
                "Environment": "Production",
                "Credentials": { "StoreId": "130544850001", "Key": "key", "IV": "iv" },
                "Endpoints": { "ApiBaseUrl": "https://ka.mypay.test/api/init" }
              }
            }
          }
        }
        """;

        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
    }
}
