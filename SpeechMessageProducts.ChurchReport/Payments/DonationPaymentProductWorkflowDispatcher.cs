/*
 * 檔案整體說明
 *
 * 目的與定位：預設的 ChurchReport 奉獻付款產品流程派送器。 目前 ChurchReport 付款回傳後主要有兩條產品流程： 1. 一般收費單/奉獻收費單：交給 
 * DonationFeePaymentProcessor 更新 CRM 與呈現結果。 2. 定期定額奉獻：交給 RecurringDonationPaymentProcessor 更新定期扣款相關資料。 這個類別很薄，
 * 原因是派送器只應決定「交給誰」，不應把 CRM 更新、LINE 通知、 ViewBag 設定等細節全部塞進自己。這樣未來新增其他產品流程時，只要新增清楚的分支或 processor， 不會讓 callback 
 * controller 直接長出大量 ChurchReport 業務邏輯。
 *
 * 主要功能與流程：
 * 本檔宣告 IDonationPaymentProductWorkflowDispatcher、DonationPaymentProductWorkflowDispatcher。
 * 主要操作入口包含 HandleFeeReturn、HandleDedicationBookingReturn。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Payments/DonationPaymentReturnWorkflow.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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
