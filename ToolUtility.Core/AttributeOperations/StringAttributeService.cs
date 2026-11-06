/*
 * 檔案整體說明
 *
 * 目的與定位：StringAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 StringAttributeService。
 * - 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
 * - 缺少 Entity／欄位或明確 null 回空字串；其他值沿用 ToString，真正格式化故障直接上拋而非偽裝空資料。 不保存結果，真正故障交回 host。
 * - 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
 * 主要操作入口包含 GetAttribute、SetAttribute。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/AttributeOperations/AttributeServiceComposite.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using System.Globalization;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// StringAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
    /// </summary>
    public class StringAttributeService : IAttributeValueReader<string>, IAttributeValueWriter<string>
    {

        /// <summary>
        /// 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
        /// </summary>
        public StringAttributeService(object logger)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
        }

        /// <summary>
        /// 缺少 Entity／欄位或明確 null 回空字串；其他值沿用 ToString，真正格式化故障直接上拋而非偽裝空資料。 不保存結果，真正故障交回 host。
        /// </summary>
        public string GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return string.Empty;
            if (string.IsNullOrEmpty(propertyName)) return string.Empty;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value == null) return string.Empty;
                return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetAttribute(ref Entity entity, string propertyName, string value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = value;
            }
            else
            {
                entity.Attributes.Add(propertyName, value);
            }
        }


    }
}
