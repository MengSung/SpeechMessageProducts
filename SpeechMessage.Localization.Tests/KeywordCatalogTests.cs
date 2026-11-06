/*
 * 檔案整體說明
 *
 * 目的與定位：以真實系統表保護 NFKC、空白、大小寫、跨語系衝突及明確比對模式；不執行產品路由或傳送 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 KeywordCatalogTests。
 * - 注入全形拉丁字與不同 Unicode 空白，完全比對須與資源正規化後結果一致。
 * - 六個既有英文關鍵字由真實 en resx 讀取，即使大小寫及全形不同仍回傳舊處理器意圖。
 * - 英文會友仍可輸入中文，最後繁中層永遠參與；未命中明確回 false 且不回傳前次意圖。
 * - 同語系或可同時啟用語系正規化後的同字對應異意圖，建構整表直接拒絕，不能用查詢順序掩蓋。
 * 測試案例入口包含 Keyword_FullWidthAndSpaces_Normalized、Keyword_EnglishCaseInsensitive、
 * Keyword_ChineseAlwaysWorksForEnglishMember、Keyword_ConflictInSameCulture_ThrowsAtBuild、
 * Keyword_SameIntentAliasesAndEmptyInput_Handled、Keyword_ExplicitModes_PreserveCulturePriority、
 * Keyword_ExplicitPriority_PreservesOverlappingLegacyOrder、Keyword_InvalidOrOversizedInput_ReturnsNoIntent。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>以真實系統表保護 NFKC、空白、大小寫、跨語系衝突及明確比對模式；不執行產品路由或傳送 LINE。</summary>
public sealed class KeywordCatalogTests
{
    /// <summary>注入全形拉丁字與不同 Unicode 空白，完全比對須與資源正規化後結果一致。</summary>
    [Fact(DisplayName = "關鍵字全形與連續空白正規化")]
    public void Keyword_FullWidthAndSpaces_Normalized()
    {
        var table = new SystemTextTable(["zh-TW", "en"], [["Keyword.Test", "中文命令", "hello world"]]);
        Assert.True(new KeywordCatalog(table).TryMatch("en", ["en"], " \tＨＥＬＬＯ　 \r\nＷＯＲＬＤ ", out var intent));
        Assert.Equal("Test", intent);
    }

    /// <summary>六個既有英文關鍵字由真實 en resx 讀取，即使大小寫及全形不同仍回傳舊處理器意圖。</summary>
    /// <param name="input">既有英文詞的變形。</param>
    /// <param name="expected">既有意圖識別。</param>
    [Theory(DisplayName = "英文空殼僅提供既有關鍵字並忽略大小寫")]
    [InlineData("pRaY", "PrayerRequest")]
    [InlineData("REPORT", "Journal")]
    [InlineData("ｑｒ", "QrCode")]
    [InlineData("rpg", "Rpg")]
    [InlineData("id", "QueryUserId")]
    [InlineData("notify", "NotifyLink")]
    public void Keyword_EnglishCaseInsensitive(string input, string expected)
    {
        Assert.True(new KeywordCatalog(new SystemTextTable()).TryMatch("en", ["en"], input, out var intent));
        Assert.Equal(expected, intent);
    }

    /// <summary>英文會友仍可輸入中文，最後繁中層永遠參與；未命中明確回 false 且不回傳前次意圖。</summary>
    [Fact(DisplayName = "英文會友仍可使用中文且未命中不保留結果")]
    public void Keyword_ChineseAlwaysWorksForEnglishMember()
    {
        var table = new SystemTextTable(["zh-TW", "en"], [["Keyword.Prayer", "代禱", "pray"]]);
        var catalog = new KeywordCatalog(table);
        Assert.True(catalog.TryMatch("en", ["en"], "代禱", out var intent));
        Assert.Equal("Prayer", intent);
        Assert.False(catalog.TryMatch("en", ["en"], "unrecognized", out intent));
        Assert.Equal(string.Empty, intent);
    }

    /// <summary>同語系或可同時啟用語系正規化後的同字對應異意圖，建構整表直接拒絕，不能用查詢順序掩蓋。</summary>
    /// <param name="crossCulture">是否將衝突分到另一語系。</param>
    [Theory(DisplayName = "同語系及跨語系異意圖衝突拒絕建表")]
    [InlineData(false)]
    [InlineData(true)]
    public void Keyword_ConflictInSameCulture_ThrowsAtBuild(bool crossCulture)
    {
        var table = new SystemTextTable(["zh-TW", "en"],
        [
            ["Keyword.First", "same", null],
            ["Keyword.Second", crossCulture ? "different" : "ＳＡＭＥ", crossCulture ? "ＳＡＭＥ" : null]
        ]);
        Assert.Throws<ArgumentException>(() => new KeywordCatalog(table));
    }

    /// <summary>同一意圖的大小寫別名不是資料列 ID 衝突，合法正規化後仍可查；未知與空白輸入不分派。</summary>
    [Fact(DisplayName = "同意圖別名合法且空白輸入不分派")]
    public void Keyword_SameIntentAliasesAndEmptyInput_Handled()
    {
        var table = new SystemTextTable(["zh-TW", "en"], [["Keyword.Id", "ID|Id|id", "id"]]);
        var catalog = new KeywordCatalog(table);
        Assert.True(catalog.TryMatch("en", [], "ＩＤ", out var intent));
        Assert.Equal("Id", intent);
        Assert.False(catalog.TryMatch("en", [], " \t", out _));
        Assert.False(catalog.TryMatch("en", [], null!, out _));
    }

    /// <summary>明確模式由伺服器提供並複製；先選會友語系，再選教會語系；各語系內 exact 優先於 prefix 及 contains。</summary>
    [Fact(DisplayName = "關鍵字支援明確前綴與包含模式並遵守語系順序")]
    public void Keyword_ExplicitModes_PreserveCulturePriority()
    {
        var table = new SystemTextTable(["zh-TW", "en"],
        [
            ["Keyword.Prefix", "前綴", "hello"],
            ["Keyword.Exact", "hello world", null],
            ["Keyword.Contains", "日誌", "report"]
        ]);
        var modes = new Dictionary<string, KeywordMatchMode>(StringComparer.Ordinal)
        {
            ["Keyword.Prefix"] = KeywordMatchMode.Prefix,
            ["Keyword.Contains"] = KeywordMatchMode.Contains
        };
        var catalog = new KeywordCatalog(table, matchModes: modes);
        modes["Keyword.Prefix"] = KeywordMatchMode.Exact;
        Assert.True(catalog.TryMatch("en", ["zh-TW"], "HELLO world", out var memberIntent));
        Assert.Equal("Prefix", memberIntent);
        Assert.True(catalog.TryMatch("zh-TW", ["en"], "hello world", out var churchIntent));
        Assert.Equal("Exact", churchIntent);
        Assert.True(catalog.TryMatch("en", [], "a report follows", out var containsIntent));
        Assert.Equal("Contains", containsIntent);
    }

    /// <summary>舊路由可明確要求短前綴先於長前綴；注入重疊別名，不能讓翻譯字長或字典列舉順序改寫既有優先權。</summary>
    [Fact(DisplayName = "產品指定優先序不被前綴長度覆蓋")]
    public void Keyword_ExplicitPriority_PreservesOverlappingLegacyOrder()
    {
        var table = new SystemTextTable(["zh-TW", "en"],
            [["Keyword.Long", "hello", null], ["Keyword.Short", "he", null]]);
        var modes = new Dictionary<string, KeywordMatchMode>(StringComparer.Ordinal)
        {
            ["Keyword.Long"] = KeywordMatchMode.Prefix,
            ["Keyword.Short"] = KeywordMatchMode.Prefix
        };
        var catalog = new KeywordCatalog(table, matchModes: modes,
            priorityOrder: [new TextKey("Keyword.Short"), new TextKey("Keyword.Long")]);
        Assert.True(catalog.TryMatch("zh-TW", [], "hello world", out var intent));
        Assert.Equal("Short", intent);
    }

    /// <summary>不合法 Unicode、空白及過長輸入是預期拒絕，不應留下前次意圖或傳入任何路由。</summary>
    [Fact(DisplayName = "不合法與過長關鍵字輸入拒絕分派")]
    public void Keyword_InvalidOrOversizedInput_ReturnsNoIntent()
    {
        var catalog = new KeywordCatalog(new SystemTextTable());
        Assert.False(catalog.TryMatch("en", [], "\uD800", out var malformed));
        Assert.Equal(string.Empty, malformed);
        Assert.False(catalog.TryMatch("en", [], new string('x', 16_385), out var oversized));
        Assert.Equal(string.Empty, oversized);
    }
}
