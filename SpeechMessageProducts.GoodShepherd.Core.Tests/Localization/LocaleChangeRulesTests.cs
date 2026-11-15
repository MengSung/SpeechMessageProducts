/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-04 教會語系變更的純驗證及 RichMenuGenerate 來源識別契約，
 * 由 net48 宿主驗證產品 net462，並在 net10.0 重跑相同合成案例。
 *
 * 主要功能與流程：注入未發布語言、缺少預設語言、未知時區、非法國碼及重複語系，
 * 確認固定 BusinessRuleException 不回顯輸入；成功案例比對不可變輸出、正規化後的
 * 穩定識別及不同教會隔離。相同設定測試只證明來源鍵，不冒充資料庫待辦唯一性。
 *
 * 使用邊界與維護：只借用正式共用時區規則，不讀秘密、不連資料庫或 LINE，亦不
 * 修改全域文化。各測試只持有區域純值，沒有背景工作、訂閱或待 Dispose 資源；
 * 資料庫原子建立與實機驗收由 Plugin 外殼測試另行負責。
 */
using System;
using SpeechMessageProducts.GoodShepherd.Core.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using Xunit;
namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Localization;
/// <summary>以合成設定驗證發布政策、固定錯誤與跨教會穩定來源識別，不建立外部資源。</summary>
public sealed class LocaleChangeRulesTests
{
    /// <summary>注入英文與偽語系，固定錯誤及空參數證明未發布語言無法繞過政策且不洩漏輸入。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("qps-ploc")]
    [InlineData("not-a-language")]
    public void ChurchLocale_UnopenedLanguage_Rejected(string language)
    {
        AssertRejected(() => LocaleChangeRules.Validate(language, language, "Asia/Taipei", "TW"));
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "zh-TW," + language, "Asia/Taipei", "TW"));
    }
    /// <summary>目前只發布繁中；空啟用集合缺少預設值時必須拒絕，不能自動補入或發布部分結果。</summary>
    [Fact]
    public void ChurchLocale_DefaultNotInEnabled_Rejected() =>
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "", "Asia/Taipei", "TW"));
    /// <summary>注入未知、Windows、錯誤大小寫及帶空白時區，確認沿用共用 IANA 邊界而無伺服器回落。</summary>
    [Theory]
    [InlineData("Unknown/PrivateZone")]
    [InlineData("Taipei Standard Time")]
    [InlineData("asia/taipei")]
    [InlineData(" Asia/Taipei ")]
    [InlineData("")]
    public void ChurchLocale_UnknownTimeZone_Rejected(string zone) =>
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "zh-TW", zone, "TW"));
    /// <summary>同一教會重算須符獨立 Python 編碼的固定向量；真正待辦唯一性由外殼原子儲存測試負責。</summary>
    [Fact]
    public void ChurchLocale_SameSettingsTwice_OneTask()
    {
        var church = new Guid("11111111-2222-3333-4444-555555555555");
        var first = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", "TW");
        var second = LocaleChangeRules.Validate("ZH-tw", " zh-tw ", "Asia/Taipei", "tw");
        Assert.Equal(LocaleChangeRules.SourceId(church, first), LocaleChangeRules.SourceId(church, second));
        Assert.Equal(new Guid("078e0c6a-0aaf-0f5c-8f0b-e61ef0ecb6f0"), LocaleChangeRules.SourceId(church, first));
    }
    /// <summary>交錯呼叫不同教會及設定，確認來源鍵隔離且後續呼叫不污染先前不可變快照。</summary>
    [Fact]
    public void ChurchLocale_OtherChurchUnaffected()
    {
        var churchA = new Guid("11111111-2222-3333-4444-555555555555");
        var churchB = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var settings = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", "TW");
        var original = LocaleChangeRules.SourceId(churchA, settings);
        Assert.NotEqual(original, LocaleChangeRules.SourceId(churchB, settings));
        var changed = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Tokyo", "JP");
        Assert.NotEqual(original, LocaleChangeRules.SourceId(churchA, changed));
        var countryOnly = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", "JP");
        var zoneOnly = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Tokyo", "TW");
        Assert.NotEqual(original, LocaleChangeRules.SourceId(churchA, countryOnly));
        Assert.NotEqual(original, LocaleChangeRules.SourceId(churchA, zoneOnly));
        Assert.Equal(original, LocaleChangeRules.SourceId(churchA, settings));
        Assert.Equal("TW", settings.CountryCode);
    }
    /// <summary>唯一已發布語言仍須輸出固定大小寫與逗號格式，禁止以未發布第二語言測試假成功排序。</summary>
    [Fact]
    public void ChurchLocale_NormalizedOrderingAndCase_AreStable()
    {
        var settings = LocaleChangeRules.Validate(" ZH-tw ", " zh-TW ", "Asia/Taipei", "tw");
        Assert.Equal("zh-TW", settings.DefaultLanguage);
        Assert.Equal("zh-TW", settings.EnabledLanguages);
        Assert.Equal("Asia/Taipei", settings.TimeZone);
        Assert.Equal("TW", settings.CountryCode);
    }
    /// <summary>注入重複與空語言項，依共用語系集合契約明確拒絕，避免靜默掩蓋來源設定衝突。</summary>
    [Theory]
    [InlineData("zh-TW,ZH-tw")]
    [InlineData(",zh-TW")]
    [InlineData("zh-TW,")]
    [InlineData("zh-TW,,zh-TW")]
    public void ChurchLocale_DuplicateOrEmptyLanguage_Rejected(string languages) =>
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", languages, "Asia/Taipei", "TW"));
    /// <summary>國碼只能為兩個 ASCII 字母；長度、空白、數字及 Unicode 相似字均須固定錯誤拒絕。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("TWN")]
    [InlineData("T1")]
    [InlineData("ＴＷ")]
    [InlineData(" TW ")]
    [InlineData("Té")]
    public void ChurchLocale_InvalidCountry_Rejected(string country) =>
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", country));
    /// <summary>缺值及過長設定在分割或解析前拒絕；例外只含固定鍵，不保留呼叫端原始資料。</summary>
    [Fact]
    public void ChurchLocale_NullAndOversizedInput_Rejected()
    {
        AssertRejected(() => LocaleChangeRules.Validate(null!, "zh-TW", "Asia/Taipei", "TW"));
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", null!, "Asia/Taipei", "TW"));
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "zh-TW", null!, "TW"));
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", null!));
        AssertRejected(() => LocaleChangeRules.Validate("zh-TW", new string('x', 9000), "Asia/Taipei", "TW"));
    }
    /// <summary>未具正式教會識別或缺少已驗證設定時不得建立來源鍵，避免不同操作落入空範圍。</summary>
    [Fact]
    public void ChurchLocale_SourceRequiresChurchAndValidatedSettings()
    {
        var settings = LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", "TW");
        AssertRejected(() => LocaleChangeRules.SourceId(Guid.Empty, settings));
        AssertRejected(() => LocaleChangeRules.SourceId(Guid.NewGuid(), null!));
    }
    /// <summary>集中決定性錯誤斷言：固定既有資源鍵、無原始參數、無內層輸入例外。</summary>
    /// <param name="operation">只在本次測試執行的同步故障注入。</param>
    private static void AssertRejected(Action operation)
    {
        var error = Assert.Throws<BusinessRuleException>(operation);
        Assert.Equal("Common.Plugin.InvalidConfiguration", error.TextKey.Value);
        Assert.Equal("Common.Plugin.InvalidConfiguration", error.Message);
        Assert.Empty(error.Arguments);
        Assert.Null(error.InnerException);
    }
}
