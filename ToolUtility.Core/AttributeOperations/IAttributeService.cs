/*
 * 檔案整體說明
 *
 * 目的與定位：純 Entity 屬性轉換契約，不保存 request、logger 或結果；caller 須獨占被修改的 Entity，host 快取必須另行隔離。 缺值預設由各 getter 明列；真正轉換失敗交回 
 * host 共用告警，不改全域文化或從顯示名推算正式身份。
 *
 * 主要功能與流程：
 * 本檔宣告 IAttributeService。
 * - 缺少 Entity／欄位回 false；原 bool 直接回傳，其他值沿用 caller CurrentCulture 的 Convert.ToBoolean，不改程序文化。
 * - 只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。
 * - 缺少 Entity／欄位回 0；原 int 直接回傳，其他值沿用 caller CurrentCulture 的 Convert.ToInt32，不改程序文化。
 * - 缺少 Entity／欄位或明確 null 回空字串；其他值沿用 ToString，真正格式化故障直接上拋而非偽裝空資料。
 * 資料／選項欄位包含 IAttributeService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/AttributeOperations/AttributeServiceComposite.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 純 Entity 屬性轉換契約，不保存 request、logger 或結果；caller 須獨占被修改的 Entity，host 快取必須另行隔離。
    /// 缺值預設由各 getter 明列；真正轉換失敗交回 host 共用告警，不改全域文化或從顯示名推算正式身份。
    /// </summary>
    public interface IAttributeService
    {
        // Bool
        /// <summary>缺少 Entity／欄位回 false；原 bool 直接回傳，其他值沿用 caller CurrentCulture 的 Convert.ToBoolean，不改程序文化。</summary>
        bool GetBoolAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetBoolAttribute(ref Entity entity, string propertyName, bool value);

        // Int
        /// <summary>缺少 Entity／欄位回 0；原 int 直接回傳，其他值沿用 caller CurrentCulture 的 Convert.ToInt32，不改程序文化。</summary>
        int GetIntAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetIntAttribute(ref Entity entity, string propertyName, int value);

        // String
        /// <summary>缺少 Entity／欄位或明確 null 回空字串；其他值沿用 ToString，真正格式化故障直接上拋而非偽裝空資料。</summary>
        string GetStringAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetStringAttribute(ref Entity entity, string propertyName, string value);

        // DateTime
        /// <summary>缺值回 DateTime.MinValue，原 DateTime 的 Kind／日期不變；其他值沿用 caller CurrentCulture，不做時區轉換。</summary>
        DateTime GetDateTimeAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetDateTimeAttribute(ref Entity entity, string propertyName, DateTime value);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetDateTimeAttributeToNull(ref Entity entity, string propertyName);

        // Money
        /// <summary>缺值回新 Money(0)，原 Money 是 caller 實體圖的一部分並原樣借讀；其他值依 caller CurrentCulture 轉 decimal。</summary>
        Money GetMoneyAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetMoneyAttribute(ref Entity entity, string propertyName, Money value);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetMoneyAttributeToNull(ref Entity entity, string propertyName);

        // Lookup
        /// <summary>缺少／清空或無法解析的文字回 Guid.Empty；EntityReference、Guid 與可解析 GUID 依原順序處理，顯示名不是身份。</summary>
        Guid GetLookupAttribute(Entity entity, string propertyName);
        /// <summary>讀取既有參照顯示名稱，缺少時為空；名稱不代表身份、授權或唯一性。</summary>
        string GetLookupDisplayName(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetLookupAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetLookupToNull(ref Entity entity, string propertyName);

        // OptionSet
        /// <summary>缺值回舊 sentinel -9999；原 OptionSetValue 回 Value，其他值沿用 caller CurrentCulture 轉 int，不把 sentinel 當正式 ID。</summary>
        int GetOptionSetAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetOptionSetAttribute(ref Entity entity, string propertyName, int value);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetOptionSetAttributeNull(ref Entity entity, string propertyName);

        // Float
        /// <summary>缺值回舊 sentinel -9999f；原 float 直接回傳，其他值沿用 caller CurrentCulture 轉 Single，不改程序文化。</summary>
        float GetFloatAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetFloatAttribute(ref Entity entity, string propertyName, float value);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetFloatAttributeToNull(Entity entity, string propertyName);

        // Double
        /// <summary>缺值回舊 sentinel -9999.0；保留 double／decimal／int 的原轉換優先序，其他值沿用 caller CurrentCulture。</summary>
        double GetDoubleAttribute(Entity entity, string propertyName);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetDoubleAttribute(ref Entity entity, string propertyName, double value);
        /// <summary>只在 caller 獨占的 Entity 寫回或明確清空對應 SDK 欄位；保留 add／replace 語義，不執行 SDK I/O 或發布共享物件。</summary>
        void SetDoubleAttributeToNull(Entity entity, string propertyName);
    }
}
