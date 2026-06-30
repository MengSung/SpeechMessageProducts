using System.Security.Cryptography;
using System.Text;
using SpeechMessage.Payments.Configuration;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 永豐 QPay 加解密工具。
/// AES key 由 A1/A2、B1/B2 做 XOR 後轉成大寫十六進位字串；
/// 這是舊 QPay Toolkit 的相容行為，不能改成一般 binary key 或小寫 hex。
/// </summary>
internal static class SinopacCrypto
{
    public static string BuildAesKey(PaymentMerchantProfile profile)
    {
        var a1 = ReadHalfKey(profile, "A1");
        var a2 = ReadHalfKey(profile, "A2");
        var b1 = ReadHalfKey(profile, "B1");
        var b2 = ReadHalfKey(profile, "B2");

        return ToHex(Xor(a1, a2), uppercase: true) + ToHex(Xor(b1, b2), uppercase: true);
    }

    public static string Encrypt(string aesKey, string data, string nonce)
    {
        var iv = DeriveIv(nonce);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.ASCII.GetBytes(aesKey);
        aes.IV = Encoding.ASCII.GetBytes(iv);

        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(data);
        return ToHex(encryptor.TransformFinalBlock(bytes, 0, bytes.Length), uppercase: true);
    }

    public static string Decrypt(string aesKey, string cipherText, string nonce)
    {
        var iv = DeriveIv(nonce);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.ASCII.GetBytes(aesKey);
        aes.IV = Encoding.ASCII.GetBytes(iv);

        using var decryptor = aes.CreateDecryptor();
        var bytes = FromHex(cipherText);
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(bytes, 0, bytes.Length));
    }

    private static byte[] ReadHalfKey(PaymentMerchantProfile profile, string key)
    {
        var value = SinopacRequestMapper.GetRequiredCredential(profile, key)
            .Replace("-", string.Empty)
            .Trim();

        if (value.Length < 16)
        {
            throw new PaymentConfigurationException(
                $"Sinopac profile '{profile.Name}' credential '{key}' must contain at least 16 hexadecimal characters.");
        }

        try
        {
            return FromHex(value[..16]);
        }
        catch (FormatException)
        {
            throw new PaymentConfigurationException(
                $"Sinopac profile '{profile.Name}' credential '{key}' must be hexadecimal.");
        }
    }

    private static string DeriveIv(string nonce)
    {
        // 永豐 IV 取 Nonce 的 SHA256 大寫 hex 最後 16 字元，需與銀行規格一致。
        var hash = ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)), uppercase: true);
        return hash[^16..];
    }

    private static byte[] Xor(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Sinopac key fragments must have the same length.");
        }

        var result = new byte[left.Length];
        for (var index = 0; index < left.Length; index++)
        {
            result[index] = (byte)(left[index] ^ right[index]);
        }

        return result;
    }

    private static byte[] FromHex(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            throw new FormatException("Sinopac hexadecimal value must have an even number of characters.");
        }

        var bytes = new byte[hex.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
        }

        return bytes;
    }

    private static string ToHex(byte[] bytes, bool uppercase = false)
    {
        var hex = Convert.ToHexString(bytes);
        return uppercase ? hex.ToUpperInvariant() : hex.ToLowerInvariant();
    }
}
