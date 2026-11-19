/*
 * 檔案整體說明
 *
 * 目的與定位：協調奉獻類別 plan 的 apply／verify 邊界；實際組織授權、OS寫鎖與唯一SDK lease
 * 由 Deployment CLI 持有，底層 store 只借用該命令服務，不在本類建立連線或保存跨命令狀態。
 * 主要功能與流程：apply依序驗證plan與來源、Upsert每間教會類別、回填空lookup；verify彙整缺值及跨教會參照。
 * Apply 流程先比較 plan 檔 SHA，再由同一命令的server metadata及完整來源重建候選plan並逐欄比對；
 * server 組織身份、標籤、RequiredLevel、類別名稱／順序／計數、未對應標籤與來源摘要皆須一致。
 * 完成前置核對後，
 * 先依教會＋legacy代碼Upsert所有類別，再以最多100筆 ExecuteMultiple 回填空lookup。每筆response
 * 必須齊全且成功，否則停止後續批次；每批僅記實體／批號／筆數／固定錯誤碼並flush，flush失敗也停止。
 * 重跑會重新讀來源，已有lookup不重寫，因此已確認完成的批次可安全續跑；未確認故障批次不自動重送。
 * Verify 對每個教會與舊選項值統計有選項但缺lookup的筆數，並分別列出指向其他教會的lookup。
 * 使用邊界與維護：store服務由CLI lease唯一擁有；本類只持有操作區域候選，無static cache、
 * Task、timer、retry loop或credential。失敗保持原始例外，不將批次摘要當成交易完成證明。
 * 驗證：OfferingMigrationExecutorTests注入SHA／組織漂移、第二批中斷、100筆邊界及跨教會lookup；
 * 替身測試不代表Dataverse實機ExecuteMultiple結果。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>一次來源列查閱更新；只用資料庫身份及本次plan類別ID，不保存Entity或使用者欄位。</summary>
internal sealed record OfferingLookupUpdate(string Entity, Guid RecordId, Guid ChurchId, Guid CategoryId);

/// <summary>ExecuteMultiple中單筆回應的最小安全投影；不含fault message或SDK本文。</summary>
internal sealed record OfferingUpdateResult(bool Succeeded, string ErrorCode);

/// <summary>verify對跨教會查閱的安全摘要，不包含來源主鍵或顯示名稱。</summary>
internal sealed record OfferingForeignChurchLookup(string Entity, Guid ExpectedChurchId, Guid ActualChurchId);

/// <summary>每批操作的最小JSONL安全摘要；不保存教會名稱、資料列ID、例外本文或秘密。</summary>
internal sealed record OfferingMigrationBatchLog(int Batch, string Entity, int Requested, int Succeeded,
    int Failed, string ErrorCodes);

/// <summary>唯讀核對結果；所有集合為呼叫端獨有快照。</summary>
internal sealed record OfferingVerificationResult(
    IReadOnlyDictionary<(Guid ChurchId, int OptionValue), int> MissingLookupByChurch,
    IReadOnlyList<OfferingForeignChurchLookup> ForeignChurchLookups,
    int UnresolvedChurchRecordCount);

/// <summary>CLI持有組織寫鎖及lease期間借入的操作介面；實作不得快取或自行Dispose服務。</summary>
internal interface IOfferingMigrationStore
{
    /// <summary>取得server核對後的正式組織ID。</summary>
    Guid OrganizationId { get; }
    /// <summary>取得server核對後且與白名單一致的uniqueName。</summary>
    string OrganizationName { get; }
    /// <summary>本命令讀到的選項標籤、RequiredLevel及目前畫面映射摘要。</summary>
    string MetadataFingerprint { get; }
    /// <summary>完整重新投影來源列；結果不得包含使用者個資或SDK物件。</summary>
    IReadOnlyList<OfferingSourceRecord> ReadSourceRecords();
    /// <summary>依本次server metadata與完整來源重建plan；createdUtc只沿用待核對plan的非權威時間欄。</summary>
    OfferingMigrationPlan RecreatePlan(DateTimeOffset createdUtc, IReadOnlyList<OfferingSourceRecord> records);
    /// <summary>以教會＋代碼唯一替代索引鍵原子Upsert一筆奉獻類別。</summary>
    Guid UpsertCategory(OfferingCategoryPlan category);
    /// <summary>執行一個不超過100筆的ExecuteMultiple批次，回傳逐筆結果且不吞掉原SDK例外。</summary>
    IReadOnlyList<OfferingUpdateResult> ExecuteMultiple(IReadOnlyList<OfferingLookupUpdate> updates);
}

/// <summary>奉獻類別轉換apply與verify協調器；無狀態，所有服務及結果生命週期皆屬單一呼叫。</summary>
internal static class OfferingMigrationExecutor
{
    private const int BatchSize = 100;
    private static readonly JsonSerializerOptions BatchLogJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>在寫入前完整核對hash、組織和來源；先upsert類別再分批補空lookup，中途失敗停止並保留flush紀錄。</summary>
    /// <param name="store">由外層CLI租約借入的組織store，不由本方法釋放。</param>
    /// <param name="plan">已通過嚴格JSON解析並由人工保存的plan純值。</param>
    /// <param name="providedPlanSha256">CLI從plan檔原始位元組計算的SHA-256。</param>
    /// <param name="expectedPlanSha256">使用者隨apply提供、與檔案摘要比較的SHA-256。</param>
    /// <param name="operationLog">該次操作專屬安全紀錄writer；由呼叫端擁有，批次後必須flush成功。</param>
    internal static void Apply(IOfferingMigrationStore store, OfferingMigrationPlan plan,
        string providedPlanSha256, string expectedPlanSha256, TextWriter operationLog)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operationLog);
        if (!FixedHashEquals(providedPlanSha256, expectedPlanSha256))
            throw new InvalidDataException("Offering migration plan hash is invalid.");
        if (store.OrganizationId == Guid.Empty || store.OrganizationId != plan.OrganizationId
            || !string.Equals(store.OrganizationName, plan.Organization, StringComparison.Ordinal))
            throw new InvalidDataException("Offering migration plan belongs to another server organization.");

        if (!FixedHashEquals(store.MetadataFingerprint, plan.MetadataFingerprint))
            throw new InvalidDataException("Offering migration metadata changed after planning.");
        var records = store.ReadSourceRecords();
        var current = store.RecreatePlan(plan.CreatedUtc, records);
        if (!PlanContentMatches(plan, current))
            throw new InvalidDataException("Offering migration plan does not match current server metadata and source.");
        var categories = ValidateCategories(plan.Categories);

        var categoryIds = new Dictionary<(Guid ChurchId, int OptionValue), Guid>();
        foreach (var pair in categories)
        {
            var categoryId = store.UpsertCategory(pair.Value);
            if (categoryId == Guid.Empty)
                throw new InvalidDataException("Offering category Upsert did not return a database identity.");
            categoryIds.Add(pair.Key, categoryId);
        }

        var batchNumber = 0;
        foreach (var entityGroup in records.Where(row => row.ChurchId.HasValue && row.LegacyOptionValue.HasValue && !row.CategoryId.HasValue)
                     .GroupBy(row => row.Entity, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var pending = entityGroup.Select(row =>
            {
                var key = (row.ChurchId!.Value, row.LegacyOptionValue!.Value);
                if (!categoryIds.TryGetValue(key, out var categoryId))
                    throw new InvalidDataException("Offering migration source has no planned category.");
                return new OfferingLookupUpdate(row.Entity, row.Id, key.Item1, categoryId);
            });
            foreach (var batch in pending.Chunk(BatchSize))
            {
                var updates = Array.AsReadOnly(batch);
                var results = store.ExecuteMultiple(updates);
                if (results is null || results.Count != updates.Count)
                    throw new InvalidDataException("Offering migration batch response is incomplete.");
                var failures = results.Count(result => result is null || !result.Succeeded);
                var errorCodes = FormatErrorCodes(results);
                batchNumber = checked(batchNumber + 1);
                operationLog.WriteLine(JsonSerializer.Serialize(new OfferingMigrationBatchLog(batchNumber,
                    entityGroup.Key, updates.Count, updates.Count - failures, failures, errorCodes), BatchLogJson));
                operationLog.Flush();
                if (failures != 0) throw new InvalidDataException("Offering migration batch reported a failed request.");
            }
        }
    }

    /// <summary>唯讀彙總目前缺lookup、無法解析教會及跨教會lookup；不依顯示名稱判定成功。</summary>
    /// <param name="store">由命令lease借入的唯讀投影store。</param>
    /// <returns>教會／選項值分組計數及有界錯誤列摘要。</returns>
    internal static OfferingVerificationResult Verify(IOfferingMigrationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var missing = new Dictionary<(Guid ChurchId, int OptionValue), int>();
        var foreign = new List<OfferingForeignChurchLookup>();
        var unresolved = 0;
        foreach (var row in store.ReadSourceRecords())
        {
            if (row.ChurchId is null)
            {
                unresolved = checked(unresolved + 1);
                continue;
            }
            if (row.CategoryId.HasValue)
            {
                if (row.CategoryChurchId is { } actual && actual != row.ChurchId.Value)
                    foreign.Add(new OfferingForeignChurchLookup(row.Entity, row.ChurchId.Value, actual));
                continue;
            }
            if (row.LegacyOptionValue is { } option)
            {
                var key = (row.ChurchId.Value, option);
                missing[key] = checked(missing.GetValueOrDefault(key) + 1);
            }
        }
        return new OfferingVerificationResult(
            new ReadOnlyDictionary<(Guid ChurchId, int OptionValue), int>(missing),
            new ReadOnlyCollection<OfferingForeignChurchLookup>(foreign), unresolved);
    }

    /// <summary>Plan類別主鍵以教會＋legacy值保持唯一；同名或相近名不同值不可合併。</summary>
    private static Dictionary<(Guid ChurchId, int OptionValue), OfferingCategoryPlan> ValidateCategories(
        IReadOnlyList<OfferingCategoryPlan> categories)
    {
        var result = new Dictionary<(Guid ChurchId, int OptionValue), OfferingCategoryPlan>();
        foreach (var category in categories)
        {
            if (category is null || category.ChurchId == Guid.Empty || category.LegacyOptionValue is not { } value
                || category.Code != "legacy-" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || string.IsNullOrWhiteSpace(category.Name) || category.RecordCount < 0
                || !result.TryAdd((category.ChurchId, value), category))
                throw new InvalidDataException("Offering migration category identity is invalid.");
        }
        return result;
    }

    /// <summary>比較所有具寫入影響的重建欄位；建立時間不授權任何候選內容，故不參與比對。</summary>
    private static bool PlanContentMatches(OfferingMigrationPlan supplied, OfferingMigrationPlan current) =>
        supplied.Categories is not null
        && supplied.UnmatchedScreenLabels is not null
        && supplied.OrganizationId == current.OrganizationId
        && string.Equals(supplied.Organization, current.Organization, StringComparison.Ordinal)
        && supplied.UnresolvedChurchRecordCount == current.UnresolvedChurchRecordCount
        && supplied.SourceRecordCount == current.SourceRecordCount
        && FixedHashEquals(supplied.SourceFingerprint, current.SourceFingerprint)
        && FixedHashEquals(supplied.MetadataFingerprint, current.MetadataFingerprint)
        && string.Equals(supplied.FeeCategoryRequiredLevel, current.FeeCategoryRequiredLevel, StringComparison.Ordinal)
        && string.Equals(supplied.BookingCategoryRequiredLevel, current.BookingCategoryRequiredLevel, StringComparison.Ordinal)
        && supplied.Categories.SequenceEqual(current.Categories)
        && supplied.UnmatchedScreenLabels.SequenceEqual(current.UnmatchedScreenLabels, StringComparer.Ordinal);

    /// <summary>固定比對64字元ASCII十六進位雜湊，不以長度或大小寫正規化替代完整檔案雜湊。</summary>
    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    /// <summary>固定時間比較來源摘要，避免一般字串比較短路並保持同一校驗語意。</summary>
    private static bool FixedHashEquals(string left, string right) =>
        ValidHash(left) && ValidHash(right)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    /// <summary>只保留SDK fault的固定八位十六進位數字碼；替身或其他值一律降為unknown。</summary>
    private static string FormatErrorCodes(IReadOnlyList<OfferingUpdateResult> results)
    {
        var codes = results.Where(result => result is { Succeeded: false })
            .Select(result => result!.ErrorCode)
            .Select(value => value is { Length: 10 } && value.StartsWith("0x", StringComparison.Ordinal)
                && value.AsSpan(2).ToString().All(char.IsAsciiHexDigit) ? value.ToUpperInvariant() : "unknown")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return codes.Length == 0 ? "none" : string.Join(",", codes);
    }
}
