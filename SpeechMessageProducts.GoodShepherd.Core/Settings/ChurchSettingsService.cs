/*
 * 檔案整體說明
 *
 * 目的與定位：GoodShepherd.Core 的純領域設定服務，承接白名單覆寫、教會奉獻類別與有限修改紀錄。
 * 主要流程：呼叫端先以伺服器授權流程建立 AuthorizedChurch 及完整 TextContext；服務驗證範圍與內容，
 * 依序寫入替代索引鍵、更新 UTC new_textchangedon，再使相同 scope 的本程序快取失效。
 * 類別操作每次以授權教會重新讀取最多 200 筆候選，驗證正式 ID／所屬教會後才修改；
 * 新代碼由本服務以密碼學亂數產生並經同教會衝突檢查。稽核查詢只發布同教會且最多 200 筆。
 * 使用邊界與維護：本服務不解析 HTTP、Session 或裸 request ID，不持有共享可變狀態、SDK、連線或背景資源。
 * store、clock 與 cache invalidator 由呼叫端 DI owner 注入；取消只作用於本次作業，failure 保留原始 Exception，
 * 不在鎖內執行 I/O，也不把操作結果跨 request 快取。
 * 失敗與驗證：上游 Upsert 失敗時不觸碰時間戳或快取；時間戳失敗時不失效快取；任一階段未完成均不回報成功。
 * 離線測試以可注入 fault 的 store 驗證順序、隔離、模板參數、唯一代碼及 200 筆上限。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;

namespace SpeechMessageProducts.GoodShepherd.Core.Settings;

/// <summary>在伺服器授權邊界之後執行的純核心教會設定服務；不自行授權或連接外部儲存。</summary>
public sealed class ChurchSettingsService
{
    private const int MaximumTextLength = 2000;
    private const int MaximumCategories = 200;
    private const int MaximumAuditRows = 200;
    private const int MaximumCodeAttempts = 8;
    private readonly IChurchSettingsStore store;
    private readonly IChurchSettingsCacheInvalidator invalidator;
    private readonly ChurchTextOverrideValidator overrideValidator;
    private readonly IChurchSettingsClock clock;
    private readonly ChurchTermWhitelist whitelist = new();

    /// <summary>注入本次產品容器擁有的儲存與快取 port；服務本身不取得其 Dispose 所有權。</summary>
    /// <param name="store">依教會 ID 隔離的儲存 adapter。</param>
    /// <param name="invalidator">借用既有 cache owner 的完整 scope 失效 adapter。</param>
    /// <param name="overrideValidator">借用既有系統資源驗證器，不取得其資源所有權。</param>
    /// <param name="clock">每次呼叫取得 UTC 的無狀態時鐘。</param>
    public ChurchSettingsService(IChurchSettingsStore store, IChurchSettingsCacheInvalidator invalidator,
        ChurchTextOverrideValidator overrideValidator, IChurchSettingsClock clock)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
        this.overrideValidator = overrideValidator ?? throw new ArgumentNullException(nameof(overrideValidator));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>驗證白名單、長度與具名參數後，依序 Upsert、觸碰 UTC 版本並失效相同快取 scope。</summary>
    public async Task SaveOverrideAsync(AuthorizedChurch church, TextContext context, string key, string culture,
        string text, CancellationToken cancellationToken = default)
    {
        RequireScope(church, context);
        ValidateOverride(key, culture, text, context);
        var changedOnUtc = ReadUtcNow();
        await store.UpsertOverrideAsync(new ChurchTextOverrideWrite(church.Id, key, culture, text), cancellationToken)
            .ConfigureAwait(false);
        await store.TouchTextChangedOnAsync(church.Id, changedOnUtc, cancellationToken).ConfigureAwait(false);
        invalidator.Invalidate(context, changedOnUtc);
    }

    /// <summary>依相同 scope 移除白名單覆寫，再更新 account 版本並立即失效本機快取。</summary>
    public async Task ResetOverrideAsync(AuthorizedChurch church, TextContext context, string key, string culture,
        CancellationToken cancellationToken = default)
    {
        RequireScope(church, context);
        if (!whitelist.Contains(key)) throw new ArgumentException("The text key is not editable.", nameof(key));
        ValidateCulture(culture);
        var changedOnUtc = ReadUtcNow();
        await store.RemoveOverrideAsync(church.Id, key, culture, cancellationToken).ConfigureAwait(false);
        await store.TouchTextChangedOnAsync(church.Id, changedOnUtc, cancellationToken).ConfigureAwait(false);
        invalidator.Invalidate(context, changedOnUtc);
    }

    /// <summary>新增本教會類別；代碼由伺服器安全產生，經同教會查重後才寫入並返回資料庫主鍵。</summary>
    public async Task<OfferingCategory> AddOfferingCategoryAsync(AuthorizedChurch church, TextContext context,
        string name, int sortOrder, string? accountingCode, int? legacyOptionValue,
        CancellationToken cancellationToken = default)
    {
        RequireScope(church, context);
        ValidateCategoryFields(name, sortOrder, accountingCode);
        var existing = await ReadCategoriesAsync(church.Id, cancellationToken).ConfigureAwait(false);
        if (existing.Count >= MaximumCategories)
            throw new InvalidOperationException("The church offering category limit has been reached.");
        var code = await CreateUniqueCategoryCodeAsync(church.Id, cancellationToken).ConfigureAwait(false);
        var id = await store.AddCategoryAsync(church.Id, code, name, sortOrder, accountingCode, legacyOptionValue,
            cancellationToken).ConfigureAwait(false);
        if (id == Guid.Empty) throw new InvalidOperationException("The category store returned no stable identity.");
        return new OfferingCategory(id, church.Id, code, name, sortOrder, accountingCode, legacyOptionValue, true);
    }

    /// <summary>只更新在當次教會候選中存在的正式類別 ID，拒絕跨教會或不存在的 ID。</summary>
    public async Task UpdateOfferingCategoryAsync(AuthorizedChurch church, TextContext context, Guid id,
        string name, int sortOrder, string? accountingCode, int? legacyOptionValue, bool isActive,
        CancellationToken cancellationToken = default)
    {
        RequireScope(church, context);
        if (id == Guid.Empty) throw new ArgumentException("A category database identity is required.", nameof(id));
        ValidateCategoryFields(name, sortOrder, accountingCode);
        var existing = await ReadCategoriesAsync(church.Id, cancellationToken).ConfigureAwait(false);
        if (!existing.Any(category => category.Id == id && category.ChurchId == church.Id))
            throw new ArgumentException("The category does not belong to the authorized church.", nameof(id));
        await store.UpdateCategoryAsync(church.Id, id, name, sortOrder, accountingCode, legacyOptionValue, isActive,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>讀取本教會最近最多 200 筆修改紀錄，並再次驗證正式身分與 scope 後才發布唯讀結果。</summary>
    public async Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(AuthorizedChurch church,
        TextContext context, CancellationToken cancellationToken = default)
    {
        RequireScope(church, context);
        var rows = await store.ReadAuditAsync(church.Id, MaximumAuditRows, cancellationToken).ConfigureAwait(false);
        if (rows is null || rows.Count > MaximumAuditRows)
            throw new InvalidOperationException("The audit store exceeded its bounded result contract.");
        var identities = new HashSet<Guid>();
        var candidate = new List<ChurchSettingsAuditRecord>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null || row.Id == Guid.Empty || row.ChurchId != church.Id
                || row.TargetRecordId == Guid.Empty
                || row.ChangedOnUtc.Kind != DateTimeKind.Utc || !identities.Add(row.Id))
                throw new InvalidOperationException("The audit store returned an invalid church-scoped candidate.");
            candidate.Add(row);
        }
        return candidate.OrderByDescending(row => row.ChangedOnUtc).ToArray();
    }

    private async Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId,
        CancellationToken cancellationToken)
    {
        var rows = await store.ReadCategoriesAsync(churchId, cancellationToken).ConfigureAwait(false);
        if (rows is null) throw new InvalidOperationException("The category store returned no candidate.");
        var list = new OfferingCategoryList(rows);
        if (list.Categories.Count > MaximumCategories
            || list.Categories.Any(category => category.ChurchId != churchId || category.Id == Guid.Empty))
            throw new InvalidOperationException("The category store returned an invalid church-scoped candidate.");
        return list.Categories;
    }

    private async Task<string> CreateUniqueCategoryCodeAsync(Guid churchId, CancellationToken cancellationToken)
    {
        using var random = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        for (var attempt = 0; attempt < MaximumCodeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            random.GetBytes(bytes);
            var code = "custom-" + ToLowerHex(bytes);
            if (!await store.CategoryCodeExistsAsync(churchId, code, cancellationToken).ConfigureAwait(false))
                return code;
        }
        throw new InvalidOperationException("Unable to allocate a unique church category code.");
    }

    private void ValidateOverride(string key, string culture, string text, TextContext context)
    {
        if (!whitelist.Contains(key)) throw new ArgumentException("The text key is not editable.", nameof(key));
        ValidateCulture(culture);
        if (text is null || text.Length > MaximumTextLength)
            throw new ArgumentException("Override text exceeds its bounded length.", nameof(text));
        var textKey = new TextKey(key);
        overrideValidator.ValidateOverride(context, textKey, culture, text);
    }

    private static void ValidateCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture) || culture.Length > 16)
            throw new ArgumentException("A bounded culture is required.", nameof(culture));
        if (!string.Equals(culture, "*", StringComparison.Ordinal))
        {
            try
            {
                if (CultureInfo.GetCultureInfo(culture).Name.Length == 0)
                    throw new ArgumentException("An explicit culture is required.", nameof(culture));
            }
            catch (CultureNotFoundException exception)
            {
                throw new ArgumentException("A valid culture is required.", nameof(culture), exception);
            }
        }
    }

    private static void ValidateCategoryFields(string name, int sortOrder, string? accountingCode)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("A bounded category name is required.", nameof(name));
#if NET10_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
#else
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
#endif
        if (accountingCode is { Length: > 100 })
            throw new ArgumentException("The accounting code exceeds its bounded length.", nameof(accountingCode));
        ValidateUnicode(name);
        if (accountingCode is not null) ValidateUnicode(accountingCode);
    }

    private static void RequireScope(AuthorizedChurch church, TextContext context)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(church);
        ArgumentNullException.ThrowIfNull(context);
#else
        if (church is null) throw new ArgumentNullException(nameof(church));
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        if (church.Id == Guid.Empty || context.ChurchId != church.Id)
            throw new ArgumentException("The authorized church and complete text scope do not match.", nameof(context));
    }

    private DateTime ReadUtcNow()
    {
        var value = clock.UtcNow;
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The settings clock must provide UTC.");
        return value;
    }

    /// <summary>拒絕不成對 UTF-16 surrogate，避免儲存或傳輸時文字被不同元件改寫。</summary>
    /// <param name="value">本次設定操作的文字純值，不會被服務留存。</param>
    private static void ValidateUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                    throw new ArgumentException("Text contains invalid Unicode.");
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw new ArgumentException("Text contains invalid Unicode.");
            }
        }
    }

    /// <summary>將伺服器產生的亂數代碼轉成雙目標一致的小寫十六進位格式。</summary>
    /// <param name="bytes">本次呼叫獨有的四個亂數位元組。</param>
    /// <returns>固定八碼、不含連字號的小寫十六進位文字。</returns>
    private static string ToLowerHex(byte[] bytes)
    {
#if NET10_0_OR_GREATER
        return Convert.ToHexStringLower(bytes);
#else
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
#endif
    }
}
