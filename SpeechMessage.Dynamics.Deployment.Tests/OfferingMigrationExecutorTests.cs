/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P8 奉獻類別 apply／verify 協調器的完整性、冪等性、批次上限與中斷續跑契約。
 * 主要功能與流程：呼叫純executor建立類別及回填來源列，再逐批注入成功、中斷或回應錯誤並驗證結果。
 * 測試以單案記憶體store替身注入組織漂移、plan SHA不符、重算SHA後的候選竄改、ExecuteMultiple故障及跨教會lookup；
 * 不連接 D365、不讀秘密、不將替身成功描述為實機結果。
 * 決定性斷言：拒絕不符hash／server組織／來源摘要或重建候選；先建立唯一替代鍵類別；每批至多100筆；
 * 已填 lookup 的列重試時略過；回應數量不符或錯誤即停；verify按教會與選項值統計缺值及跨教會參照。
 * 使用邊界與維護：每測試獨立建立 store、記憶體列與 StringWriter；所有候選只於測試範圍存活，
 * 不建立 timer、背景 task、外部 stream、租約或跨案例快取。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SpeechMessage.Dynamics.Deployment.Offerings;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以可控故障的單案合成store驗證遷移寫入生命週期與逐教會核對。</summary>
public sealed class OfferingMigrationExecutorTests
{
    private static readonly Guid ChurchA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChurchB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string Organization = "synthetic-org";
    private const string PlanSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly int[] ExpectedBatchSizes = { 100, 100, 25 };
    private static readonly OfferingOption[] MetadataOptions =
        { new(100000000, "十一奉獻", "None") };
    private static readonly string MetadataFingerprint = OfferingMigrationPlanner.FingerprintMetadata(
        MetadataOptions, Array.Empty<OfferingScreenOption>(),
        "None", "Unknown");

    /// <summary>同一plan連續套用兩次，類別替代鍵及lookup不重複建立，第二次不產生業務列寫入。</summary>
    [Fact]
    public void Apply_TwiceIdempotent()
    {
        var store = StoreWith(25);
        var plan = Plan(store.Records);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log);
        var writes = store.RowWriteCount;
        OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log);
        Assert.Single(store.Categories);
        Assert.Equal(writes, store.RowWriteCount);
        Assert.Equal(25, store.RowWriteCount);
    }

    /// <summary>相同SHA-256的十六進位大小寫不影響核准；仍須完整比對摘要且先完成來源與metadata檢查。</summary>
    [Fact]
    public void Apply_PlanHashHexCaseInsensitive_AcceptsSameDigest()
    {
        var store = StoreWith(1);
        var plan = Plan(store.Records);
        using var log = new StringWriter(CultureInfo.InvariantCulture);

        OfferingMigrationExecutor.Apply(store, plan, PlanSha.ToUpperInvariant(), PlanSha, log);

        Assert.Equal(1, store.RowWriteCount);
    }

    /// <summary>第二批故障後重跑只跳過第一批已回填列，其他列完成且正式類別仍只有一個替代鍵身份。</summary>
    [Fact]
    public void Apply_ResumeAfterInterruption()
    {
        var store = StoreWith(225);
        var plan = Plan(store.Records);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        store.FailOnBatch = 2;
        Assert.Throws<IOException>(() => OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log));
        Assert.Equal(100, store.Records.Count(record => record.CategoryId.HasValue));
        store.FailOnBatch = 0;
        OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log);
        Assert.Equal(225, store.Records.Count(record => record.CategoryId.HasValue));
        Assert.Single(store.Categories);
    }

    /// <summary>plan檔案SHA錯誤或server組織身份不符時，在任何類別或記錄寫入前拒絕。</summary>
    [Theory]
    [InlineData("hash")]
    [InlineData("organization")]
    [InlineData("metadata")]
    public void Apply_PlanHashOrOrganizationChanged_Rejected(string drift)
    {
        var store = StoreWith(1);
        var plan = Plan(store.Records);
        if (drift == "organization") store.OrganizationId = Guid.NewGuid();
        if (drift == "metadata") store.MetadataFingerprint = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Throws<InvalidDataException>(() => OfferingMigrationExecutor.Apply(store, plan,
            drift == "hash" ? "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" : PlanSha,
            PlanSha, log));
        Assert.Empty(store.Categories);
        Assert.Equal(0, store.RowWriteCount);
    }

    /// <summary>即使呼叫端為被竄改的plan重新計算SHA，apply仍須以目前metadata與來源重建候選並拒絕任意類別名稱。</summary>
    [Fact]
    public void Apply_RecalculatedHashWithTamperedCategoryName_RejectedBeforeWrites()
    {
        var store = StoreWith(1);
        var original = Plan(store.Records);
        var tampered = original with
        {
            Categories = new[]
            {
                original.Categories[0] with { Name = "caller-controlled-name" }
            }
        };
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        var reviewedHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tampered)));

        Assert.Throws<InvalidDataException>(() => OfferingMigrationExecutor.Apply(
            store, tampered, reviewedHash, reviewedHash, log));

        Assert.Empty(store.Categories);
        Assert.Equal(0, store.RowWriteCount);
    }

    /// <summary>225筆來源最多分成100／100／25三批，每批呼叫一次ExecuteMultiple且沒有超過上限。</summary>
    [Fact]
    public void Apply_BatchesOf100_ExecuteMultiple()
    {
        var store = StoreWith(225);
        var plan = Plan(store.Records);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log);
        Assert.Equal(ExpectedBatchSizes, store.BatchSizes);
        Assert.All(store.BatchSizes, size => Assert.InRange(size, 1, 100));
    }

    /// <summary>逐筆SDK fault的數字碼寫入安全批次紀錄，第一個失敗批次後不送出下一批。</summary>
    [Fact]
    public void Apply_BatchFault_RecordsSafeNumericCodeAndStops()
    {
        var store = StoreWith(225);
        var plan = Plan(store.Records);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        store.FaultOnBatch = 1;

        Assert.Throws<InvalidDataException>(() => OfferingMigrationExecutor.Apply(store, plan, PlanSha, PlanSha, log));

        Assert.Single(store.BatchSizes);
        Assert.Contains("\"errorCodes\":\"0X80040224\"", log.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic", log.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("0x80040224", log.ToString(), StringComparison.Ordinal);
    }

    /// <summary>有舊選項值但無類別lookup時按教會及選項分開計數，不能只回傳全域總數或錯誤合併教會。</summary>
    [Fact]
    public void Verify_OptionWithoutLookup_CountedPerChurch()
    {
        var store = StoreWith(3);
        store.SetRecord(0, store.Records[0] with { ChurchId = ChurchA, LegacyOptionValue = 100000000 });
        store.SetRecord(1, store.Records[1] with { ChurchId = ChurchA, LegacyOptionValue = 100000000 });
        store.SetRecord(2, store.Records[2] with { ChurchId = ChurchB, LegacyOptionValue = 100000000 });
        var result = OfferingMigrationExecutor.Verify(store);
        Assert.Equal(2, result.MissingLookupByChurch[(ChurchA, 100000000)]);
        Assert.Equal(1, result.MissingLookupByChurch[(ChurchB, 100000000)]);
        Assert.Empty(result.ForeignChurchLookups);
    }

    /// <summary>lookup存在但指向另一教會的類別必須單獨回報，不可當成已完成或靜默略過。</summary>
    [Fact]
    public void Verify_LookupToOtherChurchCategory_Reported()
    {
        var store = StoreWith(1);
        store.Categories.Add(new OfferingCategoryPlan(ChurchB, "legacy-100000000", "十一奉獻", 1, 100000000, 1, "None"));
        store.SetRecord(0, store.Records[0] with
        {
            ChurchId = ChurchA, LegacyOptionValue = 100000000,
            CategoryId = Guid.NewGuid(), CategoryChurchId = ChurchB
        });
        var result = OfferingMigrationExecutor.Verify(store);
        Assert.Single(result.ForeignChurchLookups);
        Assert.Equal(ChurchB, result.ForeignChurchLookups[0].ActualChurchId);
        Assert.Equal(ChurchA, result.ForeignChurchLookups[0].ExpectedChurchId);
    }

    private static OfferingMigrationPlan Plan(ReadOnlyCollection<OfferingSourceRecord> records) =>
        OfferingMigrationPlanner.Create(OrganizationId, Organization,
            records.Where(record => record.ChurchId.HasValue).Select(record => record.ChurchId!.Value)
                .Distinct().OrderBy(id => id).ToArray(),
            MetadataOptions, records, Array.Empty<OfferingScreenOption>(),
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero), "None", "Unknown");

    private static Store StoreWith(int count)
    {
        var rows = Enumerable.Range(1, count).Select(index =>
            new OfferingSourceRecord(GuidFrom(index), "new_fee", ChurchA, 100000000)).ToList();
        return new Store(rows);
    }

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 12);
        return new Guid(bytes);
    }

    private sealed class Store(List<OfferingSourceRecord> records) : IOfferingMigrationStore
    {
        private readonly ReadOnlyCollection<OfferingSourceRecord> recordView = records.AsReadOnly();
        public Guid OrganizationId { get; set; } = OfferingMigrationExecutorTests.OrganizationId;
        public string OrganizationName => Organization;
        public string MetadataFingerprint { get; set; } = OfferingMigrationExecutorTests.MetadataFingerprint;
        public ReadOnlyCollection<OfferingSourceRecord> Records => recordView;
        public List<OfferingCategoryPlan> Categories { get; } = new();
        public List<int> BatchSizes { get; } = new();
        public int RowWriteCount { get; private set; }
        public int FailOnBatch { get; set; }
        public int FaultOnBatch { get; set; }
        public IReadOnlyList<OfferingSourceRecord> ReadSourceRecords() => recordView;
        /// <summary>合成store沿正式純planner依同一來源快照重建，以避免固定plan期待掩蓋候選竄改。</summary>
        public OfferingMigrationPlan RecreatePlan(DateTimeOffset createdUtc, IReadOnlyList<OfferingSourceRecord> sourceRecords) =>
            OfferingMigrationPlanner.Create(OrganizationId, OrganizationName,
                sourceRecords.Where(record => record.ChurchId.HasValue).Select(record => record.ChurchId!.Value)
                    .Distinct().OrderBy(id => id).ToArray(),
                MetadataOptions, sourceRecords, Array.Empty<OfferingScreenOption>(), createdUtc,
                "None", "Unknown");
        public void SetRecord(int index, OfferingSourceRecord record) => records[index] = record;
        public Guid UpsertCategory(OfferingCategoryPlan category)
        {
            var index = Categories.FindIndex(item => item.ChurchId == category.ChurchId && item.Code == category.Code);
            if (index < 0)
            {
                Categories.Add(category);
                return GuidFrom(Categories.Count + 10_000);
            }

            Categories[index] = category;
            return GuidFrom(index + 10_001);
        }
        public IReadOnlyList<OfferingUpdateResult> ExecuteMultiple(IReadOnlyList<OfferingLookupUpdate> updates)
        {
            BatchSizes.Add(updates.Count);
            if (FailOnBatch == BatchSizes.Count) throw new IOException("Synthetic batch interruption.");
            if (FaultOnBatch == BatchSizes.Count)
                return updates.Select(_ => new OfferingUpdateResult(false, "0x80040224")).ToArray();
            var results = new List<OfferingUpdateResult>(updates.Count);
            foreach (var update in updates)
            {
                var index = records.FindIndex(row => row.Id == update.RecordId && row.Entity == update.Entity);
                if (index < 0) { results.Add(new OfferingUpdateResult(false, "missing")); continue; }
                if (!records[index].CategoryId.HasValue)
                {
                    records[index] = records[index] with { CategoryId = update.CategoryId, CategoryChurchId = update.ChurchId };
                    RowWriteCount++;
                }
                results.Add(new OfferingUpdateResult(true, ""));
            }
            return results;
        }
    }
}
