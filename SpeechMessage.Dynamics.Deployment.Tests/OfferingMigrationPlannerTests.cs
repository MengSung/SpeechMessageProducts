/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P8 奉獻類別轉換計畫的純資料規則；使用合成教會、選項中繼資料與正式記錄 ID，
 * 不連接 D365、不讀取 User Secrets，也不把離線成功冒充組織驗收。
 * 主要流程：將來源列依教會與舊選項數值分組，將目前畫面名稱以中繼資料標籤精確映射，
 * 再驗證同名不同值保留、沒有教會的列只計數、名稱來自 metadata 而非程式碼對照。
 * 故障與決定性斷言：注入相同／相似標籤但不同數值、未對應畫面名稱、沒有教會的來源列，
 * 並以大量教會乘目前選項觸發候選容量上限；斷言計畫不合併正式身份、不捏造資料值、
 * 來源摘要由穩定ID決定、超量時不發布部分plan。
 * 使用邊界與維護：所有候選均由單一測試建立並在測試結束釋放；沒有服務連線、暫存檔、
 * 快取或背景資源，合成輸入與計畫不得延伸到其他測試或正式資料範圍。
 */
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Offerings;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護轉換計畫的教會／選項值穩定身份及metadata文字來源契約，所有輸入皆為合成資料。</summary>
public sealed class OfferingMigrationPlannerTests
{
    private static readonly Guid ChurchA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChurchB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FeeA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid FeeB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly OfferingScreenOption[] ScreenOptions =
    {
        new("十一奉獻", 1), new("感恩奉獻", 2), new("建堂奉獻", 3), new("宣教奉獻", 4),
        new("主日奉獻", 5), new("愛心奉獻", 6)
    };
    private static readonly string[] ExpectedCatalogLabels =
        ["十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻", "主日奉獻", "愛心奉獻"];

    /// <summary>來源列依教會 ID 與選項數值分組；同值不同實體筆數累計但類別身份不依顯示名稱。</summary>
    [Fact]
    public void Plan_GroupsByChurchAndOptionValue()
    {
        var plan = Create(
            new[] { ChurchA, ChurchB },
            new[] { new OfferingOption(100000000, "十一奉獻", "ApplicationRequired") },
            new[]
            {
                Record(FeeA, "new_fee", ChurchA, 100000000),
                Record(FeeB, "new_dedication_booking", ChurchA, 100000000),
                Record(GuidFrom(3), "new_fee", ChurchB, 100000000)
            },
            Array.Empty<OfferingScreenOption>());

        Assert.Equal(2, plan.Categories.Count);
        Assert.Equal(2, plan.Categories.Single(item => item.ChurchId == ChurchA).RecordCount);
        Assert.Equal(1, plan.Categories.Single(item => item.ChurchId == ChurchB).RecordCount);
    }

    /// <summary>兩個不同選項值即使名稱完全相同或相近仍分別保留，不以名稱Distinct或覆蓋正式選項身份。</summary>
    [Fact]
    public void Plan_SimilarNames_NotMerged()
    {
        var plan = Create(
            new[] { ChurchA },
            new[]
            {
                new OfferingOption(100000000, "十一奉獻", "None"),
                new OfferingOption(100000001, "十一奉獻", "None"),
                new OfferingOption(100000002, "什一奉獻", "None")
            },
            new[]
            {
                Record(FeeA, "new_fee", ChurchA, 100000000),
                Record(FeeB, "new_fee", ChurchA, 100000001),
                Record(GuidFrom(3), "new_fee", ChurchA, 100000002)
            },
            Array.Empty<OfferingScreenOption>());

        Assert.Equal(3, plan.Categories.Count);
        Assert.Equal(3, plan.Categories.Select(item => item.LegacyOptionValue).Distinct().Count());
        Assert.Equal(2, plan.Categories.Count(item => item.Name == "十一奉獻"));
    }

    /// <summary>分類名稱逐一取自合成組織metadata標籤，無程式碼映射時仍依正式數值正確產生。</summary>
    [Fact]
    public void Plan_NamesFromOrganizationMetadata_NotCodeMaps()
    {
        var plan = Create(
            new[] { ChurchA },
            new[] { new OfferingOption(100000042, "metadata-owned-label", "None") },
            Array.Empty<OfferingSourceRecord>(),
            new[] { new OfferingScreenOption("metadata-owned-label", 1) });

        var category = Assert.Single(plan.Categories);
        Assert.Equal("metadata-owned-label", category.Name);
        Assert.Equal(100000042, category.LegacyOptionValue);
    }

    /// <summary>畫面現有名稱與已使用選項值取聯集；metadata精確匹配的目前選項即使尚未使用也納入每間教會計畫。</summary>
    [Fact]
    public void Plan_IncludesCurrentlyOfferedOptions()
    {
        var metadata = ScreenOptions.Select((screen, index) =>
            new OfferingOption(100000000 + index, screen.Label, "None")).ToArray();
        var plan = Create(new[] { ChurchA }, metadata,
            new[] { Record(FeeA, "new_fee", ChurchA, 100000000) }, ScreenOptions);

        Assert.Equal(ScreenOptions.Length, plan.Categories.Count);
        Assert.Equal(1, plan.Categories.Single(item => item.LegacyOptionValue == 100000000).RecordCount);
        Assert.All(plan.Categories.Where(item => item.LegacyOptionValue != 100000000), item => Assert.Equal(0, item.RecordCount));
    }

    /// <summary>部署對照標籤是工具協定資料而非 UI 資源；標註不可改變六個精確比對值。</summary>
    [Fact]
    public void Catalog_ScreenOptions_UsesProtocolLabelsAndExplicitI18nExclusion()
    {
        var field = typeof(OfferingMigrationCatalog).GetField(
            nameof(OfferingMigrationCatalog.ScreenOptions),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        Assert.NotNull(Attribute.GetCustomAttribute(field!, typeof(I18nIgnoreAttribute)));
        Assert.Equal(ExpectedCatalogLabels, OfferingMigrationCatalog.ScreenOptions.Select(option => option.Label));
    }

    /// <summary>沒有可判定教會的正式列只計入未解析總數，不建立教會類別或以付款人／其他列臆造歸屬。</summary>
    [Fact]
    public void Plan_RecordWithoutChurch_CountedNotFilled()
    {
        var plan = Create(new[] { ChurchA },
            new[] { new OfferingOption(100000000, "十一奉獻", "None") },
            new[] { Record(FeeA, "new_fee", null, 100000000) },
            Array.Empty<OfferingScreenOption>());

        Assert.Equal(1, plan.UnresolvedChurchRecordCount);
        Assert.Empty(plan.Categories);
    }

    /// <summary>大量教會與目前畫面選項交乘不得無界配置plan；超過固定候選預算時整份拒絕。</summary>
    [Fact]
    public void Plan_CategoryCandidateLimit_RejectsUnboundedExpansion()
    {
        var churches = Enumerable.Range(1, 25_001).Select(GuidFrom).ToArray();
        var metadata = ScreenOptions.Select((screen, index) =>
            new OfferingOption(100000000 + index, screen.Label, "None")).ToArray();

        Assert.Throws<System.IO.InvalidDataException>(() => Create(
            churches, metadata, Array.Empty<OfferingSourceRecord>(), ScreenOptions));
    }

    private static OfferingMigrationPlan Create(IReadOnlyList<Guid> churches, IReadOnlyList<OfferingOption> metadata,
        IReadOnlyList<OfferingSourceRecord> records, IReadOnlyList<OfferingScreenOption> screenOptions) =>
        OfferingMigrationPlanner.Create(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "synthetic-org",
            churches, metadata, records, screenOptions,
            DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture));

    private static OfferingSourceRecord Record(Guid id, string entity, Guid? church, int optionValue) =>
        new(id, entity, church, optionValue);

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 12);
        return new Guid(bytes);
    }
}
