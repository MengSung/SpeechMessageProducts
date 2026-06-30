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
/// ChurchReport 付款回傳 workflow 的產品層入口。
/// Controller 只負責把 HTTP callback 轉成金流核心查詢結果；這個 workflow 再決定要交給
/// ChurchReport 的收費單、定期定額奉獻、CRM 更新與 LINE 通知流程。
/// </summary>
public interface IDonationPaymentReturnWorkflow
{
    IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult);
}

/// <summary>
/// Provider-neutral 的 ChurchReport 付款回傳 workflow。
/// 此類別接收 <see cref="PaymentStatusResult"/>，把 provider 結果轉成 ChurchReport 產品層 DTO，
/// 再交給產品流程派送器；沒有派送器時才使用只顯示結果頁的 fallback。
/// </summary>
public sealed class DonationPaymentReturnWorkflow : IDonationPaymentReturnWorkflow
{
    private const string PaymentResultViewName = "~/Views/QPayCard/PaymentResult.cshtml";
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
            // 這裡是 ChurchReport 產品層的分界點：金流核心只告知付款狀態，
            // CRM 更新、LINE 通知與付款結果頁由 fee/recurring donation processor 負責。
            var workflowResult = CreateWorkflowPaymentResult(shopNo, payToken, statusResult, providerData);
            return IsDedicationBooking(workflowResult.PaymentCategory)
                ? _productWorkflowDispatcher.HandleDedicationBookingReturn(shopNo, payToken, workflowResult)
                : _productWorkflowDispatcher.HandleFeeReturn(shopNo, payToken, workflowResult);
        }

        // 測試或未注入 dispatcher 的情境使用保守 fallback，只顯示結果，不寫入 CRM 或 LINE。
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
            ? "Order created successfully. Payment processing will continue through the ChurchReport workflow."
            : "Payment failed. Please try again later or contact the church office.";
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
            ? "Credit card recurring"
            : "Credit card";
    }

    private static string ResolvePaymentCategory(IReadOnlyDictionary<string, string> providerData)
    {
        var paymentCategory = Read(providerData, "payment_category");
        if (IsDedicationBooking(paymentCategory))
        {
            return "Dedication booking";
        }

        return string.IsNullOrWhiteSpace(paymentCategory)
            ? "Payment"
            : paymentCategory;
    }

    private static bool IsDedicationBooking(string value)
    {
        // 這是 ChurchReport 產品分類，不是 provider payment method；
        // 多保留幾個關鍵字可讓舊 callback metadata 與新中性分類都能正確派送。
        return value.Equals("dedication_booking", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("recurring_dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("認獻", StringComparison.OrdinalIgnoreCase) ||
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