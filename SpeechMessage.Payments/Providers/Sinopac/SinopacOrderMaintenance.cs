/*
 * 檔案整體說明
 *
 * 目的與定位：實作永豐 OrderMaintain capability，將中立取消授權、立即請款及停止定期扣款操作
 * 映射至固定的 C、P、E 命令；呼叫端不能自行指定銀行命令。
 *
 * 主要流程與依賴：
 * 先檢查本次 resolver profile、穩定 OrderNo 與命令，再透過同一 partial provider 的 Nonce/AES/Sign 傳輸送出。
 * 只有回應商店、訂單、命令及成功狀態全數相符時才發布成功狀態；provider 拒絕、簽章錯誤、逾時或網路錯誤
 * 會轉成不含原始本文與憑證的固定錯誤結果。呼叫端取消會原樣傳遞。
 *
 * 隔離與資源生命週期：
 * profile、訂單資料只在單次非同步操作中使用，不寫入靜態欄位、快取或共用 HttpClient 標頭。
 * HttpClient 由 Host/DI 擁有；每次 request/response 由共同傳輸 helper Dispose，取消或逾時後不保存重試狀態。
 * 本檔不執行 CRM、資料庫入帳或 callback 冪等；產品持久層仍須以資料庫唯一鍵及原子操作負責入帳。
 * 測試使用合成 profile 與記憶體 handler，不連線永豐或任何正式金流。
 */
using System.Security.Cryptography;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>永豐 provider 的 OrderMaintain capability partial，不保存跨請求交易狀態。</summary>
internal sealed partial class SinopacPaymentProvider
{
    /// <summary>以固定 C 命令取消授權；成功回應必須與本次 profile、訂單及命令一致。</summary>
    /// <param name="profile">Gateway 本次解析的永豐商店設定。</param>
    /// <param name="request">包含受信任穩定 OrderNo 的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞到 Nonce 與 Order I/O。</param>
    /// <returns>銀行確認取消後為 Cancelled；其餘情況為安全的失敗結果。</returns>
    public Task<PaymentStatusResult> CancelAuthorizationAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteOrderMaintenanceAsync(profile, request, "C", PaymentStatus.Cancelled, cancellationToken);
    }

    /// <summary>以固定 P 命令立即請款；只有銀行確認成功才回傳 Succeeded。</summary>
    /// <param name="profile">Gateway 本次解析的永豐商店設定。</param>
    /// <param name="request">包含受信任穩定 OrderNo 的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞到 Nonce 與 Order I/O。</param>
    /// <returns>銀行確認請款後為 Succeeded；其餘情況為安全的失敗結果。</returns>
    public Task<PaymentStatusResult> CaptureNowAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteOrderMaintenanceAsync(profile, request, "P", PaymentStatus.Succeeded, cancellationToken);
    }

    /// <summary>
    /// 執行固定命令並驗證回應關聯。request/response 的唯一資源 owner 是共同 HTTP helper；
    /// 任何 profile、OrderNo 或 command 不匹配都在發布狀態前 fail closed。
    /// </summary>
    /// <param name="profile">本次解析的單一永豐商店設定。</param>
    /// <param name="request">操作識別。</param>
    /// <param name="command">僅允許此檔呼叫端指定的 C、P 或 E。</param>
    /// <param name="successStatus">銀行明確成功後的中立狀態。</param>
    /// <param name="cancellationToken">呼叫端取消權杖。</param>
    /// <returns>中立訂單操作結果。</returns>
    private async Task<PaymentStatusResult> ExecuteOrderMaintenanceAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        string command,
        PaymentStatus successStatus,
        CancellationToken cancellationToken)
    {
        if (!IsMatchingProfile(profile, request.ProfileName) ||
            !IsValidIdentifier(request.OrderNo) ||
            command is not ("C" or "P" or "E"))
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.RequestInvalid,
                "OrderMaintainRequestInvalid");
        }

        try
        {
            var shopNo = SinopacRequestMapper.GetRequiredCredential(profile, "ShopNo");
            var response = await SendOrderAsync<
                SinopacOrderMaintainRequest,
                SinopacOrderMaintainResponse>(
                    profile,
                    new SinopacOrderMaintainRequest
                    {
                        ShopNo = shopNo,
                        OrderNo = request.OrderNo,
                        Command = command
                    },
                    SinopacApiService.OrderMaintain,
                    cancellationToken);

            if (!string.Equals(response.ShopNo, shopNo, StringComparison.Ordinal) ||
                !string.Equals(response.OrderNo, request.OrderNo, StringComparison.Ordinal) ||
                !string.Equals(response.Command, command, StringComparison.Ordinal) ||
                !SinopacStatusMapper.IsSuccessStatus(response.Status))
            {
                return CreateOrderMaintenanceFailure(
                    request.OrderNo,
                    PaymentErrorKind.ProviderRejected,
                    "OrderMaintainResponseMismatch");
            }

            return new PaymentStatusResult
            {
                Status = successStatus,
                ProductOrderId = request.OrderNo,
                ProviderTransactionId = response.TSNo
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentConfigurationException)
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.ConfigurationInvalid,
                "ConfigurationInvalid");
        }
        catch (SinopacRequestException exception)
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                exception.ErrorKind,
                exception.DiagnosticCode);
        }
        catch (SinopacSignatureException)
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.SignatureInvalid,
                "SignatureInvalid");
        }
        catch (OperationCanceledException)
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.NetworkFailure,
                "OrderMaintainTimeout");
        }
        catch (Exception exception) when (IsNetworkTransportException(exception))
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.NetworkFailure,
                "OrderMaintainTransport");
        }
        catch (Exception exception) when (
            exception is JsonException or CryptographicException or FormatException or OverflowException)
        {
            return CreateOrderMaintenanceFailure(
                request.OrderNo,
                PaymentErrorKind.SerializationFailure,
                "OrderMaintainResponseUnreadable");
        }
    }

    /// <summary>建立不含銀行本文、卡片資料或憑證的訂單維護失敗結果。</summary>
    /// <param name="orderNo">本次操作的穩定產品訂單識別。</param>
    /// <param name="kind">標準化錯誤分類。</param>
    /// <param name="code">固定或 provider helper 產生的安全診斷碼。</param>
    /// <returns>狀態固定為 Failed 的中立結果。</returns>
    private static PaymentStatusResult CreateOrderMaintenanceFailure(
        string orderNo,
        PaymentErrorKind kind,
        string code)
    {
        return new PaymentStatusResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = orderNo,
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
                Message = code
            }
        };
    }
}
