/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成資料驗證 GoodShepherd.Core 教會設定服務的白名單、覆寫存檔順序、
 * 類別教會範圍與有限稽核查詢；不連 D365、LINE 或任何正式／測試組織。
 * 主要流程：建立不可變 AuthorizedChurch 與完整 TextContext，透過測試自有的設定儲存契約
 * 注入 Upsert、時間戳、類別及稽核結果，再斷言服務只在完整成功後失效快取或發布結果。
 * 使用邊界與維護：所有替身、時鐘與候選集合只屬單一測試；測試不讀秘密、不使用共享快取、
 * 背景工作、timer、連線或外部資源。新增失敗案例時須保留原始故障身分及 owner 清理斷言。
 * 故障與決定性斷言：明確注入過長／不允許的鍵、模板參數不符、Upsert 故障、同教會代碼衝突
 * 與跨教會候選；驗證無部分寫入、無錯誤失效、類別代碼唯一且稽核最多 200 筆。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Settings;

/// <summary>以操作紀錄 fake 保護設定服務的授權範圍、失敗原子性與有界資料契約。</summary>
public sealed class ChurchSettingsServiceTests
{
    private static readonly string[] DeleteThenTouch = { "delete", "touch" };
    private static readonly string[] UpsertThenTouch = { "upsert", "touch" };
    private static readonly string[] UpsertOnly = { "upsert" };
    private static readonly string[] CategoryOperations = { "categories", "code-exists", "category-add", "categories", "category-update" };
    private static readonly Guid ChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherChurchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly TextContext Context = new(ChurchId, "zh-TW", "zh-TW", "test-org", "ChurchReport");

    /// <summary>超過覆寫本文上限時，必須在任何儲存或快取操作前拒絕。</summary>
    [Fact]
    public async Task Save_TooLong_Rejected()
    {
        var store = new SettingsStore();
        var invalidator = new CacheInvalidator();
        var service = CreateService(store, invalidator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Role.Member", "zh-TW", new string('字', 2001)));

        Assert.Empty(store.Operations);
        Assert.Empty(invalidator.Scopes);
    }

    /// <summary>不在精確白名單中的鍵，包括一般業務鍵，必須在儲存前拒絕。</summary>
    [Fact]
    public async Task Save_NonWhitelistedKey_Rejected()
    {
        var store = new SettingsStore();
        var service = CreateService(store, new CacheInvalidator());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Keyword.Attendance", "zh-TW", "新文字"));

        Assert.Empty(store.Operations);
    }

    /// <summary>還原必須依完整教會、鍵及語系刪除覆寫，成功後更新時間戳並只失效相同 scope。</summary>
    [Fact]
    public async Task Reset_RemovesOverride()
    {
        var store = new SettingsStore();
        var invalidator = new CacheInvalidator();
        var service = CreateService(store, invalidator);

        await service.ResetOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Role.Member", "zh-TW");

        Assert.Equal(DeleteThenTouch, store.Operations.Select(item => item.Name));
        Assert.Equal(ChurchId, store.Operations[0].ChurchId);
        Assert.Single(invalidator.Scopes);
        Assert.True(Context.HasSameScope(invalidator.Scopes[0]));
    }

    /// <summary>白名單必須精確包含 11 個 Role 鍵及單一 FeeNotify 範本，不可多收其他鍵。</summary>
    [Fact]
    public void Whitelist_ContainsElevenRolesAndFeeTemplate()
    {
        var whitelist = new ChurchTermWhitelist();

        Assert.Equal(12, whitelist.Keys.Count);
        Assert.Equal(11, whitelist.Keys.Count(key => key.StartsWith("Role.", StringComparison.Ordinal)));
        Assert.Contains("Fees.Notify.Template", whitelist.Keys);
    }

    /// <summary>關鍵字與 Webhook 路由永遠不屬於顯示文字白名單。</summary>
    [Theory]
    [InlineData("Keyword.Attendance")]
    [InlineData("Webhook.Route.Report")]
    [InlineData("Role.Member.extra")]
    public void Whitelist_RoutingAndKeywordKeys_NeverAllowed(string key)
    {
        Assert.False(new ChurchTermWhitelist().Contains(key));
    }

    /// <summary>FeeNotify 覆寫不得刪除或改名具名參數；參數不符時零儲存、零失效。</summary>
    [Fact]
    public async Task Save_TemplatePlaceholderMismatch_Rejected()
    {
        var store = new SettingsStore();
        var invalidator = new CacheInvalidator();
        var service = CreateService(store, invalidator);

        await Assert.ThrowsAsync<FormatException>(() =>
            service.SaveOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Fees.Notify.Template", "zh-TW", "通知 {member} {date}"));

        Assert.Empty(store.Operations);
        Assert.Empty(invalidator.Scopes);
    }

    /// <summary>成功存檔須使用教會／鍵／語系替代索引鍵，依序觸碰 UTC 時間戳及本機完整 scope 快取。</summary>
    [Fact]
    public async Task SettingsService_Save_UpsertsByAlternateKeyAndTouchesTextChangedOn()
    {
        var store = new SettingsStore();
        var invalidator = new CacheInvalidator();
        var service = CreateService(store, invalidator);

        await service.SaveOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Role.Member", "zh-TW", "新組員");

        Assert.Equal(UpsertThenTouch, store.Operations.Select(item => item.Name));
        Assert.Equal((ChurchId, "Role.Member", "zh-TW"), store.Operations[0].AlternateKey);
        Assert.Equal(DateTimeKind.Utc, store.Operations[1].ChangedOnUtc!.Value.Kind);
        Assert.Single(invalidator.Scopes);
        Assert.True(Context.HasSameScope(invalidator.Scopes[0]));
    }

    /// <summary>Upsert 故障必須原樣傳遞，且不得觸碰 account 時間或失效本機快取。</summary>
    [Fact]
    public async Task SettingsService_SaveFailure_NoTouchNoInvalidate()
    {
        var failure = new InvalidOperationException("synthetic upsert failure");
        var store = new SettingsStore { UpsertFailure = failure };
        var invalidator = new CacheInvalidator();
        var service = CreateService(store, invalidator);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveOverrideAsync(new AuthorizedChurch(ChurchId), Context, "Role.Member", "zh-TW", "新組員"));

        Assert.Same(failure, actual);
        Assert.Equal(UpsertOnly, store.Operations.Select(item => item.Name));
        Assert.Empty(invalidator.Scopes);
    }

    /// <summary>奉獻類別的新增、排序、停用與會計科目更新都必須使用授權教會，拒絕別教會資料。</summary>
    [Fact]
    public async Task Category_AddDisableSortAccountingCode_ChurchScoped()
    {
        var store = new SettingsStore();
        var service = CreateService(store, new CacheInvalidator());
        var church = new AuthorizedChurch(ChurchId);
        var created = await service.AddOfferingCategoryAsync(church, Context, "指定奉獻", 4, "AC-01", null);

        await service.UpdateOfferingCategoryAsync(church, Context, created.Id, "指定奉獻", 2, "AC-02", null, false);

        Assert.All(store.Operations, operation => Assert.Equal(ChurchId, operation.ChurchId));
        Assert.Equal(CategoryOperations,
            store.Operations.Select(item => item.Name));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateOfferingCategoryAsync(church, Context, Guid.NewGuid(), "外教會", 0, null, null, true));
    }

    /// <summary>新類別代碼由服務端產生、格式固定且同教會不得重複，不接受呼叫端提供代碼。</summary>
    [Fact]
    public async Task Category_NewCode_ServerGeneratedUnique()
    {
        var store = new SettingsStore();
        store.ExistingCodes.Add("custom-00000000");
        var service = CreateService(store, new CacheInvalidator());

        var first = await service.AddOfferingCategoryAsync(new AuthorizedChurch(ChurchId), Context, "類別甲", 1, null, null);
        var second = await service.AddOfferingCategoryAsync(new AuthorizedChurch(ChurchId), Context, "類別乙", 2, null, null);

        Assert.Matches("^custom-[0-9a-f]{8}$", first.Code);
        Assert.Matches("^custom-[0-9a-f]{8}$", second.Code);
        Assert.NotEqual(first.Code, second.Code);
        Assert.Contains(first.Code, store.ExistingCodes);
        Assert.Contains(second.Code, store.ExistingCodes);
    }

    /// <summary>稽核查詢必須固定於授權教會並將服務回傳限制在最近 200 筆以內。</summary>
    [Fact]
    public async Task Audit_OnlyThisChurchRecords_Max200()
    {
        var store = new SettingsStore
        {
            AuditRows = Enumerable.Range(0, 250).Select(index =>
                new ChurchSettingsAuditRecord(GuidFrom(index + 1), ChurchId, DateTime.UtcNow.AddMinutes(-index),
                    "new_name", "舊名", "新名", "管理員")).ToArray()
        };
        var service = CreateService(store, new CacheInvalidator());

        var rows = await service.ReadAuditAsync(new AuthorizedChurch(ChurchId), Context);

        Assert.Equal(200, store.AuditLimit);
        Assert.Equal(200, rows.Count);
        Assert.All(rows, row => Assert.Equal(ChurchId, row.ChurchId));
        Assert.Equal(rows.OrderByDescending(row => row.ChangedOnUtc), rows);
    }

    private static ChurchSettingsService CreateService(SettingsStore store, CacheInvalidator invalidator) =>
        new(store, invalidator,
            GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"), new FixedClock());

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 12);
        return new Guid(bytes);
    }

    /// <summary>固定 UTC 時鐘只服務本案例，避免測試依賴執行緒文化或牆上時鐘競態。</summary>
    private sealed class FixedClock : IChurchSettingsClock
    {
        /// <summary>傳回明確 UTC 瞬間；不建立 timer 或背景工作。</summary>
        public DateTime UtcNow => new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>記錄完整操作次序並可在 Upsert 注入原始故障，不連接任何外部儲存。</summary>
    private sealed class SettingsStore : IChurchSettingsStore
    {
        /// <summary>本案例執行過的操作與 scope 摘要。</summary>
        internal List<StoreOperation> Operations { get; } = new();
        /// <summary>可注入的 Upsert 原例外。</summary>
        internal Exception? UpsertFailure { get; init; }
        /// <summary>合成的教會代碼集合。</summary>
        internal HashSet<string> ExistingCodes { get; } = new(StringComparer.Ordinal);
        /// <summary>本案例單一教會候選，讓新增後的正式 ID 可由後續更新流程讀回。</summary>
        internal List<OfferingCategory> Categories { get; } = new();
        /// <summary>供單一 audit 案例回傳的合成列。</summary>
        internal IReadOnlyList<ChurchSettingsAuditRecord> AuditRows { get; init; } = Array.Empty<ChurchSettingsAuditRecord>();
        /// <summary>產品要求的最大稽核讀取數。</summary>
        internal int AuditLimit { get; private set; }

        /// <summary>記錄替代索引鍵 Upsert；可在呼叫邊界重拋相同故障物件。</summary>
        public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken)
        {
            Operations.Add(StoreOperation.ForOverride("upsert", value));
            if (UpsertFailure is not null) throw UpsertFailure;
            return Task.CompletedTask;
        }

        /// <summary>記錄移除教會覆寫。</summary>
        public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("delete", churchId, (churchId, key, culture), null));
            return Task.CompletedTask;
        }

        /// <summary>記錄教會改字時間；測試確認僅服務在前序成功後進入此操作。</summary>
        public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("touch", churchId, default, changedOnUtc));
            return Task.CompletedTask;
        }

        /// <summary>回傳本教會的合成類別候選。</summary>
        public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("categories", churchId, default, null));
            return Task.FromResult<IReadOnlyList<OfferingCategory>>(Categories.ToArray());
        }

        /// <summary>以教會範圍查詢代碼唯一性。</summary>
        public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("code-exists", churchId, default, null));
            return Task.FromResult(ExistingCodes.Contains(code));
        }

        /// <summary>記錄新增的完整類別值並回傳本案例正式合成 ID。</summary>
        public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
            string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("category-add", churchId, default, null));
            ExistingCodes.Add(code);
            var id = GuidFrom(100 + Categories.Count);
            Categories.Add(new OfferingCategory(id, churchId, code, name, sortOrder, accountingCode, legacyOptionValue, true));
            return Task.FromResult(id);
        }

        /// <summary>記錄以正式 ID 更新的同教會類別欄位。</summary>
        public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
            string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken)
        {
            Operations.Add(new StoreOperation("category-update", churchId, default, null));
            return Task.CompletedTask;
        }

        /// <summary>記錄教會範圍與要求筆數，只回傳不超過上限的合成列。</summary>
        public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(Guid churchId, int maximum,
            CancellationToken cancellationToken)
        {
            AuditLimit = maximum;
            return Task.FromResult<IReadOnlyList<ChurchSettingsAuditRecord>>(
                AuditRows.Where(row => row.ChurchId == churchId).Take(maximum).ToArray());
        }
    }

    /// <summary>只記錄失效 context；不保存 snapshot、教會內容或跨案例狀態。</summary>
    private sealed class CacheInvalidator : IChurchSettingsCacheInvalidator
    {
        /// <summary>本案例收到的完整範圍。</summary>
        internal List<TextContext> Scopes { get; } = new();
        /// <summary>只記錄已成功儲存後要求的本機失效。</summary>
        public void Invalidate(TextContext context, DateTime changedOnUtc) => Scopes.Add(context);
    }

    /// <summary>測試專用操作記錄，資料只包含合成 ID、固定鍵與 UTC 時間。</summary>
    private sealed record StoreOperation(string Name, Guid ChurchId,
        (Guid ChurchId, string Key, string Culture) AlternateKey, DateTime? ChangedOnUtc)
    {
        /// <summary>從服務傳入的純值寫入候選記錄替代索引鍵。</summary>
        internal static StoreOperation ForOverride(string name, ChurchTextOverrideWrite value) =>
            new(name, value.ChurchId, (value.ChurchId, value.Key, value.Culture), null);
    }
}
