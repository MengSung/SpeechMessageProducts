/*
 * 檔案整體說明
 *
 * 目的與定位：行銷名單與批次成員操作的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 
 * 已完成安全純化。
 *
 * 主要功能與流程：
 * 本檔宣告 IMarketingListService。
 * - 沿用行銷名單成員查詢；集合只屬本次 caller，正式成員 ID 不同者不可按顯示名稱合併。
 * - 保留舊行銷名單 ArrayList 輸出；caller 獨占資料，不能把暫存位置當正式資料身份。
 * - 加入一筆已授權名單關係；使用正式記錄 ID，是否可重試由 host 的資料庫冪等策略判斷。
 * - 沿用可取消批次加入契約；Task 完成前 caller 不可釋放借入 SDK，取消不代表已提交的寫入自動回復。
 * 資料／選項欄位包含 IMarketingListService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Core/ListOperations/MarketingListService.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 行銷名單與批次成員操作的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。
    /// 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 已完成安全純化。
    /// </summary>
    /// <remarks>
    /// 呼叫端先以伺服器驗證使用者／租戶／教會與資料範圍；任何名稱、路由或 ID 參數均不能建立授權。
    /// 可變 Entity、集合及 ArrayList 僅屬單次操作，caller 不得跨 scope 共用；實作須在發布前驗正式 ID 及完整結果。
    /// SDK 由 host 借入並擁有 timeout、drain 與 Dispose；本契約不保存 request、Session、credential 或執行資源。
    /// 真正故障由實作保留原 Exception 並交最終 host 共用告警；預期查無與明確 caller 取消不偽裝成系統錯誤。
    /// </remarks>
    public interface IMarketingListService
    {
        /// <summary>
        /// 沿用行銷名單成員查詢；集合只屬本次 caller，正式成員 ID 不同者不可按顯示名稱合併。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveMembersByListId(Guid listId);

        /// <summary>
        /// 保留舊行銷名單 ArrayList 輸出；caller 獨占資料，不能把暫存位置當正式資料身份。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        ArrayList GetAllMemberData(Guid listId);

        /// <summary>
        /// 加入一筆已授權名單關係；使用正式記錄 ID，是否可重試由 host 的資料庫冪等策略判斷。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuid">範圍內的正式成員 ID，不以姓名或位置取代。</param>
        void AddMember(Guid listGuid, Guid memberGuid);

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
        /// 沿用 SDK 批次加入及批次上限預設值；host 擁有排程、節流、取消與 drain，不由 Core 契約建立背景工作。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="service">caller 擁有的已授權 SDK，必須等 await／drain 結束後再釋放。</param>
        /// <param name="maxBatchSize">保留舊預設 1000；host 實作負責批次上限與排空。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入同步 SDK 的 I/O，也不等於交易回復。</param>
        /// <returns>已完成工作的 Task；caller 必須 await 以觀察取消／故障及資源排空，回傳成功數不代表可安全重試。</returns>
        Task<int> AddMembersUsingSdkAsync(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service, int maxBatchSize = 1000, CancellationToken cancellationToken = default);

        /// <summary>
        /// 依正式名單及成員 ID 移除關係；只改已授權關係，不以顯示名稱推定目標。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuid">範圍內的正式成員 ID，不以姓名或位置取代。</param>
        void RemoveMember(Guid listGuid, Guid memberGuid);

        /// <summary>
        /// 沿用可取消批次移除契約；已完成批次不因取消回復，caller 須 await 全部工作及診斷失敗。
        /// </summary>
        /// <param name="listGuid">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="memberGuidList">caller 獨占的正式成員 ID 清單；執行中不得並行修改。</param>
        /// <param name="batchSize">保留舊預設 50；實作必須限制工作數並驗證正值。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入同步 SDK 的 I/O，也不等於交易回復。</param>
        /// <returns>已完成工作的 Task；caller 必須 await 以觀察取消／故障及資源排空，回傳成功數不代表可安全重試。</returns>
        Task<int> RemoveMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default);

    }
}
