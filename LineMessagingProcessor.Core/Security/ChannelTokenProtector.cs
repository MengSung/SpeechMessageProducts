/*
 * 檔案整體說明
 *
 * 目的與定位：P2-03雙目標Channel Token協定入口，供Plugin與受控主機共用，
 * 不負責查詢教會、選取Channel、授權或通知。輸入是單次借用key與秘密，
 * 輸出為v1封包或解密後的明文字串，任何拒絕使用固定安全例外。
 *
 * 主要功能與流程：依附錄A.5以AES-256-CBC／PKCS7加密，再以獨立key計算
 * HMAC-SHA256(ASCII(v1) || IV || 密文)。解密先驗完整MAC才建立解密器。
 * 封包限制4000字元、明文2927 UTF-8 bytes，拒絕空值、非canonical base64、
 * 錯版本／區塊／Unicode。較小的Channel secret欄位由寫入端另驗長度。
 *
 * 所有權與隔離：沒有static key、request、Session、教會狀態或cache；每次複製
 * 借入key，caller不得在呼叫中改寫原陣列。AES／HMAC／RNG／transform由當次
 * using釋放，當次key、明文byte與中間buffer在finally清零，沒有跨操作保留。
 * 輸入與回傳string由caller限制生命週期，CLR字串無法保證清零，禁止記錄或快取。
 *
 * 失敗與驗證：只將預期格式、編碼及crypto失敗轉成固定例外，不保存敏感inner；
 * 不吞掉記憶體耗盡等程序故障，也不自行通知。修改須重跑雙目標向量、認證後解密
 * 故障、併行A/B與有界資源測試；固定時間分支需另做程式審查，不能用耗時測試冒充。
 * Framework 使用沒有覆寫演算法的私有 HMAC 子類，避開已重現於本機 servicing 版本
 * 的 exact-type NativeHmac 靜態 CSP 卸載故障；仍由 Framework 選取符合政策的 hash，
 * 不改協定、不提高權限。此相容路徑依賴 runtime，升級時必須重跑受限域程序回歸。
 */
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace LineMessagingProcessor.Core.Security;

/// <summary>
/// 無狀態的雙目標Channel Token保護器；不解析設定或授權。每次使用新IV與操作自有
/// 子key／buffer，先認證再解密，caller保有原key及回傳字串的保密責任。
/// </summary>
public static class ChannelTokenProtector
{
    /// <summary>A.3持久欄位上限；解析前限制字串，避免不受限的分割與base64配置。</summary>
    private const int MaximumEnvelopeCharacters = 4000;

    /// <summary>可容納於4000字元v1封包的最大CBC密文，必須為16-byte區塊倍數。</summary>
    private const int MaximumCiphertextBytes = 2928;

    /// <summary>PKCS7至少一byte padding，故密文上限減一為最大明文byte數。</summary>
    private const int MaximumPlaintextBytes = MaximumCiphertextBytes - 1;

    /// <summary>唯讀使用的嚴格UTF-8編碼器；沒有每次操作狀態，不將錯誤字元靜默替換。</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>只含公開協定分隔符的私有唯讀表，Split不會修改；不含任何操作或使用者狀態。</summary>
    private static readonly char[] Separators = { ':' };

    /// <summary>
    /// 以新鮮RNG IV產生v1封包；完整候選在當次記憶體完成，沒有磁碟、網路或共享發布。
    /// key與明文byte只存於當次操作，成功／失敗均清零並Dispose演算法。
    /// </summary>
    /// <param name="plain">非空、至多2927 UTF-8 bytes的秘密；不修剪或正規化。</param>
    /// <param name="key64">精確64 bytes，前32 AES、後32 HMAC；呼叫期間不可改寫。</param>
    /// <returns>canonical base64的v1封包；內容仍應視為機密資料，不可記錄。</returns>
    /// <exception cref="ChannelTokenProtectionException">key、明文或crypto操作未通過。</exception>
    public static string Protect(string plain, byte[] key64)
    {
        byte[]? key = null, encryptionKey = null, authenticationKey = null;
        byte[]? plaintext = null, iv = null, ciphertext = null, mac = null;
        try
        {
            if (string.IsNullOrEmpty(plain) || plain.Length > MaximumPlaintextBytes
                || StrictUtf8.GetByteCount(plain) > MaximumPlaintextBytes)
                throw new ChannelTokenProtectionException();

            key = SnapshotKey(key64);
            encryptionKey = CopyHalf(key, 0);
            authenticationKey = CopyHalf(key, 32);
            plaintext = StrictUtf8.GetBytes(plain);
            iv = new byte[16];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(iv);

            using (var aes = CreateAes(encryptionKey, iv))
            using (var transform = aes.CreateEncryptor())
                ciphertext = transform.TransformFinalBlock(plaintext, 0, plaintext.Length);

            mac = ComputeMac(authenticationKey, iv, ciphertext);
            return "v1:" + Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(ciphertext)
                + ":" + Convert.ToBase64String(mac);
        }
        catch (CryptographicException) { throw new ChannelTokenProtectionException(); }
        catch (ArgumentException) { throw new ChannelTokenProtectionException(); }
        finally
        {
            Clear(mac);
            Clear(ciphertext);
            Clear(iv);
            Clear(plaintext);
            Clear(authenticationKey);
            Clear(encryptionKey);
            Clear(key);
        }
    }

    /// <summary>
    /// 在有界格式與完整MAC認證成功後才解密；padding／UTF-8拒絕與MAC失敗同型別同訊息，
    /// 沒有inner。只有完成所有驗證才發布明文，當次中間buffer於finally清零。
    /// </summary>
    /// <param name="envelope">至多4000字元、精確四部分的v1封包。</param>
    /// <param name="key64">精確64 bytes的借用key，操作期間不可被caller變更。</param>
    /// <returns>caller擁有的秘密字串；須限制scope，不能記錄／快取或跨教會保存。</returns>
    /// <exception cref="ChannelTokenProtectionException">任一格式、認證或解密驗證失敗。</exception>
    public static string Unprotect(string envelope, byte[] key64)
    {
        byte[]? key = null, encryptionKey = null, authenticationKey = null;
        byte[]? iv = null, ciphertext = null, suppliedMac = null, actualMac = null, plaintext = null;
        try
        {
            if (string.IsNullOrEmpty(envelope) || envelope.Length > MaximumEnvelopeCharacters)
                throw new ChannelTokenProtectionException();
            // 至多分成五份；即使輸入全是分隔符，也不配置數千份子字串。
            var parts = envelope.Split(Separators, 5, StringSplitOptions.None);
            if (parts.Length != 4 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
                throw new ChannelTokenProtectionException();

            key = SnapshotKey(key64);
            encryptionKey = CopyHalf(key, 0);
            authenticationKey = CopyHalf(key, 32);
            iv = DecodeCanonical(parts[1], 16);
            ciphertext = DecodeCanonical(parts[2], 0);
            suppliedMac = DecodeCanonical(parts[3], 32);
            if (ciphertext.Length == 0 || ciphertext.Length > MaximumCiphertextBytes || ciphertext.Length % 16 != 0)
                throw new ChannelTokenProtectionException();

            actualMac = ComputeMac(authenticationKey, iv, ciphertext);
            if (!MacEquals(actualMac, suppliedMac))
                throw new ChannelTokenProtectionException();

            // 必須在認證成功後才建立解密器；不可為了分類錯誤先試padding。
            using (var aes = CreateAes(encryptionKey, iv))
            using (var transform = aes.CreateDecryptor())
                plaintext = transform.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            if (plaintext.Length == 0 || plaintext.Length > MaximumPlaintextBytes)
                throw new ChannelTokenProtectionException();
            return StrictUtf8.GetString(plaintext);
        }
        catch (CryptographicException) { throw new ChannelTokenProtectionException(); }
        catch (ArgumentException) { throw new ChannelTokenProtectionException(); }
        catch (FormatException) { throw new ChannelTokenProtectionException(); }
        finally
        {
            Clear(plaintext);
            Clear(actualMac);
            Clear(suppliedMac);
            Clear(ciphertext);
            Clear(iv);
            Clear(authenticationKey);
            Clear(encryptionKey);
            Clear(key);
        }
    }

    /// <summary>
    /// net10委派官方固定時間API；net462以公開長度決定迴圈上限，逐byte XOR累加，
    /// 內容差異沒有提前返回。長度不同仍跑完較長陣列，零補位不會掩蓋長度差異。
    /// </summary>
    /// <param name="left">當次計算的MAC，不可為null。</param>
    /// <param name="right">當次收到的MAC，不可為null。</param>
    /// <returns>長度及每一byte相同才為true。</returns>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static bool MacEquals(byte[] left, byte[] right)
    {
#if NETFRAMEWORK
        var difference = left.Length ^ right.Length;
        var count = Math.Max(left.Length, right.Length);
        for (var i = 0; i < count; i++)
            difference |= (i < left.Length ? left[i] : 0) ^ (i < right.Length ? right[i] : 0);
        return difference == 0;
#else
        return CryptographicOperations.FixedTimeEquals(left, right);
#endif
    }

    /// <summary>驗證長度並複製borrowed key；回傳陣列唯一owner是本次Protect／Unprotect。</summary>
    /// <param name="key64">caller保有且呼叫中不可變更的key。</param>
    /// <returns>必須在finally清零的64-byte快照。</returns>
    private static byte[] SnapshotKey(byte[] key64)
    {
        if (key64 is null || key64.Length != 64)
            throw new ChannelTokenProtectionException();
        return (byte[])key64.Clone();
    }

    /// <summary>從已驗證快照複製一個32-byte子key，避免AES與HMAC共用同一key範圍。</summary>
    /// <param name="key">本次64-byte快照。</param>
    /// <param name="offset">固定0或32，由內部呼叫端選取。</param>
    /// <returns>當次owner在finally清零的子key。</returns>
    private static byte[] CopyHalf(byte[] key, int offset)
    {
        var result = new byte[32];
        Buffer.BlockCopy(key, offset, result, 0, result.Length);
        return result;
    }

    /// <summary>建立單次CBC演算法；若設定失敗則本函式Dispose，成功才將owner移交caller。</summary>
    /// <param name="key">單次32-byte AES key；provider自行複製，不接管此陣列。</param>
    /// <param name="iv">當次16-byte IV。</param>
    /// <returns>caller必須using釋放的AES。</returns>
    private static Aes CreateAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        try
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }
        catch
        {
            aes.Dispose();
            throw;
        }
    }

    /// <summary>計算版本／IV／密文的MAC；本函式擁有HMAC與輸入buffer，成功失敗均釋放／清零。</summary>
    /// <param name="key">當次HMAC子key，owner仍為外層操作。</param>
    /// <param name="iv">已驗長度的IV。</param>
    /// <param name="ciphertext">有界密文。</param>
    /// <returns>外層操作在finally清零的32-byte MAC。</returns>
    private static byte[] ComputeMac(byte[] key, byte[] iv, byte[] ciphertext)
    {
        var input = new byte[2 + iv.Length + ciphertext.Length];
        try
        {
            input[0] = (byte)'v';
            input[1] = (byte)'1';
            Buffer.BlockCopy(iv, 0, input, 2, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, input, 2 + iv.Length, ciphertext.Length);
#if NETFRAMEWORK
            using var hmac = new FrameworkCompatibleHmacSha256(key);
#else
            using var hmac = new HMACSHA256(key);
#endif
            return hmac.ComputeHash(input);
        }
        finally { Clear(input); }
    }

#if NETFRAMEWORK
    /// <summary>
    /// 委派 Framework 自有的繼承相容路徑，避免本機 exact-type 快速路徑的共用 CSP
    /// provider 在受限 AppDomain 卸載時釋放失敗。沒有演算法、政策或 Dispose 覆寫，
    /// 也沒有額外狀態；每次 ComputeMac 的 using 是唯一 owner，基底釋放 hash 與 key。
    /// 不固定 managed provider、不繞過 FIPS；平台更新須以程序退出及卸載回歸確認可用。
    /// </summary>
    private sealed class FrameworkCompatibleHmacSha256 : HMACSHA256
    {
        /// <summary>只將當次借用的認證子 key 交由基底複製，外層仍負責清零原陣列。</summary>
        /// <param name="key">當次 32-byte 認證子 key，不保存 caller 陣列或跨操作共用 instance。</param>
        internal FrameworkCompatibleHmacSha256(byte[] key) : base(key) { }
    }
#endif

    /// <summary>拒絕空白、非canonical padding與錯長度的base64；驗證失敗自行清零尚未移交的陣列。</summary>
    /// <param name="text">已由封包上限限制的部位字串。</param>
    /// <param name="expectedLength">IV／MAC固定byte數；0代表稍後依CBC契約檢查密文。</param>
    /// <returns>成功時移交外層finally清零的陣列。</returns>
    private static byte[] DecodeCanonical(string text, int expectedLength)
    {
        var result = Convert.FromBase64String(text);
        try
        {
            if ((expectedLength != 0 && result.Length != expectedLength)
                || !string.Equals(Convert.ToBase64String(result), text, StringComparison.Ordinal))
                throw new ChannelTokenProtectionException();
            return result;
        }
        catch
        {
            // 所有驗證／配置故障均尚未移交陣列，包含canonical重編碼失敗。
            Clear(result);
            throw;
        }
    }

    /// <summary>清零本次擁有的可變buffer；不接觸caller原key或CLR不可清零的輸入／回傳字串。</summary>
    /// <param name="bytes">已完成使用的當次陣列，可為null表示尚未配置。</param>
    private static void Clear(byte[]? bytes)
    {
        if (bytes is null) return;
#if NETFRAMEWORK
        Array.Clear(bytes, 0, bytes.Length);
#else
        CryptographicOperations.ZeroMemory(bytes);
#endif
    }
}
