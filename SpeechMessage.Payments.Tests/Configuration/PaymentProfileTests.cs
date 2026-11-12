/*
 * 檔案整體說明
 *
 * 目的與定位：保護 Payment 命名設定檔的解析失敗邊界，以及永豐 profile 的必要設定驗證。
 * 測試透過實際 PaymentGateway、OptionsPaymentProfileResolver 與 PaymentOptionsValidator，
 * 確認不存在的 profile 或沒有可用預設值時會回傳 ConfigurationInvalid，且永豐缺少必要鍵時
 * 只以 profile 名稱和缺少鍵名診斷，不把合成憑證或 endpoint 值帶入錯誤訊息。
 *
 * 主要情境：每個案例建立本地 PaymentOptions 和 PaymentMerchantProfile，使用固定合成值；
 * 直接驗證 options validator，並用沒有 provider 的 gateway 驗證解析失敗不會進入付款傳輸。
 * 測試不讀 ChurchReport 設定、User Secrets、環境變數，也不發出 HTTP 或建立外部資源。
 *
 * 維護與驗證：必要鍵集合須與 04A P4-04 契約同步；新增診斷欄位時必須仍只含安全鍵名。
 * 以本專案 Release 測試確認指定案例，並在固定 guard 執行全方案驗證；本檔結果不是商店或實機付款證據。
 */
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Configuration;

/// <summary>
/// 驗證具名付款設定檔的 fail-closed 行為與永豐必要鍵檢查。
/// 使用本地合成設定；錯誤不得回退至未指定或其他商店，也不得洩漏憑證值。
/// </summary>
public sealed class PaymentProfileTests
{
    /// <summary>
    /// 不存在的 profile 與沒有 DefaultProfile 的空名稱均須產生 ConfigurationInvalid，
    /// 且因解析在 provider 呼叫之前失敗，不得送出付款請求。
    /// </summary>
    [Fact]
    public async Task Profile_Missing_FailsClosed()
    {
        var missing = CreateGateway(new PaymentOptions
        {
            DefaultProfile = "Configured",
            Profiles = { ["Configured"] = new PaymentMerchantProfile { Provider = PaymentProviderKind.Sinopac } }
        });
        var missingResult = await missing.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProfileName = "NotConfigured",
            ProductOrderId = "SYNTHETIC-ORDER"
        });
        missingResult.Error.Kind.Should().Be(PaymentErrorKind.ConfigurationInvalid);

        var noDefault = CreateGateway(new PaymentOptions
        {
            Profiles = { ["Configured"] = new PaymentMerchantProfile { Provider = PaymentProviderKind.Sinopac } }
        });
        var blankResult = await noDefault.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProfileName = " ",
            ProductOrderId = "SYNTHETIC-ORDER"
        });
        blankResult.Error.Kind.Should().Be(PaymentErrorKind.ConfigurationInvalid);
    }

    /// <summary>
    /// 逐一省略每個必要永豐設定，確認驗證失敗並只回報 profile 名稱與缺少鍵名，
    /// 不得將任何合成憑證或 API 網址值寫進診斷。
    /// </summary>
    [Fact]
    public void SinopacProfile_MissingCredential_FailsValidation()
    {
        var required = new[]
        {
            "ShopNo", "A1", "A2", "B1", "B2", "XKeyId", "Endpoints:ApiBaseUrl"
        };

        foreach (var missingKey in required)
        {
            var options = new PaymentOptions();
            var profile = new PaymentMerchantProfile
            {
                Provider = PaymentProviderKind.Sinopac,
                Credentials =
                {
                    ["ShopNo"] = "SYNTHETIC-SHOP",
                    ["A1"] = "SYNTHETIC-A1",
                    ["A2"] = "SYNTHETIC-A2",
                    ["B1"] = "SYNTHETIC-B1",
                    ["B2"] = "SYNTHETIC-B2",
                    ["XKeyId"] = "SYNTHETIC-XKEY"
                },
                Endpoints = { ["ApiBaseUrl"] = "https://synthetic.invalid/" }
            };
            if (missingKey.StartsWith("Endpoints:", StringComparison.Ordinal))
            {
                profile.Endpoints.Remove("ApiBaseUrl");
            }
            else
            {
                profile.Credentials.Remove(missingKey);
            }
            options.Profiles["JesusTest"] = profile;

            var result = new PaymentOptionsValidator().Validate(Options.DefaultName, options);
            result.Succeeded.Should().BeFalse();
            var message = result.FailureMessage ?? string.Empty;
            message.Should().Contain("JesusTest");
            message.Should().Contain(missingKey);
            message.Should().NotContain("SYNTHETIC-");
            message.Should().NotContain("synthetic.invalid");
        }

        var aliasProfile = new PaymentMerchantProfile
        {
            Provider = PaymentProviderKind.Sinopac,
            Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ShopNo"] = "SYNTHETIC-SHOP",
                ["A1"] = "SYNTHETIC-A1",
                ["A2"] = "SYNTHETIC-A2",
                ["B1"] = "SYNTHETIC-B1",
                ["B2"] = "SYNTHETIC-B2",
                ["XKeyID"] = "SYNTHETIC-XKEY"
            },
            Endpoints = { ["ApiBaseUrl"] = "https://synthetic.invalid/" }
        };
        var aliasOptions = new PaymentOptions();
        aliasOptions.Profiles["AliasProfile"] = aliasProfile;
        new PaymentOptionsValidator().Validate(Options.DefaultName, aliasOptions).Succeeded.Should().BeTrue();

        aliasProfile.Credentials["B2"] = " ";
        var blankValueResult = new PaymentOptionsValidator().Validate(Options.DefaultName, aliasOptions);
        blankValueResult.Succeeded.Should().BeFalse();
        (blankValueResult.FailureMessage ?? string.Empty).Should().Contain("B2");

        var nonSinopacOptions = new PaymentOptions();
        nonSinopacOptions.Profiles["MyPayProfile"] = new PaymentMerchantProfile
        {
            Provider = PaymentProviderKind.MyPay
        };
        new PaymentOptionsValidator().Validate(Options.DefaultName, nonSinopacOptions).Succeeded.Should().BeTrue();
    }

    /// <summary>建立只用 options resolver 的實際 gateway；profile 解析失敗必須在 provider 選取之前結束。</summary>
    private static IPaymentGateway CreateGateway(PaymentOptions options) =>
        new PaymentGateway(new OptionsPaymentProfileResolver(Options.Create(options)), Array.Empty<IPaymentProvider>());
}
