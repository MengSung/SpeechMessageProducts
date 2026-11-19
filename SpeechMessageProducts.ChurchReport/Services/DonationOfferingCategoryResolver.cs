/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻類別信任邊界，將本次操作已驗證的登入 contact
 * 轉成單一教會範圍，並透過 GoodShepherd.Core 的有界 reader 取得奉獻類別快照。
 * 主要流程：讀取時只接受 contact.parentcustomerid 指向非空 account；正式清單投影為 ID／名稱選項，
 * 空正式清單時才以有明確 Dispose owner 的短命 metadata cache 產生不可提交相容項目；寫入前先依類別
 * Guid 對整批資料逐一驗證，全部存在、啟用且具備必填舊選項值後才回傳完整候選清單；Entity 映射則一次設定 lookup 與舊選項。
 * 適用範圍與不變條件：呼叫端必須提供伺服器驗證的登入 contact，不能使用路由或表單
 * 教會 ID；類別名稱只供顯示，不能作為身分；相同名稱但不同 ID 的類別皆保留。
 * 使用邊界與維護：SDK 服務由呼叫端借用且不 Dispose；每次讀取最多 200 列，快照只在本次
 * 操作存活，不放入靜態欄位、Session 或共享快取，避免跨使用者／教會保留。
 * 失敗處理與驗證：缺少 account 範圍、未知／停用／跨教會類別或必填舊值時 fail closed；
 * 欄位寫入前先驗完整類別，避免 Entity 留下部分映射。合成測試驗證隔離與欄位原子性。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using ToolUtilityNameSpace;

namespace ChurchReport.Services;

/// <summary>
/// 提供奉獻類別的伺服器端教會範圍解析、讀取及寫入映射。
/// </summary>
internal static class DonationOfferingCategoryResolver
{
    /// <summary>
    /// 從已驗證的登入 contact 解析 account 教會範圍。
    /// </summary>
    /// <param name="loginContact">由伺服器登入／授權流程取得的 contact。</param>
    /// <returns>登入 contact 所屬 account 的正式資料庫 ID。</returns>
    /// <exception cref="InvalidOperationException">登入 contact 缺少有效 account 教會關聯。</exception>
    internal static Guid ResolveChurchId(Entity loginContact)
    {
        ArgumentNullException.ThrowIfNull(loginContact);
        if (loginContact.LogicalName != "contact"
            || !loginContact.Attributes.TryGetValue("parentcustomerid", out var value)
            || value is not EntityReference { LogicalName: "account" } church
            || church.Id == Guid.Empty)
        {
            throw new InvalidOperationException("登入聯絡人沒有可驗證的所屬教會。");
        }

        return church.Id;
    }

    /// <summary>
    /// 驗證人工補單的受款 contact 與已驗證登入同工屬於同一教會。
    /// </summary>
    /// <param name="loginContact">由伺服器登入流程提供的同工 contact，唯一授權範圍來源。</param>
    /// <param name="donorContact">依人工補單資料查得的受款 contact，只作為收費單受款人。</param>
    /// <exception cref="InvalidOperationException">任一 contact 缺少有效 account 或兩者教會不同時。</exception>
    /// <remarks>
    /// 人工輸入欄位可決定要查哪位受款人，不能藉此擴張登入者的資料範圍。
    /// 此檢查只讀取呼叫端本次提供的 Entity，不保存身分或教會資料。
    /// </remarks>
    internal static void ValidateKeyInDonor(Entity loginContact, Entity donorContact)
    {
        var loginChurchId = ResolveChurchId(loginContact);
        var donorChurchId = ResolveChurchId(donorContact);
        if (loginChurchId != donorChurchId)
        {
            throw new InvalidOperationException("人工奉獻受款人不屬於登入同工的教會。");
        }
    }

    /// <summary>
    /// 依伺服器登入 contact 的教會讀取單次奉獻類別快照，不保存 SDK 或跨請求資料。
    /// </summary>
    /// <param name="service">由當次 ChurchReport 呼叫端擁有的 Dataverse SDK。</param>
    /// <param name="loginContact">伺服器已驗證的登入 contact，不接受 request 自報教會。</param>
    /// <returns>最多 200 筆、只含登入教會資料的不可變快照。</returns>
    internal static OfferingCategoryList ReadForContact(IOrganizationService service, Entity loginContact)
    {
        ArgumentNullException.ThrowIfNull(service);
        return OfferingCategoryReader.Read(service, ResolveChurchId(loginContact));
    }

    /// <summary>
    /// 為目前已驗證的登入 contact 建立奉獻下拉投影；正式類別存在時只回啟用項目，
    /// 空清單才以舊 OptionSet metadata 顯示不可提交的相容項目。
    /// </summary>
    /// <param name="service">由當次 request／manager 操作借入的 Dataverse SDK。</param>
    /// <param name="loginContact">伺服器已驗證的登入 contact；其 account 是唯一教會範圍。</param>
    /// <returns>依正式類別排序的選項複本；正式類別用 Guid 作值，metadata 項目沒有可提交 ID。</returns>
    /// <remarks>
    /// 不保存 SDK、contact 或跨請求結果。metadata MemoryCache 僅由本方法建立並在同步讀取結束時 Dispose；
    /// metadata 查詢失敗會向上傳遞，不會以固定文字或另一間教會的資料掩蓋。
    /// </remarks>
    internal static List<DonationOfferingCategoryOption> ReadOptionsForContact(
        IOrganizationService service,
        Entity loginContact)
    {
        var categories = ReadForContact(service, loginContact);
        if (categories.Categories.Count > 0)
        {
            return categories.Categories
                .Where(item => item.IsActive)
                .Select(item => new DonationOfferingCategoryOption(
                    item.Id.ToString("D"),
                    item.Name,
                    true))
                .ToList();
        }

        using var metadataCache = new MemoryCache(new MemoryCacheOptions());
        var metadataService = new OptionSetMetadataService(service, cache: metadataCache);
        var mapping = metadataService.GetOptionSetMapping("new_fee", "new_category");
        if (mapping.Count == 0)
        {
            throw new InvalidOperationException("奉獻類別 OptionSet metadata 查詢失敗或沒有可用選項。");
        }

        return mapping.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new DonationOfferingCategoryOption(string.Empty, name, false))
            .ToList();
    }

    /// <summary>
    /// 從本次操作的教會快照解析可建立新記錄的奉獻類別。
    /// </summary>
    /// <param name="churchId">由伺服器登入 contact 解析出的教會 ID。</param>
    /// <param name="categories">同一操作、同一教會讀取的完整快照。</param>
    /// <param name="categoryId">表單送入的正式類別資料庫 ID。</param>
    /// <returns>存在、啟用且屬於該教會的正式類別；無效輸入回傳 null。</returns>
    internal static OfferingCategory? ResolveForWrite(
        Guid churchId,
        OfferingCategoryList categories,
        Guid categoryId)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (churchId == Guid.Empty || categoryId == Guid.Empty)
        {
            return null;
        }

        var category = categories.Categories.FirstOrDefault(item => item.Id == categoryId);
        return category is { IsActive: true } && category.ChurchId == churchId
            ? category
            : null;
    }

    /// <summary>
    /// 在建立任何 CRM 記錄前，完整解析並驗證同一操作的全部類別 ID。
    /// </summary>
    /// <param name="churchId">由伺服器驗證登入 contact 得到的教會範圍。</param>
    /// <param name="categories">同一教會的完整單次讀取快照。</param>
    /// <param name="categoryIds">表單明細依序提交的正式類別 Guid。</param>
    /// <returns>與輸入順序一致的完整、啟用且具舊欄位值類別；沒有任何部分結果發布。</returns>
    /// <exception cref="InvalidOperationException">任一 ID 無效、重複、跨教會、停用或缺少必填舊值。</exception>
    internal static IReadOnlyList<OfferingCategory> ResolveAllForWrite(
        Guid churchId,
        OfferingCategoryList categories,
        IEnumerable<Guid> categoryIds)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(categoryIds);
        var ids = categoryIds.ToArray();
        if (ids.Length == 0 || ids.Length > OfferingCategoryReader.MaximumCategories)
        {
            throw new InvalidOperationException("奉獻類別數量無效。");
        }

        var candidate = new List<OfferingCategory>(ids.Length);
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                throw new InvalidOperationException("同一奉獻付款不可重複選取相同類別 ID。");
            }

            var category = ResolveForWrite(churchId, categories, id);
            if (category is null || !category.LegacyOptionValue.HasValue)
            {
                throw new InvalidOperationException("奉獻類別不存在、已停用、跨教會或缺少必填舊選項值。");
            }

            candidate.Add(category);
        }

        return candidate.AsReadOnly();
    }

    /// <summary>
    /// 驗證完整欄位映射後，將類別 lookup 與必填的舊選項值一次寫入候選 Entity。
    /// </summary>
    /// <param name="target">尚未交給 CRM Create 的操作區域 Entity。</param>
    /// <param name="category">同一授權教會快照中解析出的正式類別。</param>
    /// <param name="lookupAttribute">目標實體的奉獻類別 lookup 欄位。</param>
    /// <param name="legacyAttribute">目標實體既有且必填的選項欄位。</param>
    /// <exception cref="InvalidOperationException">類別或必填舊選項值無效時，在任何欄位變更前拒絕。</exception>
    internal static void ApplyToEntity(
        Entity target,
        OfferingCategory category,
        string lookupAttribute,
        string legacyAttribute)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(category);
        if (string.IsNullOrWhiteSpace(lookupAttribute) || string.IsNullOrWhiteSpace(legacyAttribute))
        {
            throw new ArgumentException("奉獻類別欄位名稱不可為空白。");
        }

        if (category.Id == Guid.Empty
            || category.ChurchId == Guid.Empty
            || !category.IsActive
            || !category.LegacyOptionValue.HasValue)
        {
            throw new InvalidOperationException("奉獻類別缺少有效正式 ID、啟用狀態或必填舊選項值。");
        }

        var lookup = new EntityReference("new_offeringcategory", category.Id);
        var legacy = new OptionSetValue(category.LegacyOptionValue.Value);
        target[lookupAttribute] = lookup;
        target[legacyAttribute] = legacy;
    }

    /// <summary>
    /// 回傳排序第一個啟用類別的正式 ID；空集合保持空值，交由 metadata 相容流程處理。
    /// </summary>
    /// <param name="categories">已排序的單教會類別快照。</param>
    /// <returns>第一個啟用類別資料庫 ID，或沒有啟用類別時的 null。</returns>
    internal static Guid? GetDefaultCategory(OfferingCategoryList categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        return categories.Categories.FirstOrDefault(item => item.IsActive)?.Id;
    }
}
