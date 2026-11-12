/*
 * 檔案整體說明
 *
 * 目的與定位：將 Money-first PaymentCreateRequest 轉成 MyPay 建單 contract。外層 form 與內層 encry_data 的欄位敏感，
 * 因此此 mapper 同時集中處理中立金額及 MyPay 表單相容格式。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPayRequestMapper。
 * 主要操作入口包含 MapCreatePayload、MapCreateForm、GetRequiredCredential、TryGetCredential、FormatAmount、GenerateIv、Encrypt、
 * NormalizeKey；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * payload 金額只由 request.Total 與 item.Price 的 Money 讀取；profile 憑證僅在本次加密過程借用，不寫入 mapper 或結果。
 * 加密 key、IV 與明細只於單次 mapping 建立；任何失敗向 caller 傳遞，不保存跨請求的商店或付款人狀態。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// 將 provider-neutral PaymentCreateRequest 轉成 MyPay 建單 contract。
/// MyPay 對外層 form 與內層 encry_data 的欄位非常敏感，錯誤欄位常被回報成
/// 「金鑰過期或使用錯誤金鑰」，因此這個 mapper 是高鉅相容性的主要保護點。
/// </summary>
internal static class MyPayRequestMapper
{
    /// <summary>以 Money 總額與商品單價建立 MyPay 中立 payload，將 provider 幣別契約轉成 MyPay 欄位。</summary>
    /// <param name="profile">resolver 選定的 MyPay 商店設定；只在本次映射讀取。</param>
    /// <param name="request">Gateway 已驗證支援幣別的付款請求。</param>
    /// <returns>供加密與傳輸使用的 MyPay payload。</returns>
    public static MyPayCreatePayload MapCreatePayload(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        return new MyPayCreatePayload
        {
            StoreUid = GetRequiredCredential(profile, "StoreId"),
            OrderId = request.ProductOrderId,
            Cost = FormatAmount(request.Total.Amount),
            // api/orders 必須有 items；宿主產品未提供明細時由 MapItems 建立單筆相容資料。
            Items = MapItems(request),
            // user_id 是 MyPay 必填消費者識別；優先取產品層傳入的 UserId，再退回付款者資料。
            UserId = FirstNonEmpty(
                GetMetadata(request, "UserId"),
                request.Customer.Name,
                request.Customer.Email,
                request.ProductOrderId),
            Ip = FirstNonEmpty(
                GetMetadata(request, "Ip"),
                GetProfileSetting(profile, "IP"),
                "127.0.0.1"),
            Currency = request.Total.Currency,
            ProductName = request.Description,
            // PaymentMethod 這裡會轉成 MyPay pfn，不可直接沿用 Sinopac/QPay 的 C/A/M/L 語意。
            PaymentMethod = ResolvePaymentMethod(profile, request),
            UserName = request.Customer.Name,
            UserEmail = request.Customer.Email,
            UserPhone = request.Customer.Phone,
            SuccessReturnUrl = request.Callbacks.SuccessUrl,
            FailureReturnUrl = request.Callbacks.FailureUrl,
            NotifyUrl = request.Callbacks.BackendUrl
        };
    }

    /// <summary>建立 MyPay 表單欄位與加密資料；密鑰只在此呼叫使用，不寫入結果或共用狀態。</summary>
    /// <param name="profile">resolver 選定的商店設定與憑證。</param>
    /// <param name="request">Money-first 的中立付款請求。</param>
    /// <returns>待單次 HTTP 呼叫使用的表單欄位字典。</returns>
    public static IReadOnlyDictionary<string, string> MapCreateForm(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        var payload = MapCreatePayload(profile, request);
        var service = new MyPayServicePayload();
        var key = GetRequiredCredential(profile, "Key");
        var agentId = TryGetCredential(profile, "AgentId");
        // 有 AgentId 才是經銷/代理商模式；否則 direct merchant 必須只送 top-level store_uid。
        var encryptionKey = !string.IsNullOrWhiteSpace(agentId)
            ? TryGetCredential(profile, "AgentKey") ?? key
            : key;
        var iv = GenerateIv();

        var form = new Dictionary<string, string>
        {
            // service 與 encry_data 都使用同一把 MyPay 金鑰加密；IV 會前綴在密文後一起 base64。
            ["service"] = Encrypt(JsonConvert.SerializeObject(service, Formatting.None), encryptionKey, iv),
            ["encry_data"] = Encrypt(JsonConvert.SerializeObject(payload, Formatting.None), encryptionKey, iv)
        };

        if (string.IsNullOrWhiteSpace(agentId))
        {
            // /api/init direct merchant contract：外層只能放 store_uid，不可同時放 agent_uid。
            form["store_uid"] = payload.StoreUid;
        }
        else
        {
            // /api/agent reseller contract：外層只能放 agent_uid，商店 store_uid 留在加密 payload 內。
            form["agent_uid"] = agentId;
        }

        return form;
    }

    private static string GetRequiredCredential(PaymentMerchantProfile profile, string key)
    {
        if (profile.Credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new PaymentConfigurationException($"MyPay profile '{profile.Name}' is missing credential '{key}'.");
    }

    private static string? TryGetCredential(PaymentMerchantProfile profile, string key)
    {
        return profile.Credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string FormatAmount(decimal amount)
    {
        return amount % 1 == 0
            ? decimal.ToInt64(amount).ToString()
            : amount.ToString("0.##");
    }

    private static byte[] GenerateIv()
    {
        return RandomNumberGenerator.GetBytes(16);
    }

    private static string Encrypt(string data, string key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = NormalizeKey(key);
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(Encoding.UTF8.GetBytes(data), 0, Encoding.UTF8.GetByteCount(data));
        return Convert.ToBase64String(iv.Concat(encrypted).ToArray());
    }

    private static byte[] NormalizeKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length is 16 or 24 or 32)
        {
            return bytes;
        }

        return SHA256.HashData(bytes);
    }

    private static IReadOnlyList<MyPayCreateItemPayload> MapItems(PaymentCreateRequest request)
    {
        if (request.Items.Count == 0)
        {
            var amount = FormatAmount(request.Total.Amount);
            // 舊版產品流程只有單筆商品；保留這個 fallback 可避免 encry_data 缺 items。
            return new[]
            {
                new MyPayCreateItemPayload
                {
                    Id = FirstNonEmpty(GetMetadata(request, "Param1"), request.ProductOrderId),
                    Name = FirstNonEmpty(request.Description, request.ProductOrderId),
                    Cost = amount,
                    Amount = "1",
                    Total = amount
                }
            };
        }

        return request.Items.Select((item, index) =>
        {
            var quantity = item.Quantity <= 0 ? 1 : item.Quantity;
            var total = item.Price.Amount * quantity;
            return new MyPayCreateItemPayload
            {
                Id = (index + 1).ToString(),
                Name = FirstNonEmpty(item.Name, request.Description, request.ProductOrderId),
                Cost = FormatAmount(item.Price.Amount),
                Amount = quantity.ToString(),
                Total = FormatAmount(total)
            };
        }).ToArray();
    }

    private static string ResolvePaymentMethod(PaymentMerchantProfile profile, PaymentCreateRequest request)
    {
        // PFN 是 MyPay payment-function，不是宿主產品或永豐的 pay type。
        // 允許 profile 或 metadata 明確指定，支援未來不同產品調整顯示的付款工具。
        var configuredPfn = FirstNonEmpty(
            GetMetadata(request, "PFN"),
            GetProfileSetting(profile, "PFN"));
        if (!string.IsNullOrWhiteSpace(configuredPfn))
        {
            return configuredPfn;
        }

        return request.PaymentMethod?.Trim().ToUpperInvariant() switch
        {
            "L" or "LINEPAY" or "LINEPAYON" => "LINEPAYON",
            "M" or "MOBILEPAY" => "MobilePayAll",
            "A" or "ATM" or "E_COLLECTION" => "E_COLLECTION",
            // 舊版 MyPay 信用卡流程使用 pfn=0，讓 MyPay 顯示商店啟用的付款工具。
            "C" or "CREDITCARD" or "CUP" => "0",
            _ => "0"
        };
    }

    private static string GetMetadata(PaymentCreateRequest request, string key)
    {
        return request.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string GetProfileSetting(PaymentMerchantProfile profile, string key)
    {
        return profile.Settings.TryGetValue(key, out var value) ? value : string.Empty;
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
