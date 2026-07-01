using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 奉獻付款回傳 workflow。
///
/// 這個 workflow 是「通用金流核心」與「ChurchReport 產品後續流程」中間的轉接層。
///
/// 通用金流核心回傳的是 <see cref="PaymentStatusResult"/>：
/// - 它只關心 provider 回報的付款狀態、金額、交易編號、錯誤訊息與 ProviderData。
/// - 它不應知道 ChurchReport 的 CRM fee id、奉獻類別、LINE 通知或 Razor View。
///
/// ChurchReport 後續流程需要的是 <see cref="DonationPaymentWorkflowResult"/>：
/// - 它要知道要更新哪一筆 CRM 收費單或定期定額資料。
/// - 它要知道付款是否成功、信用卡 token、卡號遮罩、付款分類等產品欄位。
/// - 它最後會交給 DonationFeePaymentProcessor 或 RecurringDonationPaymentProcessor。
///
/// 所以這個類別的主要責任就是「轉譯」與「派送」，不要在這裡直接實作 CRM 更新或 LINE 發送。
/// </summary>
public interface IDonationPaymentReturnWorkflow
{
    IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult);
}

/// <summary>
/// 預設的 ChurchReport 奉獻付款回傳 workflow。
///
/// 如果 DI 有提供 <see cref="IDonationPaymentProductWorkflowDispatcher"/>，
/// workflow 會把結果派送到真正的 ChurchReport 產品 processor。
/// 如果沒有提供 dispatcher，workflow 會回傳一個基本付款結果頁，這主要用於測試或保底顯示。
/// </summary>
public sealed class DonationPaymentReturnWorkflow : IDonationPaymentReturnWorkflow
{
    private const string PaymentResultViewName = "~/Views/PaymentReturn/PaymentResult.cshtml";

    private readonly IDonationPaymentProductWorkflowDispatcher? _productWorkflowDispatcher;

    public DonationPaymentReturnWorkflow(
        IDonationPaymentProductWorkflowDispatcher? productWorkflowDispatcher = null)
    {
        _productWorkflowDispatcher = productWorkflowDispatcher;
    }

    public IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult)
    {
        ArgumentNullException.ThrowIfNull(statusResult);

        var providerData = statusResult.ProviderData ?? new Dictionary<string, string>();
        if (_productWorkflowDispatcher != null)
        {
            var workflowResult = CreateWorkflowPaymentResult(shopNo, payToken, statusResult, providerData);
            return IsDedicationBooking(workflowResult.PaymentCategory)
                ? _productWorkflowDispatcher.HandleDedicationBookingReturn(shopNo, payToken, workflowResult)
                : _productWorkflowDispatcher.HandleFeeReturn(shopNo, payToken, workflowResult);
        }

        return CreateFallbackViewResult(shopNo, payToken, statusResult, providerData);
    }

    private static ViewResult CreateFallbackViewResult(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult,
        IReadOnlyDictionary<string, string> providerData)
    {
        // Fallback view 的目的不是取代正式 ChurchReport 產品流程。
        // 正式環境應該由 dispatcher 接手，進一步更新 CRM 與通知付款者。
        // 這裡只提供一個可診斷的結果頁，避免沒有 dispatcher 時直接空白或例外。
        var isSuccess = statusResult.Status == PaymentStatus.Succeeded &&
            !statusResult.Error.HasError;
        var providerMessage = FirstNonEmpty(
            Read(providerData, "provider_message"),
            statusResult.Error.Message,
            statusResult.Status.ToString());
        var orderId = FirstNonEmpty(
            statusResult.ProductOrderId,
            statusResult.ProviderOrderRef,
            payToken);

        var viewData = CreateViewData();
        viewData["IsSuccess"] = isSuccess;
        viewData["Message"] = isSuccess
            ? "付款狀態已成功取得，ChurchReport 後續流程將繼續處理。"
            : "付款失敗，請稍後再試或聯絡教會辦公室。";
        viewData["OrderId"] = orderId;
        viewData["TransactionId"] = FirstNonEmpty(
            statusResult.ProviderTransactionId,
            statusResult.ProviderOrderRef,
            payToken);
        viewData["Amount"] = statusResult.Amount?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        viewData["PaymentTime"] = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        viewData["PaymentMethod"] = ResolvePaymentMethod(providerData);
        viewData["DedicationCategory"] = ResolvePaymentCategory(providerData);
        viewData["ErrorDetails"] = providerMessage;
        viewData["ShopNo"] = shopNo ?? string.Empty;
        viewData["ProductEntityId"] = Read(providerData, "product_entity_id");

        return new ViewResult
        {
            ViewName = PaymentResultViewName,
            ViewData = viewData
        };
    }

    private static ViewDataDictionary CreateViewData()
    {
        return new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary());
    }

    private static string ResolvePaymentMethod(IReadOnlyDictionary<string, string> providerData)
    {
        var paymentCategory = Read(providerData, "payment_category");
        return IsDedicationBooking(paymentCategory)
            ? "定期定額信用卡"
            : "信用卡";
    }

    private static string ResolvePaymentCategory(IReadOnlyDictionary<string, string> providerData)
    {
        var paymentCategory = Read(providerData, "payment_category");
        if (IsDedicationBooking(paymentCategory))
        {
            return "定期定額奉獻";
        }

        return string.IsNullOrWhiteSpace(paymentCategory)
            ? "付款"
            : paymentCategory;
    }

    private static bool IsDedicationBooking(string value)
    {
        // 這裡判斷的是 ChurchReport 產品分類，不是 provider 付款方式。
        // 有些舊流程會把定期定額奉獻寫成英文代碼，有些資料可能保留中文分類，
        // 因此用多個中性條件辨識，避免把分類判斷散落到 controller 或 processor。
        return value.Equals("dedication_booking", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("recurring_dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("定期", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("奉獻", StringComparison.OrdinalIgnoreCase);
    }

    private static DonationPaymentWorkflowResult CreateWorkflowPaymentResult(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult,
        IReadOnlyDictionary<string, string> providerData)
    {
        var isSuccess = statusResult.Status == PaymentStatus.Succeeded &&
            !statusResult.Error.HasError;
        var status = isSuccess ? "S" : "F";
        var description = FirstNonEmpty(
            Read(providerData, "provider_message"),
            statusResult.Error.Message,
            statusResult.Status.ToString());

        return new DonationPaymentWorkflowResult
        {
            ShopNo = shopNo ?? string.Empty,
            PayToken = FirstNonEmpty(payToken, statusResult.ProviderOrderRef),
            OrderNo = FirstNonEmpty(statusResult.ProductOrderId, statusResult.ProviderOrderRef),
            ProviderTransactionId = statusResult.ProviderTransactionId,
            Amount = statusResult.Amount,
            AmountMinorUnits = ResolveMinorAmount(statusResult, providerData),
            ProductEntityId = Read(providerData, "product_entity_id", "param1"),
            PaymentOrganization = Read(providerData, "payment_organization", "param2"),
            PaymentCategory = Read(providerData, "payment_category", "param3"),
            PayType = FirstNonEmpty(Read(providerData, "pay_type"), "C"),
            Status = status,
            Description = description,
            LeftCCNo = Read(providerData, "left_cc_no"),
            RightCCNo = Read(providerData, "right_cc_no"),
            CCExpDate = Read(providerData, "cc_exp_date"),
            CCToken = Read(providerData, "cc_token"),
            ProviderData = providerData
        };
    }

    private static string ResolveMinorAmount(
        PaymentStatusResult statusResult,
        IReadOnlyDictionary<string, string> providerData)
    {
        var providerAmount = Read(providerData, "amount");
        if (!string.IsNullOrWhiteSpace(providerAmount))
        {
            return providerAmount;
        }

        return statusResult.Amount.HasValue
            ? decimal.Round(statusResult.Amount.Value * 100m, 0, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string Read(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
