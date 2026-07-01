using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 建立付款訂單時使用的中性 adapter。
/// 它負責把 ChurchReport 既有產品資料、CRM entity id、付款分類與 callback URL
/// 轉成 <see cref="PaymentCreateRequest"/>；真正的 provider protocol mapping 仍由
/// SpeechMessage.Payments 核心負責。
/// </summary>
public sealed class DonationPaymentCreateGatewayAdapter : IDonationPaymentCreateGatewayAdapter
{
    private const int DefaultRecurringDeductTotalNum = 12;
    private const int DefaultRecurringDeductFreq = 1;
    private const string DefaultRecurringPeriodType = "M";

    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentCreateRequestFactory _requestFactory;
    private readonly ChurchReportPaymentProfileResolver _profileResolver;

    public DonationPaymentCreateGatewayAdapter(
        IPaymentGateway paymentGateway,
        PaymentCreateRequestFactory requestFactory,
        ChurchReportPaymentProfileResolver profileResolver)
    {
        _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
    }

    /// <summary>
    /// 將 ChurchReport 產品付款輸入轉成 provider-neutral 的建單 request。
    /// Metadata 是 ChurchReport 與 provider mapping 的邊界，集中保存產品單據 id、組織、
    /// 付款分類與定期定額欄位，避免這些資料散落在各個 processor。
    /// </summary>
    public Task<PaymentCreateResult> CreateCardPaymentAsync(
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = _requestFactory.Create(new PaymentCreateRequestInput
        {
            ProfileName = _profileResolver.ResolveProfileName(input.ProfileName),
            ProductOrderId = input.ProductOrderId,
            Amount = input.Amount,
            Currency = input.Currency,
            Description = input.ProductName,
            PaymentMethod = input.PaymentMethod,
            PaymentMethodSubType = input.PaymentMethodSubType,
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = input.ReturnUrl,
                BackendUrl = input.BackendUrl,
                SuccessUrl = input.SuccessUrl,
                FailureUrl = input.FailureUrl
            },
            Customer = input.Customer,
            Items = ResolveItems(input),
            Metadata = BuildMetadata(input)
        });

        return _paymentGateway.CreatePaymentAsync(request, cancellationToken);
    }

    /// <summary>
    /// 建立付款訂單並投影回舊 QPayProcessor 期待的 CreOrder shape。
    /// 這是相容舊 ChurchReport 呼叫點的轉換，不應新增 provider-specific protocol 邏輯。
    /// </summary>
    public async Task<CreOrder> CreateLegacyOrderAsync(
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result = await CreateCardPaymentAsync(input, cancellationToken);
        return ToLegacyCreOrder(input, result);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(DonationPaymentCreateInput input)
    {
        return new Dictionary<string, string>
        {
            ["Param1"] = input.ProductEntityId,
            ["Param2"] = input.PaymentOrganization,
            ["Param3"] = input.PaymentCategory,
            ["PayType"] = input.PaymentMethod,
            ["PayTypeSub"] = input.PaymentMethodSubType,
            ["AutoBilling"] = input.AutoBilling,
            ["Staging"] = input.Staging,
            ["DeductTotalNum"] = ResolveDeductTotalNum(input).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PeriodType"] = ResolvePeriodType(input),
            ["DeductFreq"] = ResolveDeductFreq(input).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CCToken"] = input.CreditCardToken,
            ["ExpireDate"] = input.ExpireDate,
            ["UserId"] = FirstNonEmpty(input.Customer.Name, input.ProductOrderId)
        };
    }

    private static IReadOnlyList<PaymentLineItem> ResolveItems(DonationPaymentCreateInput input)
    {
        if (input.Items.Count > 0)
        {
            return input.Items;
        }

        // ChurchReport 舊 UI 不一定會送明細列；此時用產品名稱與總金額建立單一明細，
        // 讓 provider core 可以得到完整的中性建單資料。
        return new[]
        {
            new PaymentLineItem
            {
                Name = input.ProductName,
                Quantity = 1,
                UnitPrice = input.Amount,
                Currency = input.Currency
            }
        };
    }

    private static int ResolveDeductTotalNum(DonationPaymentCreateInput input)
    {
        // 舊 UI 沒有送定期定額總期數時，沿用 ChurchReport 既有預設：每月扣 12 期。
        return IsRecurringCard(input) && input.DeductTotalNum <= 0
            ? DefaultRecurringDeductTotalNum
            : input.DeductTotalNum;
    }

    private static string ResolvePeriodType(DonationPaymentCreateInput input)
    {
        return IsRecurringCard(input) && string.IsNullOrWhiteSpace(input.PeriodType)
            ? DefaultRecurringPeriodType
            : input.PeriodType;
    }

    private static int ResolveDeductFreq(DonationPaymentCreateInput input)
    {
        return IsRecurringCard(input) && input.DeductFreq <= 0
            ? DefaultRecurringDeductFreq
            : input.DeductFreq;
    }

    private static bool IsRecurringCard(DonationPaymentCreateInput input)
    {
        return string.Equals(input.PaymentMethodSubType?.Trim(), "REGULAR", StringComparison.OrdinalIgnoreCase);
    }

    private static CreOrder ToLegacyCreOrder(
        DonationPaymentCreateInput input,
        PaymentCreateResult result)
    {
        var missingHostedPaymentUrl = RequiresHostedPaymentUrl(input.PaymentMethod) &&
            string.IsNullOrWhiteSpace(result.PaymentPageUrl);
        var missingAtmPayNo = RequiresAtmPayNo(input.PaymentMethod) &&
            string.IsNullOrWhiteSpace(ReadProviderData(result.ProviderData, "atm_pay_no"));
        var isRejected = result.Error.HasError
            || result.Status is PaymentStatus.Failed or PaymentStatus.Cancelled
            || missingHostedPaymentUrl
            || missingAtmPayNo;
        var order = new CreOrder
        {
            OrderNo = FirstNonEmpty(result.ProductOrderId, input.ProductOrderId),
            ShopNo = FirstNonEmpty(
                ReadProviderData(result.ProviderData, "shop_no"),
                ReadProviderData(result.ProviderData, "ShopNo")),
            TSNo = result.ProviderOrderRef,
            PayType = input.PaymentMethod,
            Amount = decimal.ToInt32(input.Amount * 100m),
            Status = isRejected ? "F" : "S",
            Description = result.Error.HasError
                ? result.Error.Message
                : missingHostedPaymentUrl
                    ? "Payment provider did not return a payment page URL."
                    : missingAtmPayNo
                        ? "Payment provider did not return an ATM virtual account number."
                        : string.Empty,
            Param1 = input.ProductEntityId,
            Param2 = input.PaymentOrganization,
            Param3 = input.PaymentCategory
        };

        ApplyLegacyPaymentUrl(order, input.PaymentMethod, result.PaymentPageUrl, result.ProviderData);
        return order;
    }

    private static void ApplyLegacyPaymentUrl(
        CreOrder order,
        string paymentMethod,
        string paymentPageUrl,
        IReadOnlyDictionary<string, string> providerData)
    {
        switch ((paymentMethod ?? string.Empty).ToUpperInvariant())
        {
            case "M":
            case "L":
                if (string.IsNullOrWhiteSpace(paymentPageUrl))
                {
                    return;
                }

                order.MobileParam = new CreOrderMobileParamRes
                {
                    MobilePayURL = paymentPageUrl
                };
                break;
            case "A":
                order.ATMParam = new CreOrderATMParamRes
                {
                    AtmPayNo = ReadProviderData(providerData, "atm_pay_no"),
                    WebAtmURL = paymentPageUrl,
                    OtpURL = ReadProviderData(providerData, "otp_url")
                };
                break;
            default:
                if (string.IsNullOrWhiteSpace(paymentPageUrl))
                {
                    return;
                }

                order.CardParam = new CreOrderCardParamRes
                {
                    CardPayURL = paymentPageUrl
                };
                break;
        }
    }

    private static string ReadProviderData(
        IReadOnlyDictionary<string, string> providerData,
        string key)
    {
        return providerData.TryGetValue(key, out var value) ? value : string.Empty;
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

    private static bool RequiresHostedPaymentUrl(string paymentMethod)
    {
        return (paymentMethod ?? string.Empty).Trim().ToUpperInvariant() is "C" or "M" or "L" or "";
    }

    private static bool RequiresAtmPayNo(string paymentMethod)
    {
        return string.Equals((paymentMethod ?? string.Empty).Trim(), "A", StringComparison.OrdinalIgnoreCase);
    }
}
