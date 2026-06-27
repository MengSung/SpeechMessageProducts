using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Taishin;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.Taishin;

/// <summary>
/// 驗證台新 TSPG provider 的狀態碼、前端/後端 callback、hash 驗證與建立付款 payload。
/// 台新 ret_code/state/hash 規則屬於 provider protocol，應留在 <c>SpeechMessage.Payments</c> provider 實作與測試中。
/// </summary>
public sealed class TaishinProviderTests
{
    [Theory]
    [InlineData("00", "1", PaymentStatus.Succeeded)]
    [InlineData("0000", "1", PaymentStatus.Succeeded)]
    [InlineData("", "1", PaymentStatus.Succeeded)]
    [InlineData("05", "0", PaymentStatus.Failed)]
    [InlineData("9999", "1", PaymentStatus.Failed)]
    [InlineData("", "0", PaymentStatus.Failed)]
    public void Status_mapper_maps_tspg_codes_to_normalized_status(
        string retCode,
        string state,
        PaymentStatus expected)
    {
        TaishinStatusMapper.Map(retCode, state).Should().Be(expected);
    }

    [Fact]
    public void Callback_parser_maps_frontend_form_callback_to_neutral_result()
    {
        // 前端 post-back 常見為 form callback；parser 必須驗證 hash 後輸出通用 callback result。
        var profile = CreateProfile();
        var hash = ComputeNotificationHash("store-key", "TX123", "F202606250001", "1", "store-iv");
        var request = new PaymentCallbackRequest
        {
            ProfileName = "TaishinSandbox",
            ProviderHint = PaymentProviderKind.Taishin,
            HttpMethod = "POST",
            ContentType = "application/x-www-form-urlencoded",
            Form = new Dictionary<string, string>
            {
                ["order_id"] = "F202606250001",
                ["transaction_id"] = "TX123",
                ["ret_code"] = "00",
                ["ret_msg"] = "approved",
                ["state"] = "1",
                ["actual_cost"] = "120000",
                ["currency"] = "TWD",
                ["hash"] = hash,
                ["cardno"] = "4111111111111111"
            }
        };

        var result = TaishinCallbackParser.Parse(profile, request);

        result.Status.Should().Be(PaymentStatus.Succeeded);
        result.ProductOrderId.Should().Be("F202606250001");
        result.ProviderTransactionId.Should().Be("TX123");
        result.Amount.Should().Be(1200m);
        result.Currency.Should().Be("TWD");
        result.Error.Should().Be(PaymentError.None);
        result.ProviderData.Should().ContainKey("ret_code").WhoseValue.Should().Be("00");
        result.Diagnostics["hash"].Should().Be("***");
        result.Diagnostics["cardno"].Should().Be("411111******1111");
    }

    [Fact]
    public void Callback_parser_maps_backend_json_callback_to_json_acknowledgement()
    {
        // 後端 result-url 常見為 JSON callback，台新需要 JSON acknowledgement；
        // ChurchReport controller 只把 acknowledgement descriptor 轉成 IActionResult。
        var profile = CreateProfile();
        var hash = ComputeNotificationHash("store-key", "TX456", "F202606250002", "1", "store-iv");
        var request = new PaymentCallbackRequest
        {
            ProfileName = "TaishinSandbox",
            ProviderHint = PaymentProviderKind.Taishin,
            HttpMethod = "POST",
            ContentType = "application/json",
            RawBody = $$"""
            {
              "ret_code": "00",
              "ret_msg": "approved",
              "params": {
                "order_no": "F202606250002",
                "transaction_id": "TX456",
                "state": "1",
                "amt": "350000",
                "cur": "NTD",
                "hash": "{{hash}}"
              }
            }
            """
        };

        var result = TaishinCallbackParser.Parse(profile, request);

        result.Status.Should().Be(PaymentStatus.Succeeded);
        result.ProductOrderId.Should().Be("F202606250002");
        result.ProviderTransactionId.Should().Be("TX456");
        result.Amount.Should().Be(3500m);
        result.Acknowledgement.Kind.Should().Be(PaymentAckKind.Json);
        result.Acknowledgement.Content.Should().Contain("success");
    }

    [Fact]
    public void Callback_parser_maps_invalid_hash_to_signature_invalid()
    {
        // Hash 驗證失敗是 provider protocol 層的簽章錯誤。
        // 產品層可記錄或略過 CRM 更新，但不應自行重算台新 hash。
        var profile = CreateProfile();
        var request = new PaymentCallbackRequest
        {
            ProfileName = "TaishinSandbox",
            ProviderHint = PaymentProviderKind.Taishin,
            Form = new Dictionary<string, string>
            {
                ["order_id"] = "F202606250003",
                ["transaction_id"] = "TX789",
                ["ret_code"] = "00",
                ["state"] = "1",
                ["actual_cost"] = "10000",
                ["hash"] = "bad-hash"
            }
        };

        var result = TaishinCallbackParser.Parse(profile, request);

        result.Error.Kind.Should().Be(PaymentErrorKind.SignatureInvalid);
        result.Status.Should().Be(PaymentStatus.Succeeded);
        result.Acknowledgement.Kind.Should().Be(PaymentAckKind.Json);
    }

    [Fact]
    public void Request_mapper_maps_create_request_to_tspg_payload()
    {
        // 建立付款時把 neutral amount/callback/customer 欄位轉成 TSPG auth.ashx 需要的 JSON payload。
        var profile = CreateProfile();
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "F202606250004",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            Customer = new PaymentCustomer
            {
                Name = "Grace",
                Email = "grace@example.test",
                Phone = "0912345678"
            },
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.test/return",
                BackendUrl = "https://example.test/backend"
            }
        };

        var payload = TaishinRequestMapper.MapCreatePayload(profile, request);

        payload.Mid.Should().Be("999812777000199");
        payload.Tid.Should().Be("T0000000");
        payload.Params.OrderNo.Should().Be("F202606250004");
        payload.Params.Amt.Should().Be("120000");
        payload.Params.Cur.Should().Be("NTD");
        payload.Params.OrderDesc.Should().Be("Fee payment");
        payload.Params.PostBackUrl.Should().Be("https://example.test/return");
        payload.Params.ResultUrl.Should().Be("https://example.test/backend");
        payload.Params.CardholderName.Should().Be("Grace");
        payload.Params.CardholderEmail.Should().Be("grace@example.test");
        payload.Params.CardholderMobilePhone!.PhoneNumber.Should().Be("0912345678");
    }

    [Fact]
    public void Service_registration_adds_taishin_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultProfile"] = "TaishinSandbox",
                ["Profiles:TaishinSandbox:Provider"] = "Taishin"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSpeechMessagePayments(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IPaymentProvider>()
            .Should()
            .Contain(paymentProvider => paymentProvider.ProviderKind == PaymentProviderKind.Taishin);
    }

    private static PaymentMerchantProfile CreateProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "TaishinSandbox",
            Provider = PaymentProviderKind.Taishin,
            Credentials = new Dictionary<string, string>
            {
                ["StoreId"] = "999812777000199",
                ["StoreKey"] = "store-key",
                ["StoreIV"] = "store-iv",
                ["TerminalId"] = "T0000000"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://tspg-t.taishinbank.com.tw/tspgapi/restapi"
            }
        };
    }

    private static string ComputeNotificationHash(
        string storeKey,
        string transactionId,
        string orderId,
        string state,
        string storeIV)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{storeKey}{transactionId}{orderId}{state}{storeIV}"));
        return Convert.ToHexString(bytes);
    }
}
