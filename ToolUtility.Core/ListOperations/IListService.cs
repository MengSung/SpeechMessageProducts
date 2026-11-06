/*
 * 檔案整體說明
 *
 * 目的與定位：名單查詢及成員寫入的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 
 * 已完成安全純化。
 *
 * 主要功能與流程：
 * 本檔宣告 IListService。
 * - 沿用名單批次加入入口；caller 提供授權名單與正式成員 ID，不以應用鎖冒充資料庫唯一性。
 * - 借用指定 SDK 加入正式成員；呼叫結束不移轉 SDK 所有權，重試前須釐清伺服器是否已完成寫入。
 * - 依正式名單及成員 ID 移除關係；只改已授權關係，不以顯示名稱推定目標。
 * - 透過借入 SDK 移除關係；連線由 caller 擁有並在全部呼叫 drain 後釋放。
 * 資料／選項欄位包含 IListService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/ListOperations/ListService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 名單查詢及成員寫入的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。
    /// 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 已完成安全純化。
    /// </summary>
    /// <remarks>
    /// 呼叫端先以伺服器驗證使用者／租戶／教會與資料範圍；任何名稱、路由或 ID 參數均不能建立授權。
    /// 可變 Entity、集合及 ArrayList 僅屬單次操作，caller 不得跨 scope 共用；實作須在發布前驗正式 ID 及完整結果。
    /// SDK 由 host 借入並擁有 timeout、drain 與 Dispose；本契約不保存 request、Session、credential 或執行資源。
    /// 真正故障由實作保留原 Exception 並交最終 host 共用告警；預期查無與明確 caller 取消不偽裝成系統錯誤。
    /// </remarks>
    public interface IListService
    {
        /// <summary>
        /// 沿用名單批次加入入口；caller 提供授權名單與正式成員 ID，不以應用鎖冒充資料庫唯一性。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        void AddMembers(Guid listGuid, List<Guid> memberGuidList);

        /// <summary>
        /// 借用指定 SDK 加入正式成員；呼叫結束不移轉 SDK 所有權，重試前須釐清伺服器是否已完成寫入。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="service">caller 擁有的已授權 SDK，必須等 await／drain 結束後再釋放。</param>
        void AddMembersUsingSdk(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service);

        /// <summary>
        /// 依正式名單及成員 ID 移除關係；只改已授權關係，不以顯示名稱推定目標。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuid">範圍內的正式成員 ID，不以姓名或位置取代。</param>
        void RemoveMember(Guid listGuid, Guid memberGuid);

        /// <summary>
        /// 透過借入 SDK 移除關係；連線由 caller 擁有並在全部呼叫 drain 後釋放。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuid">範圍內的正式成員 ID，不以姓名或位置取代。</param>
        /// <param name="service">caller 擁有的已授權 SDK，必須等 await／drain 結束後再釋放。</param>
        void RemoveMemberUsingSdk(Guid listGuid, Guid memberGuid, IOrganizationService service);

        /// <summary>
        /// 查指定名單成員；結果可變且只屬此次 caller，不得放入未分區共用狀態。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveMemberListCollectionByListId(Guid listId);

        /// <summary>
        /// 使用 caller 已授權的 SDK 查名單成員；不建立、重設或處置外部連線。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService? externalService, Guid listId);

        /// <summary>
        /// 保留舊 Proxy 命名但參數仍是 IOrganizationService；不得要求產品直接存取 Proxy 專屬連線屬性。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService? externalService, Guid listId);

        /// <summary>
        /// 解析名單的動態查詢；caller 必須維持原授權 scope，不能把名單查詢文字當授權。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveDynamicMemberList(Guid listId);

        /// <summary>
        /// 使用借入 SDK 執行動態名單查詢；結果與游標只能在同一已驗證 scope 使用。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService? externalService, Guid listId);

        /// <summary>
        /// 保留歷史命名及純 SDK 參數；動態結果不保存在介面層，連線仍由 host 管理。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService? externalService, Guid listId);

        /// <summary>
        /// 沿用名單查詢範圍與排序；caller 擁有單頁結果及後續分頁／身份驗證責任。
        /// </summary>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveLists();

        /// <summary>
        /// 沿用小組名單條件；不得按同名合併不同正式名單，caller 保有結果及分頁所有權。
        /// </summary>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveSmallGroupLists();

        /// <summary>
        /// 依正式聯絡人及關聯名稱查名單；結構名稱由受信任程式決定，不是 caller 輸入的授權依據。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryListByContactId(Guid contactId, string associationName);

        /// <summary>
        /// 保留舊 ArrayList 成員輸出契約；caller 獨占可變結果，成員必須使用正式資料庫 ID。
        /// </summary>
        /// <param name="listEntityId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        System.Collections.ArrayList GetAllMemberDataFromList(Guid listEntityId);

        /// <summary>
        /// 沿用第一筆同名名單或 null；同名不能視為唯一鍵或教會權限，完整集合應走對應查詢。
        /// </summary>
        /// <param name="listName">用於查詢的顯示名稱，不代表唯一名單。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveListEntityByName(string listName);

        /// <summary>
        /// 沿用聯絡人姓名關聯查詢；姓名只是條件，不可作身份、授權或去重鍵。
        /// </summary>
        /// <param name="contactName">聯絡人顯示名稱，只是查詢值或提示。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveListByContact(string contactName);

        /// <summary>
        /// 保留族群領袖名稱與 ID 查詢；正式 ID 才是記錄身份，caller 負責伺服器端 scope 驗證。
        /// </summary>
        /// <param name="contactName">聯絡人顯示名稱，只是查詢值或提示。</param>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveListByRacerLeader(string contactName, string contactId);

        /// <summary>
        /// 沿用可取消批次加入契約；Task 完成前 caller 不可釋放借入 SDK，取消不代表已提交的寫入自動回復。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="batchSize">保留舊預設 50；實作必須限制工作數並驗證正值。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入同步 SDK 的 I/O，也不等於交易回復。</param>
        /// <returns>已完成工作的 Task；caller 必須 await 以觀察取消／故障及資源排空，回傳成功數不代表可安全重試。</returns>
        Task<int> AddMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// 沿用可取消批次移除契約；已完成批次不因取消回復，caller 須 await 全部工作及診斷失敗。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="batchSize">保留舊預設 50；實作必須限制工作數並驗證正值。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入同步 SDK 的 I/O，也不等於交易回復。</param>
        /// <returns>已完成工作的 Task；caller 必須 await 以觀察取消／故障及資源排空，回傳成功數不代表可安全重試。</returns>
        Task<int> RemoveMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// 沿用 SDK 批次加入及批次上限預設值；host 擁有排程、節流、取消與 drain，不由 Core 契約建立背景工作。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="service">caller 擁有的已授權 SDK，必須等 await／drain 結束後再釋放。</param>
        /// <param name="maxBatchSize">保留舊預設 1000；host 實作負責批次上限與排空。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入同步 SDK 的 I/O，也不等於交易回復。</param>
        /// <returns>已完成工作的 Task；caller 必須 await 以觀察取消／故障及資源排空，回傳成功數不代表可安全重試。</returns>
        Task<int> AddMembersUsingSdkAsync(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service, int maxBatchSize = 1000, CancellationToken cancellationToken = default);

    }
}
