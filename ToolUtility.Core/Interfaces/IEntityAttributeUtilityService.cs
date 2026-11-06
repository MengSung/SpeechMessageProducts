/*
 * 檔案整體說明
 *
 * 目的與定位：操作 caller 獨占 SDK Entity 的純屬性契約；不存取伺服器、不保存 Entity 或主機狀態。 呼叫端須完成授權並避免同一 Entity 並行寫入，服務不接管候選的發布或生命週期。
 *
 * 主要功能與流程：
 * 本檔宣告 IEntityAttributeUtilityService。
 * - 一般值與單層別名值轉文字，缺值及空欄位名稱回空字串；不改變既有格式文化。
 * - 就地移除指定欄位；缺欄位不變更其他資料，不替換 caller 的 Entity。
 * - 保留指定欄位並明確設 null，表達 SDK 清值；原本缺欄位時新增該欄。
 * 資料／選項欄位包含 IEntityAttributeUtilityService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 操作 caller 獨占 SDK Entity 的純屬性契約；不存取伺服器、不保存 Entity 或主機狀態。
    /// 呼叫端須完成授權並避免同一 Entity 並行寫入，服務不接管候選的發布或生命週期。
    /// </summary>
    public interface IEntityAttributeUtilityService
    {
        /// <summary>一般值與單層別名值轉文字，缺值及空欄位名稱回空字串；不改變既有格式文化。</summary>
        /// <param name="targetEntity">已授權且只屬目前操作的 Entity，不保存其參照。</param>
        /// <param name="attributeName">欄位 logical name；null 或空字串依相容契約回空文字。</param>
        /// <returns>原值文字或真正缺值的空字串。</returns>
        /// <exception cref="InvalidOperationException">讀取失敗，以 InnerException 保留真實型別供 host 通知。</exception>
        string GetAttributeValue(Entity targetEntity, string? attributeName);

        /// <summary>就地移除指定欄位；缺欄位不變更其他資料，不替換 caller 的 Entity。</summary>
        /// <param name="entity">操作獨占的可變候選；owner 須協調同一物件的並行寫入。</param>
        /// <param name="propertyName">非空且非全空白欄位名。</param>
        /// <exception cref="InvalidOperationException">輸入或操作失敗；原始故障位於 InnerException。</exception>
        void RemoveAttribute(ref Entity entity, string propertyName);

        /// <summary>保留指定欄位並明確設 null，表達 SDK 清值；原本缺欄位時新增該欄。</summary>
        /// <param name="entity">操作獨占的 Entity，方法不保留或替換其參照。</param>
        /// <param name="propertyName">非空且非全空白欄位名。</param>
        /// <exception cref="InvalidOperationException">輸入或操作失敗；host 須以真實例外走共用通知。</exception>
        void SetEntityAttributeToNull(ref Entity entity, string propertyName);
    }
}
