/*
 * 檔案整體說明
 *
 * 目的與定位：歷史複雜查詢的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 
 * 已完成安全純化。
 *
 * 主要功能與流程：
 * 本檔宣告 IComplexQueryService。
 * - 保留歷史 Many-to-One 查詢入口；本次只抽契約，不改 host 的舊查詢範圍或隱含行為。
 * - 依正式聯絡人查舊排序的第一筆血液報告或 null；敏感資料僅供已授權 caller 使用。
 * - 保留名單、聯絡人及期間參數；不同舊實作的日期算法不在純契約搬移時統一，不能以 host 本地時間當教會時區。
 * - 依受信任結構名稱與已授權父記錄查子集合；動態名稱與 ID 均須由實作驗證，不能以字串直接推定權限。
 * 資料／選項欄位包含 IComplexQueryService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Tests/DataOperations/ComplexQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/ComplexQueryLifecycleTests.cs、
 * ToolUtility.Core/QueryOperations/ComplexQueryDataService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 歷史複雜查詢的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。
    /// 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 已完成安全純化。
    /// </summary>
    /// <remarks>
    /// 呼叫端先以伺服器驗證使用者／租戶／教會與資料範圍；任何名稱、路由或 ID 參數均不能建立授權。
    /// 可變 Entity、集合及 ArrayList 僅屬單次操作，caller 不得跨 scope 共用；實作須在發布前驗正式 ID 及完整結果。
    /// SDK 由 host 借入並擁有 timeout、drain 與 Dispose；本契約不保存 request、Session、credential 或執行資源。
    /// 真正故障由實作保留原 Exception 並交最終 host 共用告警；預期查無與明確 caller 取消不偽裝成系統錯誤。
    /// </remarks>
    public interface IComplexQueryService
    {
        /// <summary>
        /// 保留歷史 Many-to-One 查詢入口；本次只抽契約，不改 host 的舊查詢範圍或隱含行為。
        /// </summary>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveManyToOneCollection();

        /// <summary>
        /// 依正式聯絡人查舊排序的第一筆血液報告或 null；敏感資料僅供已授權 caller 使用。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? QueryBloodReportByContactId(Guid contactId);

        /// <summary>
        /// 保留名單、聯絡人及期間參數；不同舊實作的日期算法不在純契約搬移時統一，不能以 host 本地時間當教會時區。
        /// </summary>
        /// <param name="listEntityId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="monthPeriod">舊查詢期間參數；實際單位與日期算法依對應實作保留。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod);

        /// <summary>
        /// 依受信任結構名稱與已授權父記錄查子集合；動態名稱與 ID 均須由實作驗證，不能以字串直接推定權限。
        /// </summary>
        /// <param name="parentEntityName">受信任程式指定的父表 logical name。</param>
        /// <param name="parentEntityIdName">受信任程式指定的父表主鍵欄位。</param>
        /// <param name="parentEntityId">已授權父記錄的正式資料庫 ID。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <param name="childEntityName">受信任程式指定的子表 logical name。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 保留關聯出席查詢及主日排序；結果是操作自有資料，不是可跨 scope 共用的快照。
        /// </summary>
        /// <param name="parentEntityName">受信任程式指定的父表 logical name。</param>
        /// <param name="parentEntityIdName">受信任程式指定的父表主鍵欄位。</param>
        /// <param name="parentEntityId">已授權父記錄的正式資料庫 ID。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <param name="childEntityName">受信任程式指定的子表 logical name。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 保留最近週數與聯絡人條件；姓名僅顯示提示，正式 ID 與伺服器授權決定查詢範圍。
        /// </summary>
        /// <param name="lastWeeks">舊查詢的最近週數；不是資源可無界擴張的授權。</param>
        /// <param name="contactName">聯絡人顯示名稱，只是查詢值或提示。</param>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId);

        /// <summary>
        /// 查正式週報與聯絡人的出席記錄；不可把不同週報／日期的結果混為同一發布集合。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="weeklyReportEntityId">已授權的正式週報 ID，必須與聯絡人及日期同 scope。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId);

        /// <summary>
        /// 保留歷史關聯查詢名稱；本次不猜測名稱所暗示但原實作未提供的日期條件。
        /// </summary>
        /// <param name="parentEntityName">受信任程式指定的父表 logical name。</param>
        /// <param name="parentEntityIdName">受信任程式指定的父表主鍵欄位。</param>
        /// <param name="parentEntityId">已授權父記錄的正式資料庫 ID。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <param name="childEntityName">受信任程式指定的子表 logical name。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 沿用指定主日與名單／關聯範圍查週報；日期已由 caller 決定，不在純契約層轉換時區。
        /// </summary>
        /// <param name="sunday">caller 已決定的教會主日日期，本介面不讀取系統本地時間。</param>
        /// <param name="parentEntityName">受信任程式指定的父表 logical name。</param>
        /// <param name="parentEntityIdName">受信任程式指定的父表主鍵欄位。</param>
        /// <param name="parentEntityId">已授權父記錄的正式資料庫 ID。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <param name="childEntityName">受信任程式指定的子表 logical name。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 沿用指定主日與名單／關聯範圍查週報；日期已由 caller 決定，不在純契約層轉換時區。
        /// </summary>
        /// <param name="sunday">caller 已決定的教會主日日期，本介面不讀取系統本地時間。</param>
        /// <param name="listEntityId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 保留主日前兩月的舊週報期間語義；caller 的主日與正式名單必須屬同一已授權 scope。
        /// </summary>
        /// <param name="sunday">caller 已決定的教會主日日期，本介面不讀取系統本地時間。</param>
        /// <param name="listEntityId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryWeeklyReportBeforeTwoMonthsOfSunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 保留關聯名單與名稱排序；名稱只用來排序，不能拿來去重或代替正式名單 ID。
        /// </summary>
        /// <param name="parentEntityName">受信任程式指定的父表 logical name。</param>
        /// <param name="parentEntityIdName">受信任程式指定的父表主鍵欄位。</param>
        /// <param name="parentEntityId">已授權父記錄的正式資料庫 ID。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <param name="childEntityName">受信任程式指定的子表 logical name。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 依正式聯絡人及關聯名稱查名單；結構名稱由受信任程式決定，不是 caller 輸入的授權依據。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="associationName">受信任程式指定的 CRM 關聯 logical name，不由此建立授權。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryListByContactId(Guid contactId, string associationName);

        /// <summary>
        /// 保留歷史 Collection 命名但實際回單筆或 null；不得藉此 API 名稱推論結果完整或可授權。
        /// </summary>
        /// <param name="lineId">已驗證流程提供的 LINE 綁定值，不得輸出至診斷。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveContactCollectionByLineId(string lineId);

        /// <summary>
        /// 保留 D365 名單成員舊入口；caller 必須依分頁 metadata 確認結果完整後才發布。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid listId);

        /// <summary>
        /// 保留 D365 動態名單入口；借入服務與動態查詢只活於原 host scope，不在純契約層快取。
        /// </summary>
        /// <param name="listId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveDynamicMemberListDynamics365(Guid listId);

    }
}
