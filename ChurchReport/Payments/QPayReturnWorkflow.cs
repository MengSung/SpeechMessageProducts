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
/// ChurchReport 付款回傳 workflow 的中性介面。
/// Controller 只負責把 HTTP callback 轉成 core 查詢結果；這個 workflow 才決定付款結果如何進入
/// ChurchReport 的產品流程，例如費用單付款或定期定額奉獻。
/// </summary>
public interface IDonationPaymentReturnWorkflow
{
    IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult);
}

/// <summary>
/// 舊 QPay 命名的 return workflow 介面。
/// 保留此介面是為了讓舊 Controller、測試與 DI 在遷移期間不中斷；新程式應依賴
/// <see cref="IDonationPaymentReturnWorkflow"/>。
/// </summary>
[Obsolete("Use IDonationPaymentReturnWorkflow. QPay naming is retained only for compatibility during the migration.")]
public interface IQPayReturnWorkflow : IDonationPaymentReturnWorkflow
{
}

/// <summary>
/// 將 provider-neutral 的付款狀態轉成 ChurchReport 產品流程結果。
/// 這個類別是金流核心與 ChurchReport 業務流程的邊界：它讀取 SpeechMessage.Payments 的
/// <see cref="PaymentStatusResult"/>，但不直接處理 HTTP、簽章、CRM 查詢或 LINE 發送。
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
            // 正常正式流程會進入 dispatcher，讓 ChurchReport 自己的 fee/recurring donation processor
            // 負責 CRM 更新、LINE 通知與結果頁呈現。金流核心不應知道這些產品細節。
            var workflowResult = CreateWorkflowPaymentResult(shopNo, payToken, statusResult, providerData);
            return IsDedicationBooking(workflowResult.PaymentCategory)
                ? _productWorkflowDispatcher.HandleDedicationBookingReturn(shopNo, payToken, workflowResult)
                : _productWorkflowDispatcher.HandleFeeReturn(shopNo, payToken, workflowResult);
        }

        // 測試或尚未註冊產品 dispatcher 的環境仍要能得到可讀結果頁，
        // 因此這裡保留一個無副作用 fallback，不做 CRM 或 LINE 寫入。
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
        // 這裡判斷的是 ChurchReport 的產品分類，不是 provider payment method。
        // 保留多個舊值是為了相容歷史 callback metadata 與先前抽離階段產生的分類名稱。
        return value.Equals("dedication_booking", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("recurring_dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Dedication", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("認獻", StringComparison.OrdinalIgnoreCase);
    }

    private static DonationPaymentWorkflowResult CreateWorkflowPaymentResult(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult,
        IReadOnlyDictionary<string, string> providerData)
    {
        // 將金流核心的中性查詢結果投影成 ChurchReport 產品流程需要的 DTO。
        // 這個轉換集中在單一位置，避免 fee processor、recurring processor 與 controller 各自解析 providerData。
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

/// <summary>
/// 舊類別名稱的相容外殼。
/// 所有實際付款回傳流程都在 <see cref="DonationPaymentReturnWorkflow"/>；此類別不可新增業務邏輯。
/// </summary>
[Obsolete("Use DonationPaymentReturnWorkflow. QPay naming is retained only for compatibility during the migration.")]
public sealed class QPayReturnWorkflow : IQPayReturnWorkflow
{
    private readonly DonationPaymentReturnWorkflow _inner;

    public QPayReturnWorkflow(IQPayProductWorkflowDispatcher? productWorkflowDispatcher = null)
    {
        _inner = new DonationPaymentReturnWorkflow(
            productWorkflowDispatcher == null
                ? null
                : new LegacyProductWorkflowDispatcherAdapter(productWorkflowDispatcher));
    }

    public IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult)
    {
        return _inner.HandleReturn(shopNo, payToken, statusResult);
    }

    private sealed class LegacyProductWorkflowDispatcherAdapter : IDonationPaymentProductWorkflowDispatcher
    {
        private readonly IQPayProductWorkflowDispatcher _legacyDispatcher;

        public LegacyProductWorkflowDispatcherAdapter(IQPayProductWorkflowDispatcher legacyDispatcher)
        {
            _legacyDispatcher = legacyDispatcher;
        }

        public IActionResult HandleFeeReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            return _legacyDispatcher.HandleFeeReturn(
                shopNo,
                payToken,
                ToLegacyResult(paymentResult));
        }

        public IActionResult HandleDedicationBookingReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            return _legacyDispatcher.HandleDedicationBookingReturn(
                shopNo,
                payToken,
                ToLegacyResult(paymentResult));
        }

        private static QPayWorkflowPaymentResult ToLegacyResult(DonationPaymentWorkflowResult result)
        {
            return result is QPayWorkflowPaymentResult legacyResult
                ? legacyResult
                : new QPayWorkflowPaymentResult
                {
                    ShopNo = result.ShopNo,
                    PayToken = result.PayToken,
                    OrderNo = result.OrderNo,
                    ProviderTransactionId = result.ProviderTransactionId,
                    Amount = result.Amount,
                    AmountMinorUnits = result.AmountMinorUnits,
                    ProductEntityId = result.ProductEntityId,
                    PaymentOrganization = result.PaymentOrganization,
                    PaymentCategory = result.PaymentCategory,
                    PayType = result.PayType,
                    Status = result.Status,
                    Description = result.Description,
                    LeftCCNo = result.LeftCCNo,
                    RightCCNo = result.RightCCNo,
                    CCExpDate = result.CCExpDate,
                    CCToken = result.CCToken,
                    ProviderData = result.ProviderData
                };
        }
    }
}
