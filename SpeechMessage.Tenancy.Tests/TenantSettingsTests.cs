/*
 * 檔案整體說明
 *
 * 目的與定位：保護完整組織／產品／正式教會設定的不可變發布；全部設定為合成值，不連資料庫或保存使用者資料。
 *
 * 主要功能與流程：
 * 本檔宣告 TenantSettingsTests。
 * - 固定預期值只供 Assert 比較，不保存租戶資料且不交給產品修改。
 * - 未提供任何教會設定時採正式繁中／台北／台灣／TWD 地基，品牌留空交產品選現有後備。
 * - 同教會 ID 的不同組織、同組織不同產品及不同教會逐一改變，不能命中另一範圍品牌或語系。
 * - 設定根後續變更只影響下次讀取，已回傳的 locale 清單及品牌不能被修改；來源不以 Section 對外傳遞。
 * 測試案例入口包含 GetSettings_Defaults_PreserveCompleteScope、GetSettings_DifferentScopes_DoNotCrossTalk、
 * GetSettings_SourceChanges_PreservePreviousSnapshot、GetSettings_MissingChurchOrForeignProduct_Rejects、
 * GetSettings_InvalidCultures_RejectsWholeCandidate、GetSettings_EmptyTimeZone_RejectsAndCanRetry、
 * GetSettings_InvalidField_RejectsCandidate、GetSettings_ConcurrentScopes_DoNotCrossTalkOrRetainResults；
 * 其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>保護完整組織／產品／正式教會設定的不可變發布；全部設定為合成值，不連資料庫或保存使用者資料。</summary>
public sealed class TenantSettingsTests
{
    /// <summary>固定預期值只供 Assert 比較，不保存租戶資料且不交給產品修改。</summary>
    private static readonly string[] NeutralCultures = ["zh-TW"];
    /// <summary>固定雙語預期值，不對外發布或作為可變來源。</summary>
    private static readonly string[] TwoCultures = ["zh-TW", "en"];
    /// <summary>未提供任何教會設定時採正式繁中／台北／台灣／TWD 地基，品牌留空交產品選現有後備。</summary>
    [Fact(DisplayName = "缺省租戶設定採正式地基且保留完整範圍")]
    public void GetSettings_Defaults_PreserveCompleteScope()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Build();
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        var result = new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(tenant);
        Assert.Same(tenant, result.Tenant);
        Assert.Equal("zh-TW", result.Locale.DefaultCulture);
        Assert.Equal("zh-TW", result.Locale.FormatCulture);
        Assert.Equal(NeutralCultures, result.Locale.EnabledCultures);
        Assert.Equal("Asia/Taipei", result.TimeZoneId);
        Assert.Equal("TW", result.Country);
        Assert.Equal("TWD", result.Currency);
        Assert.Equal(string.Empty, result.BrandName);
    }

    /// <summary>同教會 ID 的不同組織、同組織不同產品及不同教會逐一改變，不能命中另一範圍品牌或語系。</summary>
    [Fact(DisplayName = "組織產品教會設定完整分區")]
    public void GetSettings_DifferentScopes_DoNotCrossTalk()
    {
        var church = Guid.NewGuid();
        var first = new TenantId("Alpha", "ProductA", church);
        var second = new TenantId("Beta", "ProductA", church);
        var third = new TenantId("Alpha", "ProductB", church);
        var fourth = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        var data = new Dictionary<string, string?>();
        foreach (var pair in new[] { (first, "brand-A"), (second, "brand-B"), (third, "brand-C"), (fourth, "brand-D") })
            data[Prefix(pair.Item1) + "BrandName"] = pair.Item2;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        Assert.Equal("brand-A", new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(first).BrandName);
        Assert.Equal("brand-B", new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(second).BrandName);
        Assert.Equal("brand-C", new ConfigurationTenantSettingsProvider(config, "ProductB").GetSettings(third).BrandName);
        Assert.Equal("brand-D", new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(fourth).BrandName);
    }

    /// <summary>設定根後續變更只影響下次讀取，已回傳的 locale 清單及品牌不能被修改；來源不以 Section 對外傳遞。</summary>
    [Fact(DisplayName = "租戶設定完整複製並隔離後續設定修改")]
    public void GetSettings_SourceChanges_PreservePreviousSnapshot()
    {
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        var prefix = Prefix(tenant);
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [prefix + "TextCulture"] = "en",
            [prefix + "FormatCulture"] = "de-DE",
            [prefix + "EnabledCultures"] = "zh-TW,en",
            [prefix + "TimeZoneId"] = "Europe/London",
            [prefix + "Country"] = "gb",
            [prefix + "Currency"] = "gbp",
            [prefix + "BrandName"] = "brand-before"
        }).Build();
        var provider = new ConfigurationTenantSettingsProvider(config, "ProductA");
        var first = provider.GetSettings(tenant);
        config[prefix + "EnabledCultures"] = "zh-TW";
        config[prefix + "BrandName"] = "brand-after";
        var second = provider.GetSettings(tenant);
        Assert.Equal("brand-before", first.BrandName);
        Assert.Equal("brand-after", second.BrandName);
        Assert.Equal(TwoCultures, first.Locale.EnabledCultures);
        Assert.Equal(NeutralCultures, second.Locale.EnabledCultures);
        Assert.Equal("de-DE", first.Locale.FormatCulture);
        Assert.Equal("GB", first.Country);
        Assert.Equal("GBP", first.Currency);
    }

    /// <summary>讀教會設定必須有正式教會 ID，另一產品不能借用本 provider；錯誤不能靠組織預設覆蓋。</summary>
    [Fact(DisplayName = "教會設定缺正式ID或跨產品直接拒絕")]
    public void GetSettings_MissingChurchOrForeignProduct_Rejects()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Build();
        var provider = new ConfigurationTenantSettingsProvider(config, "ProductA");
        Assert.Throws<ArgumentException>(() => provider.GetSettings(new TenantId("Alpha", "ProductA")));
        Assert.Throws<ArgumentException>(() => provider.GetSettings(new TenantId("Alpha", "ProductB", Guid.NewGuid())));
    }

    /// <summary>注入不合法文化及超量／重複語系，整個候選拒絕，不能回半個有效設定。</summary>
    [Fact(DisplayName = "錯誤語系設定拒絕完整發布")]
    public void GetSettings_InvalidCultures_RejectsWholeCandidate()
    {
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
        config[Prefix(tenant) + "EnabledCultures"] = "zh-TW,ZH-tw";
        Assert.Throws<ArgumentException>(() => new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(tenant));
        config[Prefix(tenant) + "EnabledCultures"] = "zh-TW";
        config[Prefix(tenant) + "FormatCulture"] = "!invalid!";
        Assert.ThrowsAny<ArgumentException>(() => new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(tenant));
    }

    /// <summary>明確空白時區不能冒充缺省值或發布成功；故障後改回完整 IANA ID 可重新取得完整候選。</summary>
    /// <param name="invalid">設定來源的空值或空白。</param>
    [Theory(DisplayName = "空時區拒絕發布且修正後可重試")]
    [InlineData("")]
    [InlineData(" ")]
    public void GetSettings_EmptyTimeZone_RejectsAndCanRetry(string invalid)
    {
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
        var provider = new ConfigurationTenantSettingsProvider(config, "ProductA");
        config[Prefix(tenant) + "TimeZoneId"] = invalid;
        Assert.Throws<ArgumentException>(() => provider.GetSettings(tenant));
        config[Prefix(tenant) + "TimeZoneId"] = "Europe/London";
        Assert.Equal("Europe/London", provider.GetSettings(tenant).TimeZoneId);
    }

    /// <summary>來源中的品牌、國家、幣別、文化清單或長度失效時整份候選拒絕；不回部分合法欄位。</summary>
    /// <param name="field">固定設定欄位。</param>
    /// <param name="invalid">合成無效值。</param>
    [Theory(DisplayName = "租戶設定逐欄驗證後才完整發布")]
    [InlineData("BrandName", "brand\ninjection")]
    [InlineData("Country", "T1")]
    [InlineData("Country", "T")]
    [InlineData("Currency", "TW1")]
    [InlineData("Currency", "TWD1")]
    public void GetSettings_InvalidField_RejectsCandidate(string field, string invalid)
    {
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
        config[Prefix(tenant) + field] = invalid;
        Assert.Throws<ArgumentException>(() => new ConfigurationTenantSettingsProvider(config, "ProductA").GetSettings(tenant));
    }

    /// <summary>同 provider 供兩個教會各千次並行，不得串品牌／locale；排空 Task 後 provider 也不保留任何已回傳快照。</summary>
    [Fact(DisplayName = "教會設定千次並行隔離且完成後回收")]
    public async Task GetSettings_ConcurrentScopes_DoNotCrossTalkOrRetainResults()
    {
        var first = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        var second = new TenantId("Beta", "ProductA", Guid.NewGuid());
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Prefix(first) + "BrandName"] = "brand-A",
            [Prefix(second) + "BrandName"] = "brand-B"
        }).Build();
        var provider = new ConfigurationTenantSettingsProvider(config, "ProductA");
        var tasks = Enumerable.Range(0, 1000).Select(_ => Task.Run(() =>
        {
            Assert.Equal("brand-A", provider.GetSettings(first).BrandName);
            Assert.Equal("brand-B", provider.GetSettings(second).BrandName);
        })).ToArray();
        await Task.WhenAll(tasks);
        Assert.All(tasks, task => Assert.True(task.IsCompletedSuccessfully));
        var references = Enumerable.Range(0, 100).SelectMany(_ => Exercise(provider)).ToArray();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(provider);
    }

    /// <summary>將操作堆疊與 GC 斷言分離；只回弱引用，不讓測試自己保留完成的完整 scope 與 locale。</summary>
    /// <param name="provider">必須持續存活的真實 provider。</param>
    /// <returns>本次純弱引用。</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] Exercise(ConfigurationTenantSettingsProvider provider)
    {
        var tenant = new TenantId("Alpha", "ProductA", Guid.NewGuid());
        var result = provider.GetSettings(tenant);
        return [new(tenant), new(result), new(result.Locale)];
    }

    /// <summary>設定鍵固定包含完整 server scope；測試只建合成鍵，不由顯示品牌推導識別。</summary>
    /// <param name="tenant">本案例已授權範圍。</param>
    /// <returns>只供測試合成設定使用的完整鍵。</returns>
    private static string Prefix(TenantId tenant) => "Tenancy:" + tenant.Organization + ":" + tenant.Product + ":" + tenant.ChurchId!.Value.ToString("D") + ":";
}
