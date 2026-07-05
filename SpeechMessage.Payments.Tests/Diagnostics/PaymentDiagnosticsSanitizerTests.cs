// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentDiagnosticsSanitizerTests
// 主要成員：Sanitizer_masks_sensitive_provider_fields_and_preserves_safe_fields
// 引用命名空間：FluentAssertions、SpeechMessage.Payments.Diagnostics、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using SpeechMessage.Payments.Diagnostics;
using Xunit;

namespace SpeechMessage.Payments.Tests.Diagnostics;

/// <summary>
/// 驗證金流核心輸出的 diagnostics 不會洩漏敏感資料。
/// 這組測試特別保護 ATM 虛擬帳號例外：它是付款指示，不是信用卡號，不能被 sanitizer 遮蔽。
/// </summary>
public sealed class PaymentDiagnosticsSanitizerTests
{
    [Fact]
    public void Sanitizer_masks_sensitive_provider_fields_and_preserves_safe_fields()
    {
        // 同時放入 token、signature、StoreKey、卡號與 ATM 虛擬帳號，
        // 確認 sanitizer 能區分「必須遮蔽的 secret」與「使用者需要看到的繳費資訊」。
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
}
