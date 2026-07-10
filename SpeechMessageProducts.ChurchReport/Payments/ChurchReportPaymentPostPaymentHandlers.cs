// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class ChurchReportPaymentWorkflowContextKeys、class ChurchReportPaymentRecordUpdater、class ChurchReportPaymentPayerNotifier
// 主要成員：UpdateAsync、NotifyAsync
// 引用命名空間：System、System.Threading、System.Threading.Tasks、ChurchReport.Services、Microsoft.Extensions.Logging、Microsoft.Xrm.Sdk、SpeechMessage.Payments.Workflows、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using static ChurchReport.Services.PaymentFeeTypeHelper;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 付款後流程共用資料的 key。
/// 共用 workflow project 不知道 CRM entity、ToolUtility、LINE 聯絡人等型別，
/// 因此由 ChurchReport 在自己的 product layer 定義 context item contract。
/// </summary>
public static class ChurchReportPaymentWorkflowContextKeys
{
    public const string ToolUtility = nameof(ToolUtility);
    public const string FeeEntity = nameof(FeeEntity);
    public const string IsSuccess = nameof(IsSuccess);
    public const string FullName = nameof(FullName);
    public const string FeeType = nameof(FeeType);
    public const string ContactEntity = nameof(ContactEntity);
}

/// <summary>
/// ChurchReport 的付款紀錄更新 handler。
/// 它實作共用 <see cref="IPaymentRecordUpdater"/>，但具體仍呼叫 ChurchReport 的 CRM service。
/// </summary>
public sealed class ChurchReportPaymentRecordUpdater : IPaymentRecordUpdater
{
    private readonly PaymentCrmService _crmService;

    public ChurchReportPaymentRecordUpdater(PaymentCrmService crmService)
    {
        _crmService = crmService ?? throw new ArgumentNullException(nameof(crmService));
    }

    public Task UpdateAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
    {
        var toolUtility = context.GetRequiredItem<ToolUtilityClass>(ChurchReportPaymentWorkflowContextKeys.ToolUtility);
        var feeEntity = context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.FeeEntity);
        var isSuccess = context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess);

        _crmService.UpdateFeeEntityWithPaymentResult(toolUtility, feeEntity, context.Payment, isSuccess);
        toolUtility.UpdateEntity(ref feeEntity);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ChurchReport 的付款者通知 handler。
/// 它實作共用 <see cref="IPaymentPayerNotifier"/>，但 LINE 訊息內容、收費單分類與聯絡人欄位
/// 仍全部留在 ChurchReport product layer。
/// </summary>
public sealed class ChurchReportPaymentPayerNotifier : IPaymentPayerNotifier
{
    private readonly PaymentNotificationService _notificationService;
    private readonly ILogger<ChurchReportPaymentPayerNotifier> _logger;

    public ChurchReportPaymentPayerNotifier(
        PaymentNotificationService notificationService,
        ILogger<ChurchReportPaymentPayerNotifier> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
    {
        var toolUtility = context.GetRequiredItem<ToolUtilityClass>(ChurchReportPaymentWorkflowContextKeys.ToolUtility);
        var feeEntity = context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.FeeEntity);
        var fullName = context.GetOptionalItem<string>(ChurchReportPaymentWorkflowContextKeys.FullName) ?? "未知";
        var feeType = context.GetRequiredItem<FeeType>(ChurchReportPaymentWorkflowContextKeys.FeeType);
        var contactEntity = context.GetOptionalItem<Entity>(ChurchReportPaymentWorkflowContextKeys.ContactEntity);
        var isSuccess = context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess);

        if (contactEntity is null)
        {
            return;
        }

        try
        {
            if (isSuccess)
            {
                await _notificationService.SendLineNotificationByTypeAsync(
                    toolUtility,
                    feeEntity,
                    context.Payment,
                    fullName,
                    feeType,
                    contactEntity);
            }
            else
            {
                await _notificationService.SendLineFailureNotificationByTypeAsync(
                    toolUtility,
                    feeEntity,
                    context.Payment,
                    fullName,
                    feeType,
                    contactEntity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ChurchReport payment payer notification failed. OrderId: {OrderId}",
                context.Payment.ProductOrderId);
        }
    }
}
