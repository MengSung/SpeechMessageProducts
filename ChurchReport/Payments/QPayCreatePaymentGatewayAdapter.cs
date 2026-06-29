using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 舊 QPay 建立訂單流程與新版 <see cref="IPaymentGateway"/> 的轉接器。
/// 這個類別仍屬於 ChurchReport，因為它必須維持 <see cref="CreOrder"/>、
/// QPayProcessor、奉獻/收費分類與既有畫面的相容性；真正可跨產品共用的
/// <see cref="PaymentCreateRequestFactory"/> 已移到 <c>SpeechMessage.Payments.AspNetCore</c>。
/// </summary>
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

    /// <summary>
    /// 將 ChurchReport 舊 QPay 欄位轉成 provider-neutral 付款請求後送進共用金流核心。
    /// 產品流程專用資料會被放進 Metadata，避免共用金流層知道 CRM entity id 或奉獻分類。
    /// </summary>
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

    /// <summary>
    /// 舊 QPayProcessor 與舊畫面仍吃 CreOrder shape，因此這裡只做相容轉換。
    /// 對外新的金流核心仍以 <see cref="PaymentCreateResult"/> 為主，不把 CreOrder 帶進共用專案。
    /// </summary>
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
        // Metadata 是產品流程與 provider mapping 的延伸欄位，不是 provider SDK DTO。
        // MyPay、永豐等 provider 若需要 Param1/2/3 或 UserId，會由金流核心再轉成對應 payload。
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

        // 部分 provider 建立付款時需要商品項目；舊 ChurchReport 流程沒有明細時，
        // 以產品名稱與付款金額建立單一項目，保留原本畫面行為。
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
        // 舊 UI 沒有送出定期定額期數時，沿用原流程的 12 期預設值。
        // 這是 ChurchReport 畫面相容規則，因此留在 adapter，不放進共用金流層。
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
        // 舊 CreOrder 以 Status=S/F 判斷是否建立成功。
        // 若信用卡/LinePay 缺付款頁、ATM 缺虛擬帳號，採 fail closed，避免使用者看到成功但無法付款。
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
        // 舊前端依付款方式讀取不同 legacy param。
        // 這裡只把金流核心回傳的付款頁或虛擬帳號塞回舊 shape，不做 provider-specific parser。
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

/// <summary>
/// ChurchReport 舊 QPay 建立付款流程的輸入模型。
/// 這個模型保留產品端欄位，例如 CRM entity id、奉獻/收費分類、舊 callback URL 與定期定額欄位；
/// adapter 會再把它轉成共用的 <see cref="PaymentCreateRequestInput"/>。
/// </summary>
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
