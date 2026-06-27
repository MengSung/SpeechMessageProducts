using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

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
            Items = MapItems(request),
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
        var encryptionKey = !string.IsNullOrWhiteSpace(agentId)
            ? TryGetCredential(profile, "AgentKey") ?? key
            : key;
        var iv = GenerateIv();

        var form = new Dictionary<string, string>
        {
            ["service"] = Encrypt(JsonConvert.SerializeObject(service, Formatting.None), encryptionKey, iv),
            ["encry_data"] = Encrypt(JsonConvert.SerializeObject(payload, Formatting.None), encryptionKey, iv)
        };

        if (string.IsNullOrWhiteSpace(agentId))
        {
            form["store_uid"] = payload.StoreUid;
        }
        else
        {
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
