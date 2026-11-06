/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：ChurchReportPaymentWorkflowContextKeys：ChurchReport 付款後流程共用資料的 key。 共用 workflow project 不知道
 * CRM entity、ToolUtility、LINE 聯絡人等型別， 因此由 ChurchReport 在自己的 product layer 定義 context item contract。；
 * ChurchReportPaymentRecordUpdater：ChurchReport 的付款紀錄更新 handler。 它實作共用 IPaymentRecordUpdater，但具體仍呼叫 ChurchReport
 * 的 CRM service。；ChurchReportPaymentPayerNotifier：ChurchReport 的付款者通知 handler。 它實作共用 IPaymentPayerNotifier，但 LINE
 * 訊息內容、收費單分類與聯絡人欄位 仍全部留在 ChurchReport product layer。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportPaymentWorkflowContextKeys、ChurchReportPaymentRecordUpdater、ChurchReportPaymentPayerNotifier。
 * 主要操作入口包含 UpdateAsync、NotifyAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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

    public Task NotifyAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
    {
        var toolUtility = context.GetRequiredItem<ToolUtilityClass>(ChurchReportPaymentWorkflowContextKeys.ToolUtility);
        var feeEntity = context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.FeeEntity);
        var fullName = context.GetOptionalItem<string>(ChurchReportPaymentWorkflowContextKeys.FullName) ?? "未知";
        var feeType = context.GetRequiredItem<FeeType>(ChurchReportPaymentWorkflowContextKeys.FeeType);
        var contactEntity = context.GetOptionalItem<Entity>(ChurchReportPaymentWorkflowContextKeys.ContactEntity);
        var isSuccess = context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess);

        if (contactEntity is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (isSuccess)
            {
                _notificationService.SendLineNotificationByType(
                    toolUtility,
                    feeEntity,
                    context.Payment,
                    fullName,
                    feeType,
                    contactEntity);
            }
            else
            {
                _notificationService.SendLineFailureNotificationByType(
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

        return Task.CompletedTask;
    }
}
