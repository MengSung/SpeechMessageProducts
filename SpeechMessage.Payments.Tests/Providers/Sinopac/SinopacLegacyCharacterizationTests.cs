/*
 * 檔案整體說明
 *
 * 目的與定位：本檔位於 SpeechMessage.Payments.Tests 的永豐 provider 測試層，以固定合成資料鎖定已搬入主方案的舊 QPay 金鑰、
 * 簽章、封包加解密、建單序列化與付款查詢回應解析行為；不呼叫外部銀行、CRM、LINE 或任何正式／測試商店。
 *
 * 主要功能與流程：各測試以明確標示的合成憑證片段、nonce、payload 及 Golden JSON 呼叫 SpeechMessage.Payments 內部純函式，
 * 將結果與獨立 Node.js crypto 腳本建立的固定向量或手工定義的合成 JSON 黃金逐字比較。所有檔案由測試輸出目錄的 Golden/Sinopac
 * 複本讀取；測試不寫入共用狀態、不建立網路資源，輸入與結果生命週期僅限單一測試呼叫。
 *
 * 適用範圍與不變條件：這些測試只描述既有協定相容性，不代表永豐實機或付款成功；樣本商店、訂單、會友及憑證字串均為合成值。
 * 若協定、序列化欄位或密碼學結果改變，必須先由核定規格確認，再同步更新明確預期值，禁止用被測程式輸出自動重生 Golden。
 *
 * 使用邊界與維護：每個測試只持有區域合成 DTO 與字串，不讀 User Secrets、不連網、不保存跨測試或跨使用者狀態。
 * ReadGolden 以 File.ReadAllText 完成唯讀載入並關閉檔案 handle；測試結束後區域參考即不再保留，Golden 不會被測試覆寫。
 * 任何向量或樣本變更都須維持合成性並重新執行指定 filter；本機獨立腳本與原始輸出不提交。
 *
 * 失敗處理與驗證：固定測試名稱由 P4-01 的 dotnet test filter 執行；雜湊、AES key、ciphertext、JSON 差異或缺少 Golden
 * 都使測試失敗。向量計算原始腳本只保存在此子任務的本機 evidence，不納入版控；本檔無持續性、快取、背景工作或需 Dispose 的自有資源。
 */
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Newtonsoft.Json;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.Sinopac;

/// <summary>
/// 以合成固定向量及 JSON 黃金驗證永豐 QPay 舊協定的相容行為。
/// 所有密碼學斷言均使用獨立計算的預期值，Golden 檔僅含合成資料且不觸發外部付款。
/// </summary>
public sealed class SinopacLegacyCharacterizationTests
{
    private const string SyntheticAesKey = "00112233445566778899AABBCCDDEEFF";
    private const string SyntheticNonce = "NONCE-SYNTHETIC-001";

    /// <summary>
    /// 驗證四段明確標示為合成的假金鑰依舊規則兩兩 XOR，並以大寫十六進位字串串接。
    /// 決定性斷言保護舊 QPay 將 ASCII hex 字元作為 AES key bytes 的相容契約。
    /// </summary>
    [Fact]
    public void HashCode_FixedHalves_ProducesLegacyAesKey()
    {
        var aesKey = SinopacCrypto.BuildAesKey(CreateSyntheticProfile());

        aesKey.Should().Be(SyntheticAesKey);
    }

    /// <summary>
    /// 驗證公開純量欄位依名稱排序、忽略空白／非純量欄位並排除簽章欄位後，
    /// 與固定 nonce 及合成 AES key 串接所產生的 SHA-256 大寫向量。
    /// </summary>
    [Fact]
    public void Sign_FixedPayload_MatchesLegacyVector()
    {
        var payload = new SyntheticSigningPayload
        {
            Zeta = "z",
            Alpha = 7,
            Blank = " ",
            Sign = "SYNTHETIC-EXCLUDED-SIGN",
            Details = new Dictionary<string, string> { ["ignored"] = "non-scalar" }
        };

        SinopacSigner.GetSigningString(payload).Should().Be("Alpha=7&Zeta=z");
        SinopacSigner.GenerateSign(payload, SyntheticAesKey, SyntheticNonce)
            .Should()
            .Be("5B7929131D3D5795F42E17F952957AB34552850BCF1439172CEC78714BE5DD61");
    }

    /// <summary>
    /// 驗證 nonce 衍生 IV、大寫 ASCII AES key、CBC／PKCS7 與大寫密文十六進位格式。
    /// 固定 ciphertext 由獨立 Node.js crypto 實作計算；再解密回原始 UTF-8 JSON 證明往返資料一致。
    /// </summary>
    [Fact]
    public void Envelope_FixedNonce_EncryptsToLegacyVector()
    {
        const string plaintext = "{\"sample\":\"synthetic\",\"amount\":12345}";

        var ciphertext = SinopacCrypto.Encrypt(SyntheticAesKey, plaintext, SyntheticNonce);

        ciphertext.Should().Be(
            "C4680BBE6404BA76B4CF03947A61F2632B6F308833135BB44DAA52309D8C1111F8D4D38DA8ECA833F3C738DAEDC4F160");
        SinopacCrypto.Decrypt(SyntheticAesKey, ciphertext, SyntheticNonce).Should().Be(plaintext);
    }

    /// <summary>
    /// 驗證中性合成付款輸入映射為永豐建單 DTO 後，依 provider serializer 選項產生的 JSON
    /// 與手工維護的固定 Golden 完全相同，不以目前輸出覆寫期待檔。
    /// </summary>
    [Fact]
    public void OrderCreateRequest_Serializes_ToGolden()
    {
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "SYN-ORDER-001",
            Amount = 123.45m,
            Currency = "TWD",
            Description = "Synthetic fee",
            PaymentMethod = "CreditCard",
            PaymentMethodSubType = "ONE",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.invalid/payment/return",
                BackendUrl = "https://example.invalid/payment/backend"
            },
            Metadata = new Dictionary<string, string>
            {
                ["Memo"] = "Synthetic memo",
                ["Param1"] = "SYN-FEE-001",
                ["Param2"] = "SYN-ORG-001",
                ["Param3"] = "fee"
            }
        };
        var payload = SinopacRequestMapper.MapCreateRequest(CreateSyntheticProfile(), request);
        var actual = JsonConvert.SerializeObject(
            payload,
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            });

        actual.Should().Be(ReadGolden("order-create-request.json"));
    }

    /// <summary>
    /// 驗證付款查詢的合成 provider JSON 可反序列化為內部回應 DTO，
    /// 並保留外層狀態、交易識別、訂單與最小單位金額欄位。
    /// </summary>
    [Fact]
    public void OrderPayQueryResponse_Deserializes_FromGolden()
    {
        var response = JsonConvert.DeserializeObject<SinopacOrderPayResponse>(
            ReadGolden("order-pay-query-response.json"));

        response.Should().NotBeNull();
        response!.ShopNo.Should().Be("SYNTHETIC-SHOP-001");
        response.PayToken.Should().Be("SYN-PAY-TOKEN-001");
        response.Status.Should().Be("S");
        response.TSResultContent.Should().NotBeNull();
        response.TSResultContent!.TSNo.Should().Be("SYN-TRANSACTION-001");
        response.TSResultContent.OrderNo.Should().Be("SYN-ORDER-001");
        response.TSResultContent.Amount.Should().Be("12345");
    }

    /// <summary>
    /// 建立只供本測試使用的合成 merchant profile；不得從 User Secrets 或正式設定載入任何值。
    /// </summary>
    private static PaymentMerchantProfile CreateSyntheticProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "SyntheticTestOnly",
            Provider = PaymentProviderKind.Sinopac,
            Credentials = new Dictionary<string, string>
            {
                ["ShopNo"] = "SYNTHETIC-SHOP-001",
                ["A1"] = "0011223344556677",
                ["A2"] = "0000000000000000",
                ["B1"] = "8899AABBCCDDEEFF",
                ["B2"] = "0000000000000000",
                ["XKeyId"] = "00000000-0000-0000-0000-000000000000"
            }
        };
    }

    /// <summary>
    /// 從測試輸出目錄取得核定的合成 Golden，缺檔時讓測試明確失敗。
    /// </summary>
    private static string ReadGolden(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "Sinopac", fileName);
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// 提供可反射的純量、空白、巢狀非純量及明確排除欄位，驗證簽章欄位選取契約。
    /// </summary>
    private sealed class SyntheticSigningPayload
    {
        /// <summary>簽章排序向量中的第一個整數純量。</summary>
        public int Alpha { get; init; }

        /// <summary>應因空白內容而略過的字串欄位。</summary>
        public string Blank { get; init; } = string.Empty;

        /// <summary>應因其不是純量而略過的巢狀資料。</summary>
        public IReadOnlyDictionary<string, string> Details { get; init; } =
            new Dictionary<string, string>();

        /// <summary>由永豐簽章規則排除、不得加入簽章字串的合成欄位。</summary>
        [SinopacSignExclude]
        public string Sign { get; init; } = string.Empty;

        /// <summary>簽章排序向量中的字串純量。</summary>
        public string Zeta { get; init; } = string.Empty;
    }
}
