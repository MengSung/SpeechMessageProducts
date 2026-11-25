/*
 * 檔案整體說明
 *
 * 目的與定位：實作永豐 CardBindCreate 與以 OrderPayQuery 讀取 CardBindOut 的 provider capability；
 * 同檔的 protector 將查詢所得明文 BindToken 轉成 profile-bound、可驗證的 AES-GCM 封裝。
 *
 * 主要功能與流程：
 * 先驗證 profile 名稱、穩定 BindNo、PayToken 與 HTTPS 回跳網址，再依 QPay V2.6 組 request。
 * 查詢時驗證外層 response 簽章、商店、PayToken、CardBindOut、內層 ShopNo 與預期 BindNo；
 * 因 V2.6 外層簽章不涵蓋巢狀 TSResultContent，內層值只視為受 HTTPS 保護的回應資料，不虛構其有獨立銀行簽章。
 * 綁卡 token 經單次 AEAD 保護後才跨核心邊界；重用時解封驗證 profile、ShopNo、BindNo 與 tag，錯誤在任何 OrderCreate I/O 前拒絕。
 *
 * 使用邊界與維護：
 * 明文 token、AES 派生金鑰與明文字節只在本次操作短暫建立，不寫 log、診斷、靜態欄位或 provider cache；
 * 產品只收到密文，且憑證輪替會使舊密文失效。永豐 token 的銀行效期尚未確認，呼叫端須依銀行正式規則清除持久化密文。
 * 本 partial provider 借用原 provider 的 DI HttpClient；request／response 由 SendOrderAsync 關閉，呼叫端取消原樣傳遞。
 * 測試以固定合成 profile 與記憶體 handler 注入密文變造及跨 profile 重用，不連線金流。
 */
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>永豐 provider 的綁卡 capability partial；不持有額外 client、profile 或跨請求 token 狀態。</summary>
internal sealed partial class SinopacPaymentProvider
{
    /// <summary>
    /// 建立 NT$1 綁卡授權頁；payload 只建立授權，不呼叫請款命令。
    /// 銀行回應必須匹配本次 ShopNo、BindNo 且提供 HTTPS 付款頁後才發布 Pending。
    /// </summary>
    /// <param name="profile">Gateway 已解析、只供本次操作借用的永豐 profile。</param>
    /// <param name="request">受信任產品服務提供的綁卡識別與 HTTPS 回跳網址。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳給 Nonce 與 Order HTTP 傳輸。</param>
    /// <returns>不包含明文綁卡 token 的綁卡頁結果。</returns>
    public async Task<CardBindResult> CreateCardBindAsync(
        PaymentMerchantProfile profile,
        CardBindRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsMatchingProfile(profile, request.ProfileName) ||
            !IsValidIdentifier(request.BindNo) ||
            !IsAbsoluteHttpsUri(request.ReturnUrl))
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.RequestInvalid,
                "CardBindRequestInvalid");
        }

        SinopacCardBindCreateRequest payload;
        try
        {
            payload = new SinopacCardBindCreateRequest
            {
                ShopNo = SinopacRequestMapper.GetRequiredCredential(profile, "ShopNo"),
                BindNo = request.BindNo,
                Amount = 100,
                CurrencyID = "TWD",
                ReturnURL = request.ReturnUrl
            };

            var response = await SendOrderAsync<
                SinopacCardBindCreateRequest,
                SinopacCardBindCreateResponse>(
                    profile,
                    payload,
                    SinopacApiService.CardBindCreate,
                    cancellationToken);

            if (!string.Equals(response.ShopNo, payload.ShopNo, StringComparison.Ordinal) ||
                !string.Equals(response.BindNo, request.BindNo, StringComparison.Ordinal) ||
                !SinopacStatusMapper.IsSuccessStatus(response.Status) ||
                !IsAbsoluteHttpsUri(response.CardBindURL))
            {
                return CreateCardBindingFailure(
                    request.BindNo,
                    PaymentErrorKind.SignatureInvalid,
                    "CardBindResponseMismatch");
            }

            return new CardBindResult
            {
                Status = PaymentStatus.Pending,
                BindNo = request.BindNo,
                PaymentPageUrl = response.CardBindURL
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentConfigurationException)
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.ConfigurationInvalid,
                "ConfigurationInvalid");
        }
        catch (SinopacRequestException exception)
        {
            return CreateCardBindingFailure(
                request.BindNo,
                exception.ErrorKind,
                exception.DiagnosticCode);
        }
        catch (SinopacSignatureException)
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.SignatureInvalid,
                "SignatureInvalid");
        }
        catch (OperationCanceledException)
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.NetworkFailure,
                "CardBindTimeout");
        }
        catch (Exception exception) when (IsNetworkTransportException(exception))
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.NetworkFailure,
                "CardBindTransport");
        }
        catch (Exception exception) when (
            exception is JsonException or CryptographicException or FormatException or OverflowException)
        {
            return CreateCardBindingFailure(
                request.BindNo,
                PaymentErrorKind.SerializationFailure,
                "CardBindResponseUnreadable");
        }
    }

    /// <summary>
    /// 查詢綁卡結果。外層 Sign 只驗證其實際涵蓋的頂層純量欄位；巢狀 BindToken 依賴 HTTPS 傳輸保護，
    /// 並於本次處理中驗證商店及 BindNo 後立即以本機 AES-GCM 封裝。
    /// </summary>
    /// <param name="profile">Gateway 已解析、只供本次操作與密鑰衍生使用的永豐 profile。</param>
    /// <param name="request">含本次 PayToken 與預期 BindNo 的中立查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳給 Nonce 與 Order HTTP 傳輸。</param>
    /// <returns>成功時只回傳 profile-bound authenticated ciphertext；任何關聯失敗都不發布 token。</returns>
    public async Task<CardBindQueryResult> QueryCardBindAsync(
        PaymentMerchantProfile profile,
        CardBindQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsMatchingProfile(profile, request.ProfileName) ||
            !IsValidIdentifier(request.ExpectedBindNo) ||
            !IsValidIdentifier(request.PayToken))
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.RequestInvalid,
                "CardBindQueryRequestInvalid");
        }

        try
        {
            var shopNo = SinopacRequestMapper.GetRequiredCredential(profile, "ShopNo");
            var response = await SendOrderAsync<
                SinopacOrderPayQueryRequest,
                SinopacOrderPayResponse>(
                    profile,
                    SinopacRequestMapper.MapOrderPayQuery(
                        profile,
                        new PaymentQueryRequest
                        {
                            ProfileName = profile.Name,
                            ProviderHint = PaymentProviderKind.Sinopac,
                            ProviderOrderRef = request.PayToken
                        }),
                    SinopacApiService.OrderPayQuery,
                    cancellationToken);

            var transaction = response.TSResultContent;
            try
            {
                if (!SinopacStatusMapper.IsSuccessStatus(response.Status) ||
                    !string.Equals(response.ShopNo, shopNo, StringComparison.Ordinal) ||
                    !string.Equals(response.PayToken, request.PayToken, StringComparison.Ordinal) ||
                    transaction is null ||
                    !string.Equals(transaction.APType, "CardBindOut", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(transaction.ShopNo, shopNo, StringComparison.Ordinal) ||
                    !string.Equals(transaction.BindNo, request.ExpectedBindNo, StringComparison.Ordinal) ||
                    !SinopacStatusMapper.IsSuccessStatus(transaction.Status) ||
                    string.IsNullOrWhiteSpace(transaction.BindToken))
                {
                    return CreateCardBindingQueryFailure(
                        request.ExpectedBindNo,
                        PaymentErrorKind.SignatureInvalid,
                        "CardBindResponseMismatch");
                }

                var protectedBindToken = SinopacBindTokenProtector.Protect(
                    profile,
                    shopNo,
                    request.ExpectedBindNo,
                    transaction.BindToken);

                return new CardBindQueryResult
                {
                    Status = PaymentStatus.Succeeded,
                    BindNo = request.ExpectedBindNo,
                    ProtectedBindToken = protectedBindToken
                };
            }
            finally
            {
                if (transaction is not null)
                {
                    transaction.BindToken = string.Empty;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentConfigurationException)
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.ConfigurationInvalid,
                "ConfigurationInvalid");
        }
        catch (SinopacRequestException exception)
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                exception.ErrorKind,
                exception.DiagnosticCode);
        }
        catch (SinopacSignatureException)
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.SignatureInvalid,
                "SignatureInvalid");
        }
        catch (OperationCanceledException)
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.NetworkFailure,
                "CardBindQueryTimeout");
        }
        catch (Exception exception) when (IsNetworkTransportException(exception))
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.NetworkFailure,
                "CardBindQueryTransport");
        }
        catch (Exception exception) when (
            exception is JsonException or CryptographicException or FormatException or OverflowException)
        {
            return CreateCardBindingQueryFailure(
                request.ExpectedBindNo,
                PaymentErrorKind.SerializationFailure,
                "CardBindResponseUnreadable");
        }
    }

    private static bool IsMatchingProfile(PaymentMerchantProfile profile, string? requestedProfileName)
    {
        return string.IsNullOrWhiteSpace(requestedProfileName) ||
            string.Equals(profile.Name, requestedProfileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 驗證供永豐協定關聯使用的識別字串。只接受有界、非空且不含換行或 NUL 的值，
    /// 避免錯誤資料進入 request 欄位、密文關聯資料或診斷格式。
    /// </summary>
    /// <param name="value">單次操作中的商店、綁卡或付款識別。</param>
    /// <returns>值符合此 provider 的識別字元邊界時為 true。</returns>
    internal static bool IsValidIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Length <= 256 &&
            value.IndexOfAny(['\r', '\n', '\0']) < 0;
    }

    private static bool IsAbsoluteHttpsUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Fragment);
    }

    private static CardBindResult CreateCardBindingFailure(
        string bindNo,
        PaymentErrorKind kind,
        string code)
    {
        return new CardBindResult
        {
            Status = PaymentStatus.Failed,
            BindNo = bindNo,
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
                Message = code
            }
        };
    }

    private static CardBindQueryResult CreateCardBindingQueryFailure(
        string bindNo,
        PaymentErrorKind kind,
        string code)
    {
        return new CardBindQueryResult
        {
            Status = PaymentStatus.Failed,
            BindNo = bindNo,
            ProtectedBindToken = string.Empty,
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
                Message = code
            }
        };
    }
}

/// <summary>
/// 為跨核心邊界的 BindToken 提供版本化 AES-GCM 封裝。Key 由本 profile 的 QPay 商店金鑰做 domain-separated 衍生；
/// 附加驗證資料綁定 profile 名稱、ShopNo 及 BindNo。任何 profile／商店／綁定變更或 tag 錯誤都會拒絕解封。
/// </summary>
internal static class SinopacBindTokenProtector
{
    private const string EnvelopeVersion = "v1";
    private const string KeyDerivationContext = "SpeechMessage.Payments.Sinopac.BindToken.v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumTokenBytes = 4096;
    private const int MaximumEnvelopeCharacters = 8192;

    /// <summary>加密單次查詢所得的 BindToken，回傳版本、nonce、密文與 tag 的 Base64Url 表示。</summary>
    /// <param name="profile">本次 resolver 選出的 profile；只供 key derivation 與 AAD 使用。</param>
    /// <param name="shopNo">必須與本次已驗證銀行回應商店相同。</param>
    /// <param name="bindNo">必須與預期業務綁卡識別相同。</param>
    /// <param name="bindToken">僅供本次呼叫的明文銀行 token，不由 protector 保存。</param>
    /// <returns>不含可讀 token 的版本化 authenticated ciphertext。</returns>
    /// <exception cref="CryptographicException">profile 金鑰無效或 token 超過有界大小。</exception>
    public static string Protect(
        PaymentMerchantProfile profile,
        string shopNo,
        string bindNo,
        string bindToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindToken);
        ValidateBindingContext(profile, shopNo, bindNo);
        byte[]? plaintext = null;
        byte[]? key = null;
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? associatedData = null;
        try
        {
            plaintext = Encoding.UTF8.GetBytes(bindToken);
            if (plaintext.Length > MaximumTokenBytes)
            {
                throw new CryptographicException("Protected BindToken exceeds the configured bound.");
            }

            key = DeriveKey(profile);
            nonce = RandomNumberGenerator.GetBytes(NonceSize);
            ciphertext = new byte[plaintext.Length];
            tag = new byte[TagSize];
            associatedData = BuildAssociatedData(profile.Name, shopNo, bindNo);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return string.Join(
                '.',
                EnvelopeVersion,
                ToBase64Url(nonce),
                ToBase64Url(ciphertext),
                ToBase64Url(tag));
        }
        finally
        {
            ZeroMemory(plaintext);
            ZeroMemory(key);
            ZeroMemory(nonce);
            ZeroMemory(ciphertext);
            ZeroMemory(tag);
            ZeroMemory(associatedData);
        }
    }

    /// <summary>
    /// 驗證密文格式、profile 派生金鑰、nonce/tag 與 AAD 後解封 token。
    /// 呼叫端須只在單次 OrderCreate 組包期間持有回傳字串，並在 finally 清空 DTO 欄位。
    /// </summary>
    /// <param name="profile">本次 resolver 選出的 profile。</param>
    /// <param name="shopNo">解封時的 profile 商店識別。</param>
    /// <param name="bindNo">解封時的預期綁卡識別。</param>
    /// <param name="protectedToken">由此 protector 產生的版本化封裝。</param>
    /// <returns>僅供目前 provider 呼叫使用的明文 BindToken。</returns>
    /// <exception cref="CryptographicException">格式、profile、商店、BindNo 或 authentication tag 不匹配。</exception>
    public static string Unprotect(
        PaymentMerchantProfile profile,
        string shopNo,
        string bindNo,
        string protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken) ||
            protectedToken.Length > MaximumEnvelopeCharacters)
        {
            throw new CryptographicException("Protected BindToken envelope is invalid.");
        }

        ValidateBindingContext(profile, shopNo, bindNo);
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? key = null;
        byte[]? plaintext = null;
        byte[]? associatedData = null;
        try
        {
            var segments = protectedToken.Split('.');
            if (segments.Length != 4 ||
                !string.Equals(segments[0], EnvelopeVersion, StringComparison.Ordinal))
            {
                throw new CryptographicException("Protected BindToken envelope version is invalid.");
            }

            try
            {
                nonce = FromBase64Url(segments[1]);
                ciphertext = FromBase64Url(segments[2]);
                tag = FromBase64Url(segments[3]);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("Protected BindToken envelope encoding is invalid.", exception);
            }

            if (nonce.Length != NonceSize ||
                tag.Length != TagSize ||
                ciphertext.Length is 0 or > MaximumTokenBytes)
            {
                throw new CryptographicException("Protected BindToken envelope dimensions are invalid.");
            }

            key = DeriveKey(profile);
            plaintext = new byte[ciphertext.Length];
            associatedData = BuildAssociatedData(profile.Name, shopNo, bindNo);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            try
            {
                return new UTF8Encoding(false, true).GetString(plaintext);
            }
            catch (DecoderFallbackException exception)
            {
                throw new CryptographicException("Protected BindToken plaintext encoding is invalid.", exception);
            }
        }
        finally
        {
            ZeroMemory(key);
            ZeroMemory(plaintext);
            ZeroMemory(nonce);
            ZeroMemory(ciphertext);
            ZeroMemory(tag);
            ZeroMemory(associatedData);
        }
    }

    private static byte[] DeriveKey(PaymentMerchantProfile profile)
    {
        var signingKey = Encoding.ASCII.GetBytes(SinopacCrypto.BuildAesKey(profile));
        var context = Encoding.UTF8.GetBytes(KeyDerivationContext);
        try
        {
            return HMACSHA256.HashData(signingKey, context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
            CryptographicOperations.ZeroMemory(context);
        }
    }

    private static byte[] BuildAssociatedData(string profileName, string shopNo, string bindNo)
    {
        byte[]? profileBytes = null;
        byte[]? shopBytes = null;
        byte[]? bindBytes = null;
        try
        {
            profileBytes = Encoding.UTF8.GetBytes(profileName);
            shopBytes = Encoding.UTF8.GetBytes(shopNo);
            bindBytes = Encoding.UTF8.GetBytes(bindNo);
            var output = new byte[12 + profileBytes.Length + shopBytes.Length + bindBytes.Length];
            var span = output.AsSpan();
            BinaryPrimitives.WriteInt32BigEndian(span, profileBytes.Length);
            profileBytes.CopyTo(span[4..]);
            var shopOffset = 4 + profileBytes.Length;
            BinaryPrimitives.WriteInt32BigEndian(span[shopOffset..], shopBytes.Length);
            shopBytes.CopyTo(span[(shopOffset + 4)..]);
            var bindOffset = shopOffset + 4 + shopBytes.Length;
            BinaryPrimitives.WriteInt32BigEndian(span[bindOffset..], bindBytes.Length);
            bindBytes.CopyTo(span[(bindOffset + 4)..]);
            return output;
        }
        finally
        {
            ZeroMemory(profileBytes);
            ZeroMemory(shopBytes);
            ZeroMemory(bindBytes);
        }
    }

    private static void ValidateBindingContext(
        PaymentMerchantProfile profile,
        string shopNo,
        string bindNo)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) ||
            profile.Name.Length > 256 ||
            !SinopacPaymentProvider.IsValidIdentifier(shopNo) ||
            !SinopacPaymentProvider.IsValidIdentifier(bindNo))
        {
            throw new CryptographicException("Protected BindToken binding context is invalid.");
        }
    }

    private static void ZeroMemory(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        if (value.Length == 0 ||
            value.Any(character =>
                !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            throw new FormatException("Protected BindToken base64url segment is invalid.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Protected BindToken base64url length is invalid.")
        };
        var decoded = Convert.FromBase64String(base64);
        if (!string.Equals(ToBase64Url(decoded), value, StringComparison.Ordinal))
        {
            ZeroMemory(decoded);
            throw new FormatException("Protected BindToken base64url segment is not canonical.");
        }

        return decoded;
    }
}
