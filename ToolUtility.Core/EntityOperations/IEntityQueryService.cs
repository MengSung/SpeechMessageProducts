#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 同一已授權 host scope 的純 SDK 查詢契約；不擁有連線、不保存結果或個別使用者狀態。
    /// 單筆核對請求 ID，多筆驗證整個消費頁穩定 ID 後才回傳；SDK 故障交回 host 最終通知邊界。
    /// </summary>
    public interface IEntityQueryService
    {
        /// <summary>依資料庫 ID 直接讀完整欄位，回應 ID 與實體不符時拒絕，不走共用快取。</summary>
        /// <param name="entityName">伺服器已授權的合法實體 logical name。</param>
        /// <param name="entityId">非空的正式資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證實體。</returns>
        Entity RetrieveEntity(string entityName, Guid entityId);

        /// <summary>明確的不經快取入口；每次進 SDK，不能讀回上次 caller 修改或其他 scope 的 Entity。</summary>
        /// <param name="entityName">合法實體名稱，特殊 XML 字元不得作結構名稱。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>新一次 SDK 完整欄位結果；故障不回空或假資料。</returns>
        Entity RetrieveEntityNoCache(string entityName, Guid entityId);

        /// <summary>保留啟用條件與 TopCount=1 單筆語義，以型別化值查詢，不把名稱當唯一資料列 ID。</summary>
        /// <param name="entityName">受信任實體 logical name。</param>
        /// <param name="fieldName">受信任欄位 logical name。</param>
        /// <param name="fieldValue">原資料庫比對值，不保存於服務。</param>
        /// <returns>驗證完整頁後的第一筆，或真正空頁的 null。</returns>
        Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue);

        /// <summary>執行已授權操作局部查詢，保留 SDK 分頁資訊並拒絕同頁重複或欠缺正式 ID。</summary>
        /// <param name="query">由呼叫者擁有且執行期間不被並行修改的 Query。</param>
        /// <returns>完整驗證的單頁，呼叫端負責後續分頁，不共享可變集合。</returns>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>保留依名稱取第一個啟用帳戶 ID 的相容 API，不是多筆集合的名稱去重。</summary>
        /// <param name="accountName">原資料庫名稱條件；不能構成身分或授權。</param>
        /// <returns>首筆正式 ID，或真正空頁的 Guid.Empty；故障直接拋出。</returns>
        Guid RetrieveAccountByName(string accountName);

        /// <summary>安全 XML 查啟用工作，保留100筆上限／原投影／排序，同名不同 ID 的列完整保留。</summary>
        /// <param name="subject">只作屬性值的主旨文字，特殊字元不能注入條件。</param>
        /// <returns>整頁 ID 驗證後的操作局部 SDK 結果。</returns>
        EntityCollection RetrieveTaskBySubject(string subject);

        /// <summary>原樣提交已授權的 RetrieveMultipleRequest，候選完成且全頁 ID 驗證後才發布。</summary>
        /// <param name="request">含 Query 的操作局部 SDK 請求，呼叫期間不可並行修改。</param>
        /// <returns>保留欄位與分頁資訊的完整頁；原 SDK 例外傳至 host。</returns>
        EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request);
    }
}
