// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class MyPayRequestMapper
// 主要成員：MapCreatePayload、GetRequiredCredential、TryGetCredential、FormatAmount、GenerateIv、Encrypt、NormalizeKey、MapItems、ResolvePaymentMethod、GetMetadata
// 引用命名空間：System.Security.Cryptography、System.Text、Newtonsoft.Json、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    public static MyPayCreatePayload MapCreatePayload(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request)
    {
        return new MyPayCreatePayload
        {
            StoreUid = GetRequiredCredential(profile, "StoreId"),
            OrderId = request.ProductOrderId,
            Cost = FormatAmount(request.Amount),
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
            Currency = request.Currency,
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
            var amount = FormatAmount(request.Amount);
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
            var total = item.UnitPrice * quantity;
            return new MyPayCreateItemPayload
            {
                Id = (index + 1).ToString(),
                Name = FirstNonEmpty(item.Name, request.Description, request.ProductOrderId),
                Cost = FormatAmount(item.UnitPrice),
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
