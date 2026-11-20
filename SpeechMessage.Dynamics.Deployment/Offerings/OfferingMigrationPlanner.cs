/*
 * 檔案整體說明
 *
 * 目的與定位：Deployment 奉獻類別遷移的純資料計畫核心，將已完整讀取的組織選項 metadata、
 * 教會身份與收費／預約來源摘要轉成單次命令 plan；本檔不連線、不寫入、不讀秘密。
 * 主要流程：先驗證組織身份、教會 ID、metadata 選項值與來源列正式主鍵；以canonical metadata標籤
 * 精確對照固定畫面選項，並集結每教會已使用選項與已對照畫面選項；代碼以選項值生成，
 * new_fee欄位存在時名稱採該欄1028標籤；共享值的booking不同標籤另存於plan差異清單，
 * 同名不同值保留。無法對照的畫面標籤及無法解析教會的來源列各自明確計數／列出。
 * 不變條件與生命週期：來源摘要依 entity／正式 row ID／教會／舊選項穩定排序，不包含可變 lookup，
 * 供 apply 重驗來源漂移與中斷續跑；不輸出原始主鍵清單、不依名稱作唯一性。候選只在單次呼叫存活，
 * 使用邊界與維護：組織、選項、來源列與畫面對照均有明確數量上限；類別候選總量最多50000，
 * 超量在發布plan前拒絕，避免教會×選項展開形成無界記憶體與plan檔案。候選僅存在單次呼叫，
 * 不保留SDK Entity、request、Session、credential、快取或背景資源。
 * 失敗與驗證：重複或空正式 ID、未知實體、單欄重複選項值、模糊畫面標籤或不完整組織身份均拒絕整份 plan；
 * OfferingMigrationPlannerTests 用合成資料驗證分組、metadata文字、同名保留、畫面選項聯集及缺教會處理。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>
/// 由組織choice metadata投影的單一正式選項。Label是遷移採用的server標籤；
/// HasFeeOption與BookingLabel保留兩個來源欄位的存在狀態及原始標籤，供metadata漂移核對。
/// </summary>
internal sealed record OfferingOption(int Value, string Label, string RequiredLevel, string? BookingLabel = null,
    bool HasFeeOption = true);

/// <summary>同值跨欄標籤差異；不改變legacy代碼或新增第二筆類別。</summary>
internal sealed record OfferingLabelDifference(int Value, string FeeLabel, string BookingLabel);

/// <summary>目前畫面提供的固定標籤及原有順序；只供Deployment plan精確映射，不供ChurchReport執行期消費。</summary>
internal sealed record OfferingScreenOption(string Label, int SortOrder);

/// <summary>來源業務列的最小純投影；主鍵是資料庫身份，不帶個人欄位、Entity或查閱名稱。</summary>
internal sealed record OfferingSourceRecord(Guid Id, string Entity, Guid? ChurchId, int? LegacyOptionValue,
    Guid? CategoryId = null, Guid? CategoryChurchId = null);

/// <summary>單教會單選項的待建立類別與來源筆數；身份由ChurchId加LegacyOptionValue決定。</summary>
internal sealed record OfferingCategoryPlan(Guid ChurchId, string Code, string Name, int SortOrder,
    int? LegacyOptionValue, int RecordCount, string RequiredLevel);

/// <summary>可序列化的純轉換計畫內容，不含連線、秘密、業務列主鍵或可變SDK參照。</summary>
internal sealed record OfferingMigrationPlan(Guid OrganizationId, string Organization, DateTimeOffset CreatedUtc,
    IReadOnlyList<OfferingCategoryPlan> Categories, int UnresolvedChurchRecordCount,
    IReadOnlyList<string> UnmatchedScreenLabels, int SourceRecordCount, string SourceFingerprint,
    string MetadataFingerprint, string FeeCategoryRequiredLevel, string BookingCategoryRequiredLevel,
    IReadOnlyList<OfferingLabelDifference> LabelDifferences);

/// <summary>以完整唯讀輸入建立單次遷移候選，身份和文字均不由呼叫端名稱或程式碼對照猜測。</summary>
internal static class OfferingMigrationPlanner
{
    /// <summary>與遷移SDK讀取總筆數一致的最大教會及來源列數；避免單次候選無界成長。</summary>
    internal const int MaximumInputRows = 50_000;
    /// <summary>兩個choice metadata集合聯集的最大選項數量；每個來源欄仍由SDK各自限制256。</summary>
    internal const int MaximumOptions = 512;
    /// <summary>固定畫面映射的最大項目數量。</summary>
    internal const int MaximumScreenOptions = 64;
    /// <summary>類別候選硬上限；沿用資料讀取器最多可觀察的正式類別數量。</summary>
    internal const int MaximumCategoryCandidates = 50_000;

    private static readonly HashSet<string> SourceEntities = new(StringComparer.Ordinal)
    {
        "new_fee", "new_dedication_booking"
    };

    /// <summary>建立一份穩定、可重驗且不發布部分結果的奉獻轉換plan。</summary>
    /// <param name="organizationId">由伺服器取得的正式組織ID。</param>
    /// <param name="organization">已由Deployment白名單核定且與server uniqueName一致的名稱。</param>
    /// <param name="churchIds">本次完整讀取的account正式ID集合。</param>
    /// <param name="metadataOptions">由兩個既有choice欄位metadata核對後的選項純投影。</param>
    /// <param name="records">完整分頁取得的收費單及預約列純投影。</param>
    /// <param name="screenOptions">畫面既有標籤及順序，只用精確label對照metadata值。</param>
    /// <param name="createdUtc">由命令時鐘提供的UTC建立時間。</param>
    /// <param name="feeCategoryRequiredLevel">收費單舊選項欄位的server RequiredLevel，未知時明確標示Unknown。</param>
    /// <param name="bookingCategoryRequiredLevel">奉獻預約舊選項欄位的server RequiredLevel，未知時明確標示Unknown。</param>
    /// <returns>候選plan；所有集合均複製為唯讀資料。</returns>
    internal static OfferingMigrationPlan Create(Guid organizationId, string organization,
        IReadOnlyList<Guid> churchIds, IReadOnlyList<OfferingOption> metadataOptions,
        IReadOnlyList<OfferingSourceRecord> records, IReadOnlyList<OfferingScreenOption> screenOptions,
        DateTimeOffset createdUtc, string feeCategoryRequiredLevel = "Unknown",
        string bookingCategoryRequiredLevel = "Unknown")
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(organization) || organization.Length > 128
            || churchIds is null || churchIds.Count is < 1 or > MaximumInputRows
            || metadataOptions is null || metadataOptions.Count is < 1 or > MaximumOptions
            || records is null || records.Count > MaximumInputRows
            || screenOptions is null || screenOptions.Count > MaximumScreenOptions
            || createdUtc.Offset != TimeSpan.Zero || createdUtc == default
            || !ValidRequiredLevel(feeCategoryRequiredLevel) || !ValidRequiredLevel(bookingCategoryRequiredLevel))
            throw Invalid();
        var churches = new HashSet<Guid>();
        foreach (var churchId in churchIds)
            if (churchId == Guid.Empty || !churches.Add(churchId)) throw Invalid();

        var optionsByValue = new Dictionary<int, OfferingOption>();
        var labels = new Dictionary<string, List<OfferingOption>>(StringComparer.Ordinal);
        foreach (var option in metadataOptions)
        {
            if (option is null || option.Value < 100_000_000 || string.IsNullOrWhiteSpace(option.Label)
                || option.Label.Length > 300 || option.Label.Any(char.IsControl)
                || option.BookingLabel is { } bookingLabel
                    && (string.IsNullOrWhiteSpace(bookingLabel) || bookingLabel.Length > 300
                        || bookingLabel.Any(char.IsControl))
                || !option.HasFeeOption && option.BookingLabel is null
                || string.IsNullOrWhiteSpace(option.RequiredLevel) || option.RequiredLevel.Length > 64
                || !optionsByValue.TryAdd(option.Value, option))
                throw Invalid();
            if (!labels.TryGetValue(option.Label, out var values)) labels.Add(option.Label, values = new List<OfferingOption>(1));
            values.Add(option);
        }

        var screenLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in screenOptions)
            if (item is null || string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 300
                || item.Label.Any(char.IsControl) || item.SortOrder < 0 || !screenLabels.Add(item.Label))
                throw Invalid();

        var recordIds = new HashSet<(string Entity, Guid Id)>();
        var resolvedCounts = new Dictionary<(Guid Church, int Option), int>();
        var unresolvedChurchCount = 0;
        foreach (var record in records)
        {
            if (record is null || record.Id == Guid.Empty || !recordIds.Add((record.Entity, record.Id))
                || !SourceEntities.Contains(record.Entity)
                || record.LegacyOptionValue is { } value && !optionsByValue.ContainsKey(value)
                || record.ChurchId is { } church && !churches.Contains(church))
                throw Invalid();
            if (record.ChurchId is null)
            {
                unresolvedChurchCount = checked(unresolvedChurchCount + 1);
                continue;
            }
            if (record.LegacyOptionValue is { } optionValue)
            {
                var key = (record.ChurchId.Value, optionValue);
                resolvedCounts[key] = checked(resolvedCounts.GetValueOrDefault(key) + 1);
            }
        }

        var displayOrder = new Dictionary<int, int>();
        var unmatched = new List<string>();
        foreach (var screen in screenOptions.OrderBy(item => item.SortOrder))
        {
            if (!labels.TryGetValue(screen.Label, out var matches) || matches.Count != 1)
            {
                unmatched.Add(screen.Label);
                continue;
            }
            displayOrder.TryAdd(matches[0].Value, screen.SortOrder);
        }

        var categories = new List<OfferingCategoryPlan>();
        var labelDifferences = optionsByValue.Values
            .Where(option => option.HasFeeOption && option.BookingLabel is not null
                && !string.Equals(option.Label, option.BookingLabel, StringComparison.Ordinal))
            .OrderBy(option => option.Value)
            .Select(option => new OfferingLabelDifference(option.Value, option.Label, option.BookingLabel!))
            .ToArray();
        foreach (var church in churches.OrderBy(id => id))
        {
            var values = new HashSet<int>(resolvedCounts.Keys.Where(key => key.Church == church).Select(key => key.Option));
            values.UnionWith(displayOrder.Keys);
            foreach (var value in values.OrderBy(value => displayOrder.TryGetValue(value, out var order) ? order : int.MaxValue)
                         .ThenBy(value => value))
            {
                var option = optionsByValue[value];
                if (categories.Count >= MaximumCategoryCandidates) throw Invalid();
                var sortOrder = displayOrder.TryGetValue(value, out var screenOrder)
                    ? screenOrder
                    : checked((screenOptions.Count == 0 ? 0 : screenOptions.Max(item => item.SortOrder) + 1)
                        + values.Where(other => !displayOrder.ContainsKey(other) && other < value).Count());
                categories.Add(new OfferingCategoryPlan(church, "legacy-" + value.ToString(CultureInfo.InvariantCulture),
                    option.Label, sortOrder, value, resolvedCounts.GetValueOrDefault((church, value)), option.RequiredLevel));
            }
        }

        return new OfferingMigrationPlan(organizationId, organization, createdUtc,
            new ReadOnlyCollection<OfferingCategoryPlan>(categories),
            unresolvedChurchCount, new ReadOnlyCollection<string>(unmatched), records.Count, Fingerprint(records),
            FingerprintMetadata(metadataOptions, screenOptions, feeCategoryRequiredLevel, bookingCategoryRequiredLevel),
            feeCategoryRequiredLevel, bookingCategoryRequiredLevel,
            new ReadOnlyCollection<OfferingLabelDifference>(labelDifferences));
    }

    /// <summary>以操作區域的穩定身份序列計算雜湊；不納入會因成功回填而改變的lookup欄。</summary>
    internal static string Fingerprint(IReadOnlyList<OfferingSourceRecord> records)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false), leaveOpen: true))
        {
            foreach (var row in records.OrderBy(item => item.Entity, StringComparer.Ordinal).ThenBy(item => item.Id))
            {
                writer.Write(row.Entity);
                writer.Write(row.Id.ToByteArray());
                writer.Write(row.ChurchId?.ToByteArray() ?? Array.Empty<byte>());
                writer.Write(row.LegacyOptionValue ?? int.MinValue);
            }
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>以排序後的正式choice值、metadata標籤、RequiredLevel及畫面順序核對apply時的中繼資料漂移。</summary>
    internal static string FingerprintMetadata(IReadOnlyList<OfferingOption> metadataOptions,
        IReadOnlyList<OfferingScreenOption> screenOptions, string feeCategoryRequiredLevel = "Unknown",
        string bookingCategoryRequiredLevel = "Unknown")
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false), leaveOpen: true))
        {
            foreach (var option in metadataOptions.OrderBy(item => item.Value))
            {
                writer.Write(option.Value);
                writer.Write(option.Label);
                writer.Write(option.HasFeeOption);
                writer.Write(option.BookingLabel ?? string.Empty);
                writer.Write(option.RequiredLevel);
            }
            writer.Write(-1);
            foreach (var option in screenOptions.OrderBy(item => item.SortOrder).ThenBy(item => item.Label, StringComparer.Ordinal))
            {
                writer.Write(option.SortOrder);
                writer.Write(option.Label);
            }
            writer.Write(feeCategoryRequiredLevel);
            writer.Write(bookingCategoryRequiredLevel);
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>錯誤不回顯組織、標籤、列ID或輸入本文。</summary>
    private static InvalidDataException Invalid() => new("Offering migration source is incomplete or ambiguous.");

    /// <summary>RequiredLevel為server metadata列舉名稱或固定Unknown，不接受錯誤／無界輸出。</summary>
    private static bool ValidRequiredLevel(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsControl);
}
