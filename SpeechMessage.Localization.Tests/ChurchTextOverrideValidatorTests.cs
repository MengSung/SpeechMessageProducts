/*
 * 檔案整體說明
 *
 * 目的與定位：在任何快取發布之前驗全部覆寫；故障只含合成資料，精確保護正式 schema、參數、長度及不可變候選。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchTextOverrideValidatorTests。
 * - 固定合成系統表，只由 SystemTextTable 複製且不修改，不含教會狀態。
 * - 固定合成 ChatBar 系統鍵，不代表正式資源或使用者資料。
 * - 同本文不同正式 ID／key 均保留；外部清單後改不影響已發布快照，具名參數換序仍合法。
 * - 未知鍵、缺／多參數、schema 邊界、重複 key/culture 或 ID、外教會及非法 Unicode 均拒絕整候選。
 * 測試案例入口包含 Validate_CompleteCandidate_PreservesIdentitiesAndDetaches、
 * Validate_InvalidCandidate_RejectsBeforePublication、Validate_Sender_UsesCompleteNameAndUtf16Limit、
 * Validate_ChatBar_EnforcesFourteenCharacters、Validate_TextBoundaries_AllowExplicitEmptyAndMaximum、
 * Validate_CultureIdentity_IsCaseInsensitiveButKeepsTranslations、Validator、Context；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>在任何快取發布之前驗全部覆寫；故障只含合成資料，精確保護正式 schema、參數、長度及不可變候選。</summary>
public sealed class ChurchTextOverrideValidatorTests
{
    /// <summary>固定合成系統表，只由 SystemTextTable 複製且不修改，不含教會狀態。</summary>
    private static readonly string[] TestCultures = { "zh-TW" };
    /// <summary>固定合成 ChatBar 系統鍵，不代表正式資源或使用者資料。</summary>
    private static readonly string?[][] TestRows = { new string?[] { "RichMenu.ChatBar.Test", "base" } };
    /// <summary>同本文不同正式 ID／key 均保留；外部清單後改不影響已發布快照，具名參數換序仍合法。</summary>
    [Fact]
    public void Validate_CompleteCandidate_PreservesIdentitiesAndDetaches()
    {
        var context = Context();
        var rows = new List<ChurchTextOverride>
        {
            Row(context, "Common.Diagnostics.Ping", "same"),
            Row(context, "Keyword.Journal", "same"),
            Row(context, "Common.Diagnostics.Culture", "language={culture}")
        };
        var snapshot = Validator().Validate(context, rows);
        rows.Clear();
        Assert.True(snapshot.TryGetText("*", new TextKey("Common.Diagnostics.Ping"), out var ping));
        Assert.Equal("same", ping);
        Assert.True(snapshot.TryGetText("*", new TextKey("Keyword.Journal"), out var journal));
        Assert.Equal("same", journal);
        Assert.True(snapshot.TryGetText("*", new TextKey("Common.Diagnostics.Culture"), out var culture));
        Assert.Equal("language={culture}", culture);
    }

    /// <summary>未知鍵、缺／多參數、schema 邊界、重複 key/culture 或 ID、外教會及非法 Unicode 均拒絕整候選。</summary>
    /// <param name="fault">合成固定故障種類。</param>
    [Theory]
    [InlineData("unknown")]
    [InlineData("missing-parameter")]
    [InlineData("extra-parameter")]
    [InlineData("text-budget")]
    [InlineData("culture-budget")]
    [InlineData("invalid-culture")]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-id")]
    [InlineData("foreign-church")]
    [InlineData("unicode")]
    public void Validate_InvalidCandidate_RejectsBeforePublication(string fault)
    {
        var context = Context();
        var rows = new List<ChurchTextOverride> { Row(context, "Common.Diagnostics.Ping", "ok") };
        switch (fault)
        {
            case "unknown": rows.Add(Row(context, "Unknown.Diagnostics.Ping", "unknown")); break;
            case "missing-parameter": rows.Add(Row(context, "Common.Diagnostics.Culture", "missing")); break;
            case "extra-parameter": rows.Add(Row(context, "Keyword.Journal", "{extra}")); break;
            case "text-budget": rows.Add(Row(context, "Keyword.Journal", new string('x', 2001))); break;
            case "culture-budget": rows.Add(Row(context, "Keyword.Journal", "ok", new string('a', 17))); break;
            case "invalid-culture": rows.Add(Row(context, "Keyword.Journal", "ok", "en_!")); break;
            case "duplicate-key": rows.Add(Row(context, "Common.Diagnostics.Ping", "other")); break;
            case "duplicate-id": rows.Add(new ChurchTextOverride(rows[0].Id, context.ChurchId, new TextKey("Keyword.Journal"), "*", "other")); break;
            case "foreign-church": rows.Add(Row(Context(), "Keyword.Journal", "other")); break;
            case "unicode": rows.Add(Row(context, "Keyword.Journal", "\uD800")); break;
        }
        Assert.ThrowsAny<Exception>(() => Validator().Validate(context, rows));
    }

    /// <summary>Sender 展開完整十四字最長名稱，20 合法而 21 拒絕；重複參數每次都計長，不將插入值當新模板。</summary>
    /// <param name="text">覆寫模板。</param>
    /// <param name="accepted">是否符合 UTF-16 二十字上限。</param>
    [Theory]
    [InlineData("{churchName}123456", true)]
    [InlineData("{churchName}1234567", false)]
    [InlineData("{churchName}{churchName}", false)]
    [InlineData("{churchName}😀😀😀", true)]
    [InlineData("{churchName}😀😀😀a", false)]
    public void Validate_Sender_UsesCompleteNameAndUtf16Limit(string text, bool accepted)
    {
        var context = Context();
        var rows = new[] { Row(context, "Line.Sender.Template", text) };
        if (accepted) Assert.NotNull(Validator().Validate(context, rows));
        else Assert.ThrowsAny<Exception>(() => Validator().Validate(context, rows));
    }

    /// <summary>ChatBar 來源仍為系統鍵；以內建未來產品表測十四／十五邊界，不虛構正式資源完成度。</summary>
    /// <param name="length">UTF-16 文字長度。</param>
    /// <param name="accepted">十四字以內有效。</param>
    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    public void Validate_ChatBar_EnforcesFourteenCharacters(int length, bool accepted)
    {
        var system = new SystemTextTable(TestCultures, TestRows);
        var validator = new ChurchTextOverrideValidator(system, "這是一個長度剛好十二字的教會");
        var context = Context();
        var rows = new[] { Row(context, "RichMenu.ChatBar.Test", new string('x', length)) };
        if (accepted) Assert.NotNull(validator.Validate(context, rows));
        else Assert.ThrowsAny<Exception>(() => validator.Validate(context, rows));
    }

    /// <summary>正好 schema 二千字與空白本文可以取代資源，但參數集合不能因此漏掉。</summary>
    [Fact]
    public void Validate_TextBoundaries_AllowExplicitEmptyAndMaximum()
    {
        var context = Context();
        Assert.NotNull(Validator().Validate(context, new[] { Row(context, "Keyword.Journal", new string('x', 2000)) }));
        Assert.NotNull(Validator().Validate(context, new[] { Row(context, "Keyword.Journal", string.Empty) }));
    }

    /// <summary>語系大小寫不能繞過同 key/culture 衝突；不同文化同一 key 仍合法。</summary>
    [Fact]
    public void Validate_CultureIdentity_IsCaseInsensitiveButKeepsTranslations()
    {
        var context = Context();
        Assert.ThrowsAny<Exception>(() => Validator().Validate(context, new[]
        { Row(context, "Keyword.Journal", "one", "en"), Row(context, "Keyword.Journal", "two", "EN") }));
        Assert.NotNull(Validator().Validate(context, new[]
        { Row(context, "Keyword.Journal", "one", "en"), Row(context, "Keyword.Journal", "two", "zh-TW") }));
    }

    /// <summary>使用正式系統資源及完整測試名稱，不縮短 E.4 的字面以讓驗證通過。</summary>
    private static ChurchTextOverrideValidator Validator() => new(new SystemTextTable(), "這是一個長度剛好十二字的教會");
    /// <summary>每案獨立範圍，不保存使用者或租戶狀態。</summary>
    private static TextContext Context() => new(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");
    /// <summary>建立具正式資料庫 ID 的單列純值；文字相同不作去重。</summary>
    private static ChurchTextOverride Row(TextContext context, string key, string text, string culture = "*") =>
        new(Guid.NewGuid(), context.ChurchId, new TextKey(key), culture, text);
}
