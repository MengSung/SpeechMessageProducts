// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class ChurchReportPaymentContextBuilder
// 主要成員：Build、BuildFromResolvedValues、ResolveContactEntity
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Services、Microsoft.Xrm.Sdk、SpeechMessage.Payments.Workflows、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
