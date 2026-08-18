// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：interface IDonationPaymentProductWorkflowDispatcher、class DonationPaymentProductWorkflowDispatcher
// 主要成員：HandleFeeReturn、HandleDedicationBookingReturn
// 引用命名空間：System、ChurchReport.Tools、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Mvc、ToolUtilityNameSpace.DependencyInjection
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.Tools;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Mvc;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 奉獻付款「產品後續流程」的派送介面。
///
/// 金流核心只回答付款狀態，不能知道 ChurchReport 的 CRM 收費單、LINE 通知、奉獻分類或結果頁規則。
/// 所以 callback 被解析完成後，會先轉成 <see cref="DonationPaymentWorkflowResult"/>，
/// 再由這個 dispatcher 判斷要交給哪一個 ChurchReport 產品流程處理。
///
/// 這個介面刻意放在 ChurchReport.Payments：
/// - 它可以依賴 ChurchReport.Tools 裡的產品流程 processor。
/// - 它不應被移到 SpeechMessage.Payments，因為通用金流核心不應知道 ChurchReport 的業務流程。
/// - 它使用 DonationPayment 命名，因為這是 ChurchReport 奉獻付款流程，不是單一 provider 協定。
/// </summary>
public interface IDonationPaymentProductWorkflowDispatcher
{
    IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult);

    IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult);
}

/// <summary>
/// 預設的 ChurchReport 奉獻付款產品流程派送器。
///
/// 目前 ChurchReport 付款回傳後主要有兩條產品流程：
/// 1. 一般收費單/奉獻收費單：交給 <see cref="DonationFeePaymentProcessor"/> 更新 CRM 與呈現結果。
/// 2. 定期定額奉獻：交給 <see cref="RecurringDonationPaymentProcessor"/> 更新定期扣款相關資料。
///
/// 這個類別很薄，原因是派送器只應決定「交給誰」，不應把 CRM 更新、LINE 通知、
/// ViewBag 設定等細節全部塞進自己。這樣未來新增其他產品流程時，只要新增清楚的分支或 processor，
/// 不會讓 callback controller 直接長出大量 ChurchReport 業務邏輯。
/// </summary>
public sealed class DonationPaymentProductWorkflowDispatcher : IDonationPaymentProductWorkflowDispatcher
{
    private readonly IToolUtilityProvider _toolUtilityProvider;
    private readonly ILineNotificationWorkflow _lineNotificationWorkflow;

    public DonationPaymentProductWorkflowDispatcher(
        IToolUtilityProvider toolUtilityProvider,
        ILineNotificationWorkflow lineNotificationWorkflow)
    {
        _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
        _lineNotificationWorkflow = lineNotificationWorkflow ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
    }

    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new DonationFeePaymentProcessor(_toolUtilityProvider, _lineNotificationWorkflow);
        return processor.HandlePaymentReturn(
            shopNo,
            payToken,
            paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new RecurringDonationPaymentProcessor(
            _toolUtilityProvider,
            _lineNotificationWorkflow,
            null);
        return processor.HandlePaymentReturn(
            shopNo,
            payToken,
            paymentResult);
    }
}
