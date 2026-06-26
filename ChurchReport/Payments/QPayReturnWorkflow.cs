using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

public interface IQPayReturnWorkflow
{
    IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult);
}

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
            var workflowResult = CreateWorkflowPaymentResult(shopNo, payToken, statusResult, providerData);
            return IsDedicationBooking(workflowResult.PaymentCategory)
                ? _productWorkflowDispatcher.HandleDedicationBookingReturn(shopNo, payToken, workflowResult)
                : _productWorkflowDispatcher.HandleFeeReturn(shopNo, payToken, workflowResult);
        }

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
