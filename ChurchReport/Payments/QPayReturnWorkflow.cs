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
/// QPay 前端 return URL 後續產品流程的抽象。
/// 金流核心只負責 parse callback 與 query payment status；
/// ChurchReport 的 CRM 更新、奉獻結果頁與費用 workflow 由此介面背後的產品層處理。
/// </summary>
public interface IQPayReturnWorkflow
{
    IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult);
}

/// <summary>
/// 將永豐 QPay return/query 的標準化結果導回 ChurchReport 既有產品 workflow。
/// 這個類別不做永豐簽章、加解密或狀態碼解析，只消費 <see cref="PaymentStatusResult"/>。
/// </summary>
public sealed class QPayReturnWorkflow : IQPayReturnWorkflow
{
    private const string PaymentResultViewName = "~/Views/QPayCard/PaymentResult.cshtml";
    private readonly IQPayProductWorkflowDispatcher? _productWorkflowDispatcher;

    public QPayReturnWorkflow(IQPayProductWorkflowDispatcher? productWorkflowDispatcher = null)
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
            // 正式執行時優先交給既有 QPay processor，維持 CRM/LINE/頁面行為。
            // providerData 只承載核心已清理過的 provider metadata，避免產品層重新處理 raw callback。
            var workflowResult = CreateWorkflowPaymentResult(shopNo, payToken, statusResult, providerData);
            return IsDedicationBooking(workflowResult.PaymentCategory)
                ? _productWorkflowDispatcher.HandleDedicationBookingReturn(shopNo, payToken, workflowResult)
                : _productWorkflowDispatcher.HandleFeeReturn(shopNo, payToken, workflowResult);
        }

        // 沒有注入產品 dispatcher 時提供保底 ViewResult，主要供測試或未接上完整 ChurchReport workflow 的環境使用。
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
        // 這裡判斷的是 ChurchReport 產品分類，不是 provider payment method。
        // 保留中英文與舊值相容，讓舊資料仍可走到正確奉獻預約處理器。
        return value.Equals("dedication_booking", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("recurring_dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u8a8d\u737b", StringComparison.OrdinalIgnoreCase);
    }

    private static QPayWorkflowPaymentResult CreateWorkflowPaymentResult(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult,
        IReadOnlyDictionary<string, string> providerData)
    {
        // 將 provider-neutral status result 轉成舊 QPay processor 可處理的 workflow DTO。
        // 這是產品層相容 shim，不應被移到 SpeechMessage.Payments。
        var isSuccess = statusResult.Status == PaymentStatus.Succeeded &&
            !statusResult.Error.HasError;
        var status = isSuccess ? "S" : "F";
        var description = FirstNonEmpty(
            Read(providerData, "provider_message"),
            statusResult.Error.Message,
            statusResult.Status.ToString());

        return new QPayWorkflowPaymentResult
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
