// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentReturnWorkflowNamingTests
// 主要成員：Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename、New_donation_payment_workflow_result_exists_after_rename
// 引用命名空間：FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證付款後產品流程使用中性結果 DTO，且舊供應商結果型別已移除。
/// 以實際產品組件的完整型別名稱偵測 DTO 遺失或相容別名回流，不執行付款回呼。
/// 只讀反射資訊，不建立 DTO 實例或保存任何使用者、付款及外部連線狀態。
/// </summary>
public sealed class DonationPaymentReturnWorkflowNamingTests
{
    /// <summary>
    /// 保護舊供應商付款結果 DTO 已移除。
    /// 在已載入的真實產品組件查詢舊完整名稱，讓重新引入相容 DTO 的退化可被偵測；斷言舊型別不存在。
    /// </summary>
    [Fact]
    public void Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename()
    {
        // CRM 更新與 LINE 通知流程已經改用 DonationPaymentWorkflowResult。
        // 舊 QPayWorkflowPaymentResult 不應再保留成 alias，避免未來高鉅、台新或新金流看起來也在依賴永豐名稱。
        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult");

        resultType.Should().BeNull();
    }

    /// <summary>
    /// 保護產品流程使用中性付款結果 DTO。
    /// 以完整型別名稱查詢真實產品組件，偵測結果型別遭刪除或搬到錯誤邊界；斷言新 DTO 存在。
    /// </summary>
    [Fact]
    public void New_donation_payment_workflow_result_exists_after_rename()
    {
        // ChurchReport 產品流程應該使用 DonationPaymentWorkflowResult，而不是使用永豐專屬的 QPay 名稱。
        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult");

        resultType.Should().NotBeNull("新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別");
    }
}
