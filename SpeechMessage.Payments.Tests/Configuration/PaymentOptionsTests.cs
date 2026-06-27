using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Configuration;

/// <summary>
/// 驗證多商店 profile 設定的 binding 與解析規則。
/// ChurchReport 與未來產品都只透過 profile 名稱選金流，不應在程式碼中硬編 provider credential。
/// </summary>
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
        // 空 profile name 代表呼叫端接受 Payment:DefaultProfile；
        // 這是讓產品程式不必知道實際 provider 的主要入口。
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
        // 未知 profile 必須 fail closed，避免退回硬編 credential 或錯誤商店。
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
        // 使用記憶體中的 JSON fixture，避免測試依賴 ChurchReport/appsettings.json 或真實商店密鑰。
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
