/*
 * 檔案整體說明
 *
 * 目的與定位：保護P2-03的雙目標Channel Token封包，不接觸真實設定、D365或LINE。
 * 共用向量僅含公開合成key與明文；先由獨立Node crypto產生，再加入兩個真實產品
 * framework各自產生的封包，讓兩宿主重新解密以證明雙向相容。
 *
 * 主要功能與流程：確認net48宿主實載net462，驗證向量、亂數IV、MAC與版本綁定、
 * 篡改、錯key、格式／大小／Unicode拒絕及固定例外；以有效MAC包住無效padding或
 * UTF-8，驗證認證後解密失敗仍不洩密。兩worker於每輪barrier會合後使用不同key，
 * 檢查任何跨操作key或明文污染。獨立集合量測重複操作後的受控資源基線。
 *
 * 資源擁有權與隔離：每案擁有crypto、barrier與Process，using後才量測／結束；
 * byte[]均為合成資料，不保存真實憑證。輸出TokenExchange僅供本任務合成跨目標向量
 * 收集，不是產品日誌。測試不建立cache、背景服務或網路；故障由xUnit原樣報告。
 * 修改協定須同時重跑兩framework與Core獨立覆蓋率，不能只以本機roundtrip作驗收。
 */
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LineMessagingProcessor.Core.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LineMessagingProcessor.Tests.Security;

/// <summary>
/// 將會強制GC與量測handle的合成加密案例排除於其他案例的平行執行之外。
/// 只影響測試排程，不改產品同步方式或生命週期；兩宿主仍有獨立程序。
/// </summary>
[CollectionDefinition("Channel token lifecycle", DisableParallelization = true)]
public sealed class ChannelTokenLifecycleCollection
{
}

/// <summary>
/// 以真實AES／HMAC與公開合成fixture保護封包協定。所有失敗必須是固定型別、
/// 固定訊息且沒有inner；任何錯誤都不以日誌或網路洩漏輸入。
/// </summary>
[Collection("Channel token lifecycle")]
public sealed class ChannelTokenProtectorTests
{
    /// <summary>公開合成明文，不是有效LINE token，跨目標收集僅限此常值。</summary>
    private const string Plain = "synthetic-channel-token";

    /// <summary>只用於輸出合成跨目標向量的當案xUnit writer，沒有產品logger或靜態持有。</summary>
    private readonly ITestOutputHelper output;

    /// <summary>借用當案writer，輸出限定為合成fixture；案例結束由xUnit釋放。</summary>
    /// <param name="output">xUnit提供的測試輸出接收器。</param>
    public ChannelTokenProtectorTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// 以實際載入產品組件確認net462與net10分支，讀共同向量並逐一解密。
    /// 自產封包先本地roundtrip，再輸出合成封包供另一宿主重跑，不把宿主net48當產品net48。
    /// </summary>
    [Fact]
    public void RoundTrip_Net462AndNet10_Compatible()
    {
        var framework = typeof(ChannelTokenProtector).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!;
#if NETFRAMEWORK
        Assert.Equal(".NETFramework,Version=v4.6.2", framework.FrameworkName);
        const string target = "net462";
#else
        Assert.Equal(".NETCoreApp,Version=v10.0", framework.FrameworkName);
        const string target = "net10.0";
#endif
        var vectors = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Golden", "channel-token-vectors.json")));
        var key = Convert.FromBase64String((string)vectors["key64"]!);
        foreach (var vector in (JArray)vectors["vectors"]!)
            Assert.Equal((string)vector["plain"]!,
                ChannelTokenProtector.Unprotect((string)vector["envelope"]!, key));

        var envelope = ChannelTokenProtector.Protect(Plain, key);
        Assert.Equal(Plain, ChannelTokenProtector.Unprotect(envelope, key));
        output.WriteLine("TokenExchange:" + JsonConvert.SerializeObject(new { target, plain = Plain, envelope }));
    }

    /// <summary>相同明文與key的兩次操作必須使用不同IV；手算HMAC輸入驗證版本／IV／密文均被認證。</summary>
    [Fact]
    public void Protect_FreshIv_AndIndependentMac()
    {
        var key = Key();
        var original = (byte[])key.Clone();
        var first = ChannelTokenProtector.Protect(Plain, key).Split(':');
        var second = ChannelTokenProtector.Protect(Plain, key).Split(':');
        Assert.Equal(4, first.Length);
        Assert.Equal("v1", first[0]);
        Assert.Equal(16, Convert.FromBase64String(first[1]).Length);
        Assert.Equal(32, Convert.FromBase64String(first[3]).Length);
        Assert.NotEqual(first[1], second[1]);
        Assert.Equal(ComputeMac(first[1], first[2], key), Convert.FromBase64String(first[3]));
        Assert.Equal(original, key);
    }

    /// <summary>逐一翻轉IV、密文或MAC的第一byte；三個部位均必須在發布明文前拒絕。</summary>
    /// <param name="part">封包中需篡改的base64部位索引。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Tampered_CiphertextOrIvOrMac_Throws(int part)
    {
        var parts = ChannelTokenProtector.Protect(Plain, Key()).Split(':');
        var bytes = Convert.FromBase64String(parts[part]);
        bytes[0] ^= 1;
        parts[part] = Convert.ToBase64String(bytes);
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect(string.Join(":", parts), Key()));
    }

    /// <summary>修改AES或MAC半部key均必須拒絕，且caller擁有的原key不被清零或改寫。</summary>
    /// <param name="position">分別位於AES與HMAC半部的故障位置。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void WrongKey_Throws(int position)
    {
        var envelope = BuildPacket(Encoding.UTF8.GetBytes(Plain));
        var wrong = Key();
        wrong[position] ^= 1;
        var before = (byte[])wrong.Clone();
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect(envelope, wrong));
        Assert.Equal(before, wrong);
    }

    /// <summary>格式、版本、元件長度與canonical base64錯誤須以相同安全例外拒絕，不回顯封包。</summary>
    /// <param name="fault">公開故障分類名稱，避免把故障輸入寫入xUnit顯示名稱。</param>
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("version")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("iv-short")]
    [InlineData("mac-short")]
    [InlineData("cipher-empty")]
    [InlineData("cipher-short")]
    [InlineData("bad-base64")]
    [InlineData("space-base64")]
    [InlineData("oversize")]
    public void InvalidEnvelope_UniformFailure(string fault)
    {
        var parts = ChannelTokenProtector.Protect(Plain, Key()).Split(':');
        string? envelope = null;
        switch (fault)
        {
            case "null": break;
            case "empty": envelope = ""; break;
            case "version": parts[0] = "v2"; break;
            case "extra": envelope = string.Join(":", parts) + ":private"; break;
            case "missing": envelope = "v1:private"; break;
            case "iv-short": parts[1] = Convert.ToBase64String(new byte[15]); break;
            case "mac-short": parts[3] = Convert.ToBase64String(new byte[31]); break;
            case "cipher-empty": parts[2] = ""; break;
            case "cipher-short": parts[2] = Convert.ToBase64String(new byte[17]); break;
            case "bad-base64": parts[2] = "private$"; break;
            case "space-base64": parts[2] += " "; break;
            case "oversize": envelope = new string('x', 4001); break;
            default: throw new InvalidOperationException("Unknown synthetic fault.");
        }
        if (fault is not "null" and not "empty") envelope ??= string.Join(":", parts);
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect(envelope!, Key()));
    }

    /// <summary>key必須精確64 bytes；兩個方向同樣拒絕，不能誤把錯長度轉成演算法差異。</summary>
    /// <param name="length">負值代表null，其餘是合成錯誤key長度。</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(65)]
    public void InvalidKey_UniformFailure(int length)
    {
        var key = length < 0 ? null : new byte[length];
        AssertSafeFailure(() => ChannelTokenProtector.Protect(Plain, key!));
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect("v1:private", key!));
    }

    /// <summary>秘密不可被修剪、無效Unicode不可替換成問號，UTF-8容量須在大buffer前受限。</summary>
    [Fact]
    public void Plaintext_BoundsAndUnicode_AreStrict()
    {
        AssertSafeFailure(() => ChannelTokenProtector.Protect(null!, Key()));
        AssertSafeFailure(() => ChannelTokenProtector.Protect("", Key()));
        AssertSafeFailure(() => ChannelTokenProtector.Protect(new string((char)0xd800, 1), Key()));
        AssertSafeFailure(() => ChannelTokenProtector.Protect(new string('x', 2928), Key()));
        AssertSafeFailure(() => ChannelTokenProtector.Protect(new string('測', 976), Key()));
        var boundary = new string('x', 2927);
        var packet = ChannelTokenProtector.Protect(boundary, Key());
        Assert.InRange(packet.Length, 1, 4000);
        Assert.Equal(boundary, ChannelTokenProtector.Unprotect(packet, Key()));
        const string preserved = " \t合成秘密\r\n ";
        Assert.Equal(preserved, ChannelTokenProtector.Unprotect(
            ChannelTokenProtector.Protect(preserved, Key()), Key()));
    }

    /// <summary>
    /// 有效MAC仍不能讓無效padding或UTF-8通過。無效padding以翻轉前一block最後byte
    /// 決定性破壞末尾padding；兩路都不能把provider錯誤或inner暴露給caller。
    /// </summary>
    [Fact]
    public void AuthenticatedInvalidPaddingOrUtf8_UniformFailure()
    {
        var invalidUtf8 = BuildPacket(new byte[] { 0xc3, 0x28 });
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect(invalidUtf8, Key()));
        var valid = BuildPacket(Encoding.UTF8.GetBytes(Plain)).Split(':');
        var ciphertext = Convert.FromBase64String(valid[2]);
        ciphertext[ciphertext.Length - 17] ^= 0xff;
        valid[2] = Convert.ToBase64String(ciphertext);
        valid[3] = Convert.ToBase64String(ComputeMac(valid[1], valid[2], Key()));
        AssertSafeFailure(() => ChannelTokenProtector.Unprotect(string.Join(":", valid), Key()));
    }

    /// <summary>完整差異位置矩陣與兩方向長度差異保護XOR不提前返回；固定時間實作另由主代理逐行審查。</summary>
    [Fact]
    public void Mac_CompareIsConstantTime()
    {
        var expected = new byte[32];
        Assert.True(ChannelTokenProtector.MacEquals(expected, (byte[])expected.Clone()));
        for (var position = 0; position < expected.Length; position++)
        {
            var changed = (byte[])expected.Clone();
            changed[position] = 1;
            Assert.False(ChannelTokenProtector.MacEquals(expected, changed));
        }
        Assert.False(ChannelTokenProtector.MacEquals(new byte[31], expected));
        Assert.False(ChannelTokenProtector.MacEquals(expected, new byte[31]));
        Assert.False(ChannelTokenProtector.MacEquals(expected, new byte[33]));
        Assert.True(ChannelTokenProtector.MacEquals(Array.Empty<byte>(), Array.Empty<byte>()));
    }

    /// <summary>兩個worker每輪會合，持不同key／明文各自加解密並拒絕對方key，不能用shared mutable key串資料。</summary>
    [Fact]
    public void ParallelDifferentKeys_NoCrossTalk()
    {
        using var barrier = new Barrier(2);
        void Run(byte seed)
        {
            var key = Enumerable.Range(0, 64).Select(x => (byte)(x + seed)).ToArray();
            var otherKey = Enumerable.Range(0, 64).Select(x => (byte)(x + (seed == 1 ? 2 : 1))).ToArray();
            for (var i = 0; i < 64; i++)
            {
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                var text = seed == 1 ? "synthetic-A" : "synthetic-B";
                var packet = ChannelTokenProtector.Protect(text, key);
                Assert.Equal(text, ChannelTokenProtector.Unprotect(packet, key));
                AssertSafeFailure(() => ChannelTokenProtector.Unprotect(packet, otherKey));
            }
        }
        Parallel.Invoke(() => Run(1), () => Run(2));
    }

    /// <summary>隔離集合中重複成功及篡改拒絕後，強制回收只用於量測，檢查無界key／crypto保留與handle增加。</summary>
    [Fact]
    public void RepeatedSuccessAndFailure_ResourcesReturnToBoundedBaseline()
    {
        void Batch()
        {
            var key = Key();
            for (var i = 0; i < 512; i++)
            {
                var packet = ChannelTokenProtector.Protect(Plain, key);
                Assert.Equal(Plain, ChannelTokenProtector.Unprotect(packet, key));
                AssertSafeFailure(() => ChannelTokenProtector.Unprotect(packet, new byte[64]));
            }
        }
        Batch();
        var baselineBytes = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselineHandles = process.HandleCount;
        Batch();
        Batch();
        Assert.InRange(GC.GetTotalMemory(forceFullCollection: true) - baselineBytes, long.MinValue, 2 * 1024 * 1024);
        process.Refresh();
        Assert.InRange(process.HandleCount - baselineHandles, int.MinValue, 16);
    }

    /// <summary>產生公開合成64-byte key；每次全新陣列，caller可核對未被產品變更。</summary>
    /// <returns>0至63的固定合成key。</returns>
    private static byte[] Key() => Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();

    /// <summary>對真實產品操作斷言唯一安全型別、固定訊息及無inner，禁止輸入片段出現在例外中。</summary>
    /// <param name="operation">預期被安全拒絕的實際產品呼叫。</param>
    private static void AssertSafeFailure(Action operation)
    {
        var failure = Assert.Throws<ChannelTokenProtectionException>(operation);
        Assert.Equal("Channel token protection failed.", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.Empty(failure.Data);
    }

    /// <summary>獨立計算公開合成fixture的MAC，不能呼叫產品私有builder計算自己的expected。</summary>
    /// <param name="iv">合成IV的canonical base64。</param>
    /// <param name="ciphertext">合成密文的canonical base64。</param>
    /// <param name="key">公開合成key。</param>
    /// <returns>ASCII版本、IV與密文的HMAC-SHA256。</returns>
    private static byte[] ComputeMac(string iv, string ciphertext, byte[] key)
    {
        using var hmac = new HMACSHA256(key.Skip(32).ToArray());
        return hmac.ComputeHash(new byte[] { (byte)'v', (byte)'1' }
            .Concat(Convert.FromBase64String(iv)).Concat(Convert.FromBase64String(ciphertext)).ToArray());
    }

    /// <summary>僅建立認證成功但明文語意可錯的攻擊fixture；使用獨立AES與MAC，不呼叫產品Protect。</summary>
    /// <param name="plaintext">公開合成byte，可包含無效UTF-8。</param>
    /// <returns>具有有效MAC的測試封包。</returns>
    private static string BuildPacket(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = Key().Take(32).ToArray();
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var transform = aes.CreateEncryptor();
        var ciphertext = Convert.ToBase64String(transform.TransformFinalBlock(plaintext, 0, plaintext.Length));
        var iv = Convert.ToBase64String(aes.IV);
        return "v1:" + iv + ":" + ciphertext + ":" + Convert.ToBase64String(ComputeMac(iv, ciphertext, Key()));
    }
}
