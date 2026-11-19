/*
 * 檔案整體說明
 *
 * 目的與定位：定義 ChurchReport 與 LineWebhook 共用的奉獻類別純領域模型及單教會清單快照，
 * 將 Dataverse 資料列轉成可安全交付 UI／訊息組裝端的唯讀值，不依賴部署工具或 Web 主機。
 * 主要流程：OfferingCategory 保存資料庫主鍵、教會外鍵、代碼、名稱、排序、會計代碼、舊選項值
 * 與啟用狀態；OfferingCategoryList 先驗證整批主鍵與教會範圍，再以排序值及代碼建立唯讀快照。
 * 使用邊界與維護：名稱不是身分，同名但資料庫 ID 不同的列必須保留；同一集合重複或空 ID、
 * 空教會 ID、跨教會候選及無效代碼均 fail closed，不發布部分清單。值不含 SDK Entity、Session、
 * credential 或可變呼叫端集合，無快取、背景工作或額外資源。
 * 失敗與驗證：無效單筆資料在建立時拒絕，集合衝突以可診斷 InvalidOperationException 拒絕；
 * OfferingCategoryTests 以合成資料驗證同名保留、重複 ID 拒絕及穩定排序，不代表任何實機結果。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpeechMessageProducts.GoodShepherd.Core.Offerings;

/// <summary>不可變奉獻類別投影；資料庫 ID 是唯一身分，顯示名稱只供呈現且不得用來合併資料列。</summary>
public sealed class OfferingCategory
{
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9-]{0,49}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>建立已驗證的單列值，不保存或延長任何 SDK、使用者或組織連線狀態。</summary>
    /// <param name="id">Dataverse 正式奉獻類別主鍵，不接受臨時 draft 或操作鍵。</param>
    /// <param name="churchId">伺服器解析的所屬教會 ID。</param>
    /// <param name="code">教會範圍內的穩定業務代碼。</param>
    /// <param name="name">只用於顯示的類別名稱；同名記錄可有不同 ID。</param>
    /// <param name="sortOrder">持久化排序值。</param>
    /// <param name="accountingCode">選填會計代碼；保留呼叫端提供的原值。</param>
    /// <param name="legacyOptionValue">選填舊選項值，供相容既有報表與寫入流程。</param>
    /// <param name="isActive">是否可供新增業務記錄選用。</param>
    public OfferingCategory(Guid id, Guid churchId, string code, string name, int sortOrder,
        string? accountingCode, int? legacyOptionValue, bool isActive)
    {
        if (id == Guid.Empty) throw new ArgumentException("Offering category requires a database identity.", nameof(id));
        if (churchId == Guid.Empty) throw new ArgumentException("Offering category requires a church scope.", nameof(churchId));
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || !CodePattern.IsMatch(code))
            throw new ArgumentException("Offering category code is invalid.", nameof(code));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Offering category name is invalid.", nameof(name));
        if (accountingCode is { Length: > 50 } || accountingCode?.Any(char.IsControl) == true)
            throw new ArgumentException("Offering category accounting code is invalid.", nameof(accountingCode));
        if (legacyOptionValue is < 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(legacyOptionValue), "Legacy option value is outside the supported range.");

        Id = id;
        ChurchId = churchId;
        Code = code;
        Name = name;
        SortOrder = sortOrder;
        AccountingCode = accountingCode;
        LegacyOptionValue = legacyOptionValue;
        IsActive = isActive;
    }

    /// <summary>取得正式 Dataverse 主鍵；發布與 UI row key 均必須使用此值。</summary>
    public Guid Id { get; }
    /// <summary>取得此類別所屬教會；清單不得混入其他教會的資料。</summary>
    public Guid ChurchId { get; }
    /// <summary>取得教會範圍內的穩定代碼；排序只在 SortOrder 相同時使用。</summary>
    public string Code { get; }
    /// <summary>取得呈現名稱；名稱相同不代表同一記錄。</summary>
    public string Name { get; }
    /// <summary>取得持久化排序值。</summary>
    public int SortOrder { get; }
    /// <summary>取得選填會計科目代碼。</summary>
    public string? AccountingCode { get; }
    /// <summary>取得選填舊選項值；空值表示本類別不映射舊選項組。</summary>
    public int? LegacyOptionValue { get; }
    /// <summary>取得是否可供新業務記錄選取；停用資料仍可保留於歷史投影。</summary>
    public bool IsActive { get; }
}

/// <summary>一間教會已驗證的不可變奉獻類別快照；建立失敗時不暴露部分候選集合。</summary>
public sealed class OfferingCategoryList
{
    /// <summary>複製並驗證單一教會的候選列，然後以固定次序發布唯讀集合。</summary>
    /// <param name="categories">本次操作完整讀取的候選資料；呼叫端之後修改來源集合不影響快照。</param>
    public OfferingCategoryList(IEnumerable<OfferingCategory> categories)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(categories);
#else
        if (categories is null) throw new ArgumentNullException(nameof(categories));
#endif
        var candidate = categories.ToArray();
        if (candidate.Length > OfferingCategoryReader.MaximumCategories)
            throw new InvalidOperationException("Offering category collection exceeds its bounded limit.");
        if (candidate.Any(item => item is null)) throw new InvalidOperationException("Offering category collection contains an empty row.");
        if (candidate.Select(item => item.ChurchId).Distinct().Skip(1).Any())
            throw new InvalidOperationException("Offering category collection crosses church scope.");
        if (candidate.Select(item => item.Id).Distinct().Count() != candidate.Length)
            throw new InvalidOperationException("Offering category collection contains duplicate database identities.");

        var ordered = candidate.OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
        Categories = new ReadOnlyCollection<OfferingCategory>(ordered);
    }

    /// <summary>取得呼叫端不可修改的完整清單；不同 ID 的同名項目皆保留。</summary>
    public IReadOnlyList<OfferingCategory> Categories { get; }

    /// <summary>建立只有啟用中類別的獨立快照，不改動原集合或任何共享狀態。</summary>
    /// <returns>按原有穩定排序發布的啟用中類別清單。</returns>
    public OfferingCategoryList ActiveOnly() => new(Categories.Where(item => item.IsActive));
}
