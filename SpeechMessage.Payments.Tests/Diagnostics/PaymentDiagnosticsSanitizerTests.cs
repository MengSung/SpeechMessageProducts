/*
 * 檔案整體說明
 * 目的與定位：保護F10金流核心的診斷輸出遮罩；所有卡號與Token都是不可連線的合成字串，
 * 不讀取商店設定、不呼叫銀行、不把測試結果冒稱實機或宿主日誌驗收。
 * 主要功能與流程：以真實Sanitize及Sinopac回呼parser驗證短Token、含分隔符／內嵌卡號、
 * ATM精確白名單與偽裝欄位、密碼／授權鍵、句點／斜線PAN；斷言診斷不含原值並維持合法付款指示。
 * 使用邊界與維護：每案只擁有輸入與輸出字典；128個並行操作全部由Task.WhenAll排空，
 * 沒有I/O、連線、timer或全域可變測試狀態。輸入不得被修改，A/B輸出不可互相覆寫；
 * ProviderTransactionId仍是後續查詢所需的明確操作值，不能與可供log的診斷字典混為一談。
 */
using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Diagnostics;

/// <summary>
/// 以合成短憑證、格式化PAN與偽裝欄位重現發布前的遮罩漏洞，斷言精確結果及原字典不變；
/// 實際parser輸出仍只代表查詢線索。所有並行工作由本案排空，沒有外部I/O或跨案共享狀態。
/// </summary>
public sealed class PaymentDiagnosticsSanitizerTests
{
    /// <summary>既有長Token／純數字卡號遮罩格式不變，精確ATM欄位保留完整付款指示。</summary>
    [Fact]
    public void Sanitizer_masks_sensitive_provider_fields_and_preserves_safe_fields()
    {
        var input = new Dictionary<string, string>
        {
            ["PayToken"] = "1234567890abcdef",
            ["signature"] = "ABCDEF123456",
            ["StoreKey"] = "secret-key",
            ["cardno"] = "4111111111111111",
            ["atm_pay_no"] = "12345678901234",
            ["ret_code"] = "00",
            ["order_no"] = "F202606250001"
        };
        var sanitized = PaymentDiagnosticsSanitizer.Sanitize(input);
        sanitized["PayToken"].Should().Be("1234...cdef");
        sanitized["signature"].Should().Be("***");
        sanitized["StoreKey"].Should().Be("***");
        sanitized["cardno"].Should().Be("411111******1111");
        sanitized["atm_pay_no"].Should().Be("12345678901234");
        sanitized["ret_code"].Should().Be("00");
        sanitized["order_no"].Should().Be("F202606250001");
    }

    /// <summary>故障注入一至八字元Token；不得因無法取首尾片段而直接回傳原始憑證。</summary>
    [Theory]
    [InlineData("BindToken", "x")]
    [InlineData("bind_token", "x7")]
    [InlineData("PayToken", "b7C2D9x1")]
    [InlineData("access_token", "pQ2k6")]
    public void Sanitizer_short_tokens_are_fully_masked(string key, string value)
    {
        var input = new Dictionary<string, string> { [key] = value };
        var result = PaymentDiagnosticsSanitizer.Sanitize(input);
        result[key].Should().Be("***");
        input[key].Should().Be(value);
    }

    /// <summary>卡號可含空白、連字號或出現在描述中；完整原值必須在診斷發布前遮蔽。</summary>
    [Theory]
    [InlineData("4111-1111-1111-1111", "411111******1111")]
    [InlineData("4111 1111 1111 1111", "411111******1111")]
    [InlineData("4111\u00a01111\u00a01111\u00a01111", "411111******1111")]
    [InlineData("card=4111-1111-1111-1111; ok", "card=411111******1111; ok")]
    [InlineData("4111-1111-1111-1111/5555 5555 5555 4444", "***")]
    [InlineData("4000 0000 0000 0000 001", "400000******0001")]
    [InlineData("card=4111111111111111 12345; ok", "card=***; ok")]
    [InlineData("4111-1111-1111-1111-12345", "***")]
    public void Sanitizer_formatted_or_embedded_card_numbers_are_masked(string value, string expected)
    {
        var result = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string> { ["detail"] = value });
        result["detail"].Should().Be(expected);
        result["detail"].Should().NotContain(value);
    }

    /// <summary>真parser可帶入任意診斷鍵；注入密碼／授權標頭，必須在兩個公開診斷字典中完整遮蔽。</summary>
    [Theory]
    [InlineData("password", "synthetic-password")]
    [InlineData("pwd", "synthetic-password")]
    [InlineData("passwd", "synthetic-password")]
    [InlineData("Authorization", "Bearer synthetic-credential")]
    [InlineData("Proxy-Authorization", "Basic synthetic-credential")]
    public void Callback_actual_diagnostics_mask_passwords_and_authorization(string key, string value)
    {
        var result = SinopacCallbackParser.Parse(new PaymentCallbackRequest
        {
            Form = new Dictionary<string, string> { [key] = value }
        });
        result.Diagnostics[key].Should().Be("***");
        JsonSerializer.Serialize(new[] { result.ProviderData, result.Diagnostics }).Should().NotContain(value);
    }

    /// <summary>故障注入句點與斜線分隔的卡號；任何可記錄欄位都不能保留完整PAN表示。</summary>
    [Theory]
    [InlineData("4111.1111.1111.1111")]
    [InlineData("4111/1111/1111/1111")]
    public void Sanitizer_additional_card_punctuation_does_not_publish_full_card(string value)
    {
        var result = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string> { ["detail"] = value });
        result["detail"].Should().Be("411111******1111");
        result["detail"].Should().NotContain(value);
    }

    /// <summary>只有完整ATM欄位名稱可例外；在機密或任意欄位名稱嵌入白名單片段不能繞過遮罩。</summary>
    [Theory]
    [InlineData("atm_pay_no_secret", "secret-canary", "***")]
    [InlineData("BindToken_virtualaccount", "b7C2D9x1", "***")]
    [InlineData("fake_virtualaccount", "4111111111111111", "411111******1111")]
    public void Sanitizer_bank_account_fragments_do_not_bypass_sensitive_checks(string key, string value, string expected)
    {
        PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string> { [key] = value })[key].Should().Be(expected);
    }

    /// <summary>真實provider既有ATM別名仍保留；一般短數字或較長業務代碼不能被誤改為卡號。</summary>
    [Theory]
    [InlineData("atm_pay_no", "12345678901234")]
    [InlineData("AtmPayNo", "12345678901234")]
    [InlineData("virtual-account", "12345678901234")]
    [InlineData("detail", "invoice 1234-5678")]
    [InlineData("detail", "12345678901234567890")]
    public void Sanitizer_preserves_exact_payment_instructions_and_non_card_values(string key, string value)
    {
        PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string> { [key] = value })[key].Should().Be(value);
    }

    /// <summary>以真實回呼parser注入短憑證與格式化卡號，掃描兩個可供記錄的字典而非只測helper。</summary>
    [Fact]
    public void Callback_actual_diagnostic_outputs_do_not_publish_short_tokens_or_formatted_cards()
    {
        const string token = "b7C2D9x1";
        const string card = "4111-1111-1111-1111";
        var result = SinopacCallbackParser.Parse(new PaymentCallbackRequest
        {
            Form = new Dictionary<string, string>
            {
                ["ShopNo"] = "SYNTHETIC-SHOP",
                ["PayToken"] = token,
                ["BindToken"] = token,
                ["cardnumber"] = card
            }
        });
        result.Status.Should().Be(PaymentStatus.Pending);
        result.ProviderTransactionId.Should().Be(token);
        result.Diagnostics["BindToken"].Should().Be("***");
        result.Diagnostics["cardnumber"].Should().Be("411111******1111");
        var diagnosticJson = JsonSerializer.Serialize(new[] { result.ProviderData, result.Diagnostics });
        diagnosticJson.Should().NotContain(token).And.NotContain(card);
    }

    /// <summary>128個A/B操作共同排空後，各字典維持操作專屬副本；原始輸入與其他操作不能受輸出修改影響。</summary>
    [Fact]
    public async Task Sanitizer_parallel_operations_publish_detached_values_without_mutating_inputs()
    {
        var operations = Enumerable.Range(0, 128).Select(index => Task.Run(() =>
        {
            var token = index.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            var input = new Dictionary<string, string> { ["BindToken"] = token, ["order_no"] = "order-" + index };
            var first = PaymentDiagnosticsSanitizer.Sanitize(input);
            var second = PaymentDiagnosticsSanitizer.Sanitize(input);
            first["BindToken"].Should().Be("***");
            first["order_no"].Should().Be("order-" + index);
            ((IDictionary<string, string>)first)["order_no"] = "mutated";
            second["order_no"].Should().Be("order-" + index);
            input["BindToken"].Should().Be(token);
            input["order_no"].Should().Be("order-" + index);
        }));
        await Task.WhenAll(operations);
    }
}
