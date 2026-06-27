using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

public sealed class QPayCreatePaymentGatewayAdapter
{
    private const int DefaultRecurringDeductTotalNum = 12;
    private const int DefaultRecurringDeductFreq = 1;
    private const string DefaultRecurringPeriodType = "M";

    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentCreateRequestFactory _requestFactory;
    private readonly ChurchReportPaymentProfileResolver _profileResolver;

    public QPayCreatePaymentGatewayAdapter(
        IPaymentGateway paymentGateway,
        PaymentCreateRequestFactory requestFactory,
        ChurchReportPaymentProfileResolver profileResolver)
    {
        _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
    }

    public Task<PaymentCreateResult> CreateCardPaymentAsync(
        QPayCreatePaymentInput input,
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

    public async Task<CreOrder> CreateLegacyOrderAsync(
        QPayCreatePaymentInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result = await CreateCardPaymentAsync(input, cancellationToken);
        return ToLegacyCreOrder(input, result);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(QPayCreatePaymentInput input)
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

    private static IReadOnlyList<PaymentLineItem> ResolveItems(QPayCreatePaymentInput input)
    {
        if (input.Items.Count > 0)
        {
            return input.Items;
        }

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

    private static int ResolveDeductTotalNum(QPayCreatePaymentInput input)
    {
        return IsRecurringCard(input) && input.DeductTotalNum <= 0
            ? DefaultRecurringDeductTotalNum
            : input.DeductTotalNum;
    }

    private static string ResolvePeriodType(QPayCreatePaymentInput input)
    {
        return IsRecurringCard(input) && string.IsNullOrWhiteSpace(input.PeriodType)
            ? DefaultRecurringPeriodType
            : input.PeriodType;
    }

    private static int ResolveDeductFreq(QPayCreatePaymentInput input)
    {
        return IsRecurringCard(input) && input.DeductFreq <= 0
            ? DefaultRecurringDeductFreq
            : input.DeductFreq;
    }

    private static bool IsRecurringCard(QPayCreatePaymentInput input)
    {
        return string.Equals(input.PaymentMethodSubType?.Trim(), "REGULAR", StringComparison.OrdinalIgnoreCase);
    }

    private static CreOrder ToLegacyCreOrder(
        QPayCreatePaymentInput input,
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

public sealed record QPayCreatePaymentInput
{
    public string ProfileName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ProductName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProductEntityId { get; init; } = string.Empty;
    public string PaymentOrganization { get; init; } = string.Empty;
    public string PaymentCategory { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string BackendUrl { get; init; } = string.Empty;
    public string SuccessUrl { get; init; } = string.Empty;
    public string FailureUrl { get; init; } = string.Empty;
    public string AutoBilling { get; init; } = "Y";
    public string Staging { get; init; } = string.Empty;
    public int DeductTotalNum { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public int DeductFreq { get; init; }
    public string CreditCardToken { get; init; } = string.Empty;
    public string ExpireDate { get; init; } = string.Empty;
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
}
