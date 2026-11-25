/*
 * 檔案整體說明
 *
 * 目的與定位：金流通用核心對外公開的主要入口。宿主產品與未來產品透過 provider-neutral 操作建立／查詢付款、綁卡、維護訂單、定期扣款及解析回呼；
 * 不得直接依賴永豐、高鉅、台新 SDK、付款協定 DTO 或憑證 profile。
 *
 * 主要功能與流程：
 * 本檔宣告 IPaymentGateway。
 * 操作包含一般付款、綁卡建立與查詢、授權取消、立即請款、定期建立／查詢／停止及 callback parsing。
 * 所有操作以 profile 名稱為查找提示，由 Gateway 透過伺服器端 resolver 解析，再按 provider capability 路由。
 *
 * 使用邊界與維護：
 * profile 名稱與 ProviderHint 都不是授權來源；解析失敗或 capability 不支援時必須 fail closed，且不發出 provider I/O。
 * 綁卡查詢只公開 profile-bound authenticated ciphertext；明文 token 不得跨 provider 呼叫或進入 log。
 * provider HTTP client 由 Host/DI 管理；取消、request/response Dispose 與敏感資料生命週期由 provider 操作 owner 保證。
 * 變更介面、資料格式、隔離、取消或清理順序時，須一併核對 Gateway、provider 與契約測試。
 * 可沿 SpeechMessage.Payments/Abstractions/IPaymentProvider.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs、
 * SpeechMessage.Payments/Gateway/PaymentGateway.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 金流通用核心對外公開的主要入口。
/// 宿主產品與未來產品只能透過這個 provider-neutral 介面建立及查詢付款、
/// 管理綁卡／訂單／定期扣款並解析回呼；不得傳入已解析 profile、憑證或 provider 專屬 DTO。
/// </summary>
public interface IPaymentGateway
{
    /// <summary>依伺服器端解析的 profile 建立一次性付款；provider capability 與幣別檢查於任何 I/O 前完成。</summary>
    /// <param name="request">Money-first 的中立付款資料；profile 名稱只用於伺服器端設定查找。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>中立付款結果；不支援的 provider 或無效請求不會觸發付款 I/O。</returns>
    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>以單次 PayToken 查詢綁卡結果，成功時只回傳可驗證的 profile-bound 密文封裝。</summary>
    /// <param name="request">含伺服器端 profile 名稱、PayToken 與預期 BindNo 的查詢資料。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>綁卡狀態與密文；沒有 capability 時不發出 I/O。</returns>
    Task<CardBindQueryResult> QueryCardBindAsync(
        CardBindQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CardBindQueryResult
        {
            Status = PaymentStatus.Failed,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>建立銀行綁卡付款頁；成功只代表頁面建立，不代表使用者完成綁卡或付款。</summary>
    /// <param name="request">含伺服器產生 BindNo 與 HTTPS 回跳網址的中立請求。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>不含明文卡片 token 的綁卡頁結果。</returns>
    Task<CardBindResult> CreateCardBindAsync(
        CardBindRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CardBindResult
        {
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>要求 provider 取消指定訂單授權。</summary>
    /// <param name="request">含伺服器端 profile 名稱與穩定訂單識別的中立請求。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>只有銀行明確接受時才回報 Cancelled。</returns>
    Task<PaymentStatusResult> CancelAuthorizationAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentStatusResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = request.OrderNo,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>要求 provider 對指定訂單立即請款。</summary>
    /// <param name="request">含伺服器端 profile 名稱與穩定訂單識別的中立請求。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>只有銀行明確接受時才回報 Succeeded。</returns>
    Task<PaymentStatusResult> CaptureNowAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentStatusResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = request.OrderNo,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>依中立 Money-first 建單資料建立定期扣款方案。</summary>
    /// <param name="request">含 profile 名稱、付款資料及定期排程中繼資料的請求。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>定期方案的建立結果；不支援時不發出 I/O。</returns>
    Task<RecurringResult> CreateRecurringAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RecurringResult
        {
            Status = PaymentStatus.Failed,
            MasterOrderNo = request.ProductOrderId,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>查詢定期扣款主單及其完整、穩定識別的子扣款集合。</summary>
    /// <param name="request">含 profile 名稱與定期主單識別的中立查詢。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>驗證完成後發布的唯讀結果，或不含部分資料的失敗結果。</returns>
    Task<RecurringResult> QueryRecurringAsync(
        RecurringQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RecurringResult
        {
            Status = PaymentStatus.Failed,
            MasterOrderNo = request.MasterOrderNo,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>停止指定定期扣款方案。</summary>
    /// <param name="request">含 profile 名稱及定期主單識別的中立維護請求。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>只有銀行明確接受停止命令時才回報 Cancelled。</returns>
    Task<PaymentStatusResult> StopRecurringAsync(
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentStatusResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = request.OrderNo,
            Error = UnsupportedOperationError()
        });
    }

    /// <summary>以 provider 交易參照查詢付款狀態；回傳狀態不會自行更新產品持久資料。</summary>
    /// <param name="request">含伺服器端 profile 名稱、產品訂單及 provider 交易參照的中立查詢資料。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>經 provider 驗證並正規化的付款狀態結果。</returns>
    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>解析 provider callback 為中立查詢線索，不執行產品資料更新或通知。</summary>
    /// <param name="request">宿主已正規化的中立 callback 資料。</param>
    /// <param name="cancellationToken">由呼叫端提供的取消權杖。</param>
    /// <returns>標準 callback 結果。</returns>
    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 為尚未更新的 gateway 實作者提供安全預設值；這個分支不觸發任何 provider I/O，
    /// 讓新增 capability 不會因舊替身或外部實作者而誤宣稱已執行。
    /// </summary>
    /// <returns>固定的 UnsupportedOperation 錯誤。</returns>
    private static PaymentError UnsupportedOperationError()
    {
        return new PaymentError
        {
            Kind = PaymentErrorKind.UnsupportedOperation,
            Code = "UnsupportedOperation",
            Message = "The payment gateway does not support this operation."
        };
    }
}
