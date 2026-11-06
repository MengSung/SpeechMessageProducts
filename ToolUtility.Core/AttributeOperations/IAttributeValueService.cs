/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：IAttributeValueReader：無狀態屬性讀取契約；Entity 只在呼叫中借讀，實作不保存輸入、結果或 logger，host 負責授權及快取隔離。；
 * IAttributeValueWriter：無狀態寫入契約；caller 必須獨占目標 Entity，這不是 SDK 持久化或共用快照發布入口。；IRefAttributeClearer：保留原 ref Entity 
 * 清空簽章；明確寫 null 供後續 SDK update，不移除屬性或接管實體所有權。
 *
 * 主要功能與流程：
 * 本檔宣告 IAttributeValueReader、IAttributeValueWriter、IRefAttributeClearer、IAttributeClearer、ILookupAttributeService、
 * IOptionSetAttributeService。
 * - 讀 caller 自有實體的欄位，保留既有轉換及缺值語義；真正失敗直接交 host 共用告警。
 * - 保留 add／replace 及原 SDK 型別包裝；不保存 caller Entity，失敗不得靜默忽略。
 * - 只清空 caller 獨占欄位，無效 Entity／欄位名稱拒絕且不得執行 I/O。
 * - 只清空 caller 獨占 Entity 的指定欄位，不保存 Entity 或建立背景工作。
 * 資料／選項欄位包含 IRefAttributeClearer、IAttributeClearer，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/AttributeOperations/BoolAttributeService.cs、
 * ToolUtility.Core/AttributeOperations/DateTimeAttributeService.cs、
 * ToolUtility.Core/AttributeOperations/DoubleAttributeService.cs、
 * ToolUtility.Core/AttributeOperations/FloatAttributeService.cs、
 * ToolUtility.Core/AttributeOperations/IntAttributeService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>無狀態屬性讀取契約；Entity 只在呼叫中借讀，實作不保存輸入、結果或 logger，host 負責授權及快取隔離。</summary>
    /// <typeparam name="T">此服務原有的讀取型別，缺值 sentinel 由具體型別文件定義。</typeparam>
    public interface IAttributeValueReader<out T>
    {
        /// <summary>讀 caller 自有實體的欄位，保留既有轉換及缺值語義；真正失敗直接交 host 共用告警。</summary>
        T GetAttribute(Entity entity, string propertyName);
    }

    /// <summary>無狀態寫入契約；caller 必須獨占目標 Entity，這不是 SDK 持久化或共用快照發布入口。</summary>
    /// <typeparam name="T">原有 setter 的值型別，不由顯示文字推導正式資料庫身份。</typeparam>
    public interface IAttributeValueWriter<in T>
    {
        /// <summary>保留 add／replace 及原 SDK 型別包裝；不保存 caller Entity，失敗不得靜默忽略。</summary>
        void SetAttribute(ref Entity entity, string propertyName, T value);
    }

    /// <summary>保留原 ref Entity 清空簽章；明確寫 null 供後續 SDK update，不移除屬性或接管實體所有權。</summary>
    public interface IRefAttributeClearer
    {
        /// <summary>只清空 caller 獨占欄位，無效 Entity／欄位名稱拒絕且不得執行 I/O。</summary>
        void SetAttributeToNull(ref Entity entity, string propertyName);
    }

    /// <summary>保留 Float／Double 原非 ref 清空簽章，避免抽取破壞原來源相容與方法群組。</summary>
    public interface IAttributeClearer
    {
        /// <summary>只清空 caller 獨占 Entity 的指定欄位，不保存 Entity 或建立背景工作。</summary>
        void SetAttributeToNull(Entity entity, string propertyName);
    }

    /// <summary>Lookup 的既有非對稱讀寫契約；Guid、EntityReference 與顯示名稱各有明確語義，不作權限判定。</summary>
    public interface ILookupAttributeService : IAttributeValueReader<Guid>
    {
        /// <summary>讀 caller 既有參照的顯示名，缺少為空；顯示名不得作身份、去重或授權依據。</summary>
        string GetDisplayName(Entity entity, string propertyName);
        /// <summary>建立 caller 擁有的 SDK 參照；不存取資料庫、不擅自改變原 setter 的 ID 規則。</summary>
        void SetAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue);
        /// <summary>保留原 ref 參照賦值語義；caller 必須擁有整個 Entity／參照圖，禁止直接傳共享快取。</summary>
        void SetAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference);
        /// <summary>明確寫 null 以清空 lookup，不以空 GUID 建立暫存正式身份。</summary>
        void SetToNull(ref Entity entity, string propertyName);
    }

    /// <summary>OptionSet 保留原 SetAttributeNull 名稱，避免為一致化而破壞既有 API。</summary>
    public interface IOptionSetAttributeService : IAttributeValueReader<int>, IAttributeValueWriter<int>
    {
        /// <summary>在 caller 獨占 Entity 寫 null；服務不保存 SDK OptionSetValue、request 或 logger。</summary>
        void SetAttributeNull(ref Entity entity, string propertyName);
    }
}
