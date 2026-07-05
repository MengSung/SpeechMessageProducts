// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentWorkflowResultMapperTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentWorkflowResultMapperTests
// 主要成員：Map_reads_neutral_callback_result_fields
// 引用命名空間：FluentAssertions、SpeechMessage.Payments.Models、SpeechMessage.Payments.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 callback workflow 結果投影已移到 SpeechMessage.Payments.AspNetCore，
/// 且仍能保留 ChurchReport 後續 CRM/LINE 流程需要的核心欄位。
/// </summary>
public sealed class PaymentWorkflowResultMapperTests
{
    [Fact]
    public void Map_reads_neutral_callback_result_fields()
    {
        var result = new PaymentCallbackResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "F202606250001",
            ProviderTransactionId = "provider-tx-1",
            Amount = 1200m,
            Currency = "TWD",
            ProviderData = new Dictionary<string, string>
            {
                ["provider_message"] = "approved"
            }
        };
        var mapper = new PaymentWorkflowResultMapper();

        var workflow = mapper.Map(result);

        workflow.Status.Should().Be(PaymentStatus.Succeeded);
        workflow.ProductOrderId.Should().Be("F202606250001");
        workflow.ProviderTransactionId.Should().Be("provider-tx-1");
        workflow.Amount.Should().Be(1200m);
        workflow.ProviderMessage.Should().Be("approved");
    }
}
