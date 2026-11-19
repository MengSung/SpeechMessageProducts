/*
 * 檔案整體說明
 *
 * 目的與定位：以呼叫端已由伺服器授權的 churchId，透過借用的 Dataverse IOrganizationService
 * 讀取奉獻類別並轉成 OfferingCategoryList；不自行判定登入者、不持有連線、不快取結果。
 * 主要流程：驗證教會 ID 後以固定實體、欄位及教會 lookup 建立單次 QueryExpression，最多要求
 * 上限加一筆以偵測超量；每列都必須有資料庫主鍵、正確實體與 account 教會外鍵，最後才建立快照。
 * 信任邊界與生命週期：churchId 必須由上層授權邏輯提供，不能直接信任 request 路由值；SDK 由
 * 呼叫端擁有，本方法同步借用且不 Dispose。候選列只留在單次呼叫期間，不保存跨教會／跨使用者資料。
 * 失敗與驗證：錯誤回應、缺欄、錯教會、重複 ID 或超過 200 列皆拒絕整批，不回傳部分集合；
 * 合成測試驗超量拒絕且只發出一次 SDK 查詢，並非實機查詢證據。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessageProducts.GoodShepherd.Core.Offerings;

/// <summary>單次同步讀取器；服務由呼叫端借用，查詢只限一個已授權教會且沒有跨請求快取。</summary>
public static class OfferingCategoryReader
{
    /// <summary>單教會快照最大資料列數，超量時整份讀取失敗而非截短或分頁發布部分資料。</summary>
    public const int MaximumCategories = 200;
    private const string EntityName = "new_offeringcategory";
    private const string ChurchLookup = "new_accountid";

    /// <summary>依伺服器傳入的教會 ID 執行一次有界同步查詢並發布完整清單。</summary>
    /// <param name="service">由外層組合根持有的 SDK 服務；本方法不釋放或保存此服務。</param>
    /// <param name="churchId">已驗證授權範圍的教會資料庫 ID。</param>
    /// <returns>呼叫端獨立擁有、按穩定欄位排序且最多 200 列的清單快照。</returns>
    public static OfferingCategoryList Read(IOrganizationService service, Guid churchId)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(service);
#else
        if (service is null) throw new ArgumentNullException(nameof(service));
#endif
        if (churchId == Guid.Empty) throw new ArgumentException("A validated church identity is required.", nameof(churchId));

        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet("new_offeringcategoryid", ChurchLookup, "new_code", "new_name",
                "new_sortorder", "new_accountingcode", "new_legacyoptionvalue", "statecode"),
            TopCount = MaximumCategories + 1
        };
        query.Criteria.AddCondition(ChurchLookup, ConditionOperator.Equal, churchId);
        var response = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("Offering category query returned no response.");
        if (response.Entities.Count > MaximumCategories)
            throw new InvalidOperationException("Offering category collection exceeds its bounded limit.");

        var candidate = new List<OfferingCategory>(response.Entities.Count);
        foreach (var row in response.Entities)
        {
            if (row is null || row.LogicalName != EntityName || row.Id == Guid.Empty
                || !row.Attributes.TryGetValue(ChurchLookup, out var churchValue)
                || churchValue is not EntityReference { LogicalName: "account" } churchReference
                || churchReference.Id != churchId)
                throw new InvalidOperationException("Offering category query returned an invalid row scope.");

            candidate.Add(new OfferingCategory(
                row.Id,
                churchId,
                Required<string>(row, "new_code"),
                Required<string>(row, "new_name"),
                RequiredValue<int>(row, "new_sortorder"),
                OptionalReference<string>(row, "new_accountingcode"),
                OptionalValue<int>(row, "new_legacyoptionvalue"),
                Required<OptionSetValue>(row, "statecode").Value == 0));
        }

        return new OfferingCategoryList(candidate);
    }

    /// <summary>只接受明確型別的必要欄位，避免SDK鬆散值被轉型成另一筆業務資料。</summary>
    private static T Required<T>(Entity row, string name) where T : class =>
        row.Attributes.TryGetValue(name, out var value) && value is T typed
            ? typed
            : throw new InvalidOperationException("Offering category row is missing a required field.");

    /// <summary>只接受明確型別的必要值欄位，錯誤型別不以轉型或預設值掩蓋。</summary>
    private static T RequiredValue<T>(Entity row, string name) where T : struct =>
        row.Attributes.TryGetValue(name, out var value) && value is T typed
            ? typed
            : throw new InvalidOperationException("Offering category row is missing a required field.");

    /// <summary>只接受明確型別的選填欄位；缺欄與錯型別不會被混為空值。</summary>
    private static T? OptionalReference<T>(Entity row, string name) where T : class =>
        !row.Attributes.TryGetValue(name, out var value) ? null
            : value is T typed ? typed
            : throw new InvalidOperationException("Offering category row contains an invalid optional field.");

    /// <summary>讀取SDK以Int32直接投影的選填舊選項值；不解讀顯示文字或替代欄位。</summary>
    private static T? OptionalValue<T>(Entity row, string name) where T : struct =>
        !row.Attributes.TryGetValue(name, out var value) ? null
            : value is T typed ? typed
            : throw new InvalidOperationException("Offering category row contains an invalid optional field.");
}
