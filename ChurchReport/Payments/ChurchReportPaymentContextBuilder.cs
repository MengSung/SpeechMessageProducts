using System;
using System.Collections.Generic;
using ChurchReport.Services;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using static ChurchReport.Services.PaymentFeeTypeHelper;

namespace ChurchReport.Payments;

/// <summary>
/// 將 ChurchReport 的收費單、聯絡人與付款結果整理成共用付款後流程可消費的 context。
/// 共用金流核心只認得 <see cref="PaymentWorkflowResult"/>；CRM Entity、聯絡人欄位與收費單分類
/// 都屬於 ChurchReport 產品層，因此集中在這個 adapter，避免 MyPay、TSPG、Donation 各自重複組裝。
/// </summary>
public sealed class ChurchReportPaymentContextBuilder
{
    private const string UnknownPayerName = "未知付款者";
    private readonly PaymentFeeTypeHelper _feeTypeHelper;

    public ChurchReportPaymentContextBuilder(PaymentFeeTypeHelper feeTypeHelper)
    {
        _feeTypeHelper = feeTypeHelper ?? throw new ArgumentNullException(nameof(feeTypeHelper));
    }

    /// <summary>
    /// 建立付款後 workflow 的產品 context。這裡只做資料組裝，不更新 CRM、不送 LINE，
    /// 讓 side effect 仍由 <see cref="ChurchReportPaymentRecordUpdater"/> 與
    /// <see cref="ChurchReportPaymentPayerNotifier"/> 負責。
    /// </summary>
    public PaymentPostPaymentContext Build(
        ToolUtilityClass toolUtility,
        Entity feeEntity,
        PaymentWorkflowResult payment,
        bool isSuccess)
    {
        if (toolUtility is null) throw new ArgumentNullException(nameof(toolUtility));
        if (feeEntity is null) throw new ArgumentNullException(nameof(feeEntity));
        if (payment is null) throw new ArgumentNullException(nameof(payment));

        var contactEntity = ResolveContactEntity(toolUtility, feeEntity, out var fullName);
        var feeType = _feeTypeHelper.DetermineFeeType(toolUtility, feeEntity);

        return BuildFromResolvedValues(
            toolUtility,
            feeEntity,
            payment,
            isSuccess,
            fullName,
            feeType,
            contactEntity);
    }

    /// <summary>
    /// 使用已解析好的產品資料建立 context。這個方法讓測試與少數已經取得 contact/feeType 的舊流程
    /// 可以重用同一份 context contract，而不需要為了測試去模擬整個 CRM 查詢層。
    /// </summary>
    public PaymentPostPaymentContext BuildFromResolvedValues(
        ToolUtilityClass toolUtility,
        Entity feeEntity,
        PaymentWorkflowResult payment,
        bool isSuccess,
        string? fullName,
        FeeType feeType,
        Entity? contactEntity)
    {
        if (toolUtility is null) throw new ArgumentNullException(nameof(toolUtility));
        if (feeEntity is null) throw new ArgumentNullException(nameof(feeEntity));
        if (payment is null) throw new ArgumentNullException(nameof(payment));

        return new PaymentPostPaymentContext(
            payment,
            new Dictionary<string, object?>
            {
                [ChurchReportPaymentWorkflowContextKeys.ToolUtility] = toolUtility,
                [ChurchReportPaymentWorkflowContextKeys.FeeEntity] = feeEntity,
                [ChurchReportPaymentWorkflowContextKeys.IsSuccess] = isSuccess,
                [ChurchReportPaymentWorkflowContextKeys.FullName] = string.IsNullOrWhiteSpace(fullName) ? UnknownPayerName : fullName,
                [ChurchReportPaymentWorkflowContextKeys.FeeType] = feeType,
                [ChurchReportPaymentWorkflowContextKeys.ContactEntity] = contactEntity
            });
    }

    private static Entity? ResolveContactEntity(
        ToolUtilityClass toolUtility,
        Entity feeEntity,
        out string fullName)
    {
        fullName = UnknownPayerName;

        var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
        if (contactId == Guid.Empty)
        {
            return null;
        }

        var contactEntity = toolUtility.RetrieveEntity("contact", contactId);
        if (contactEntity is null)
        {
            return null;
        }

        var crmFullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
        if (!string.IsNullOrWhiteSpace(crmFullName))
        {
            fullName = crmFullName;
        }

        return contactEntity;
    }
}
