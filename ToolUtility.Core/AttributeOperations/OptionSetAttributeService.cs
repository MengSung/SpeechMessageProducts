/*
 * 檔案整體說明
 *
 * 目的與定位：OptionSetAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 OptionSetAttributeService。
 * - 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
 * - 缺值回舊 sentinel -9999；原 OptionSetValue 回 Value，其他值沿用 caller CurrentCulture 轉 int，不把 sentinel 當正式 ID。 不保存結果，
 * 真正故障交回 host。
 * - 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
 * - 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
 * 主要操作入口包含 GetAttribute、SetAttribute、SetAttributeNull。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Tests/DataOperations/AttributeConversionTests.cs、
 * ToolUtility.Core/AttributeOperations/AttributeServiceComposite.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using System.Globalization;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// OptionSetAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
    /// </summary>
    public class OptionSetAttributeService : IOptionSetAttributeService
    {

        /// <summary>
        /// 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
        /// </summary>
        public OptionSetAttributeService(object logger)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
        }

        /// <summary>
        /// 缺值回舊 sentinel -9999；原 OptionSetValue 回 Value，其他值沿用 caller CurrentCulture 轉 int，不把 sentinel 當正式 ID。 不保存結果，真正故障交回 host。
        /// </summary>
        public int GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return -9999;
            if (string.IsNullOrEmpty(propertyName)) return -9999;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is OptionSetValue osv) return osv.Value;
                                    return Convert.ToInt32(value, CultureInfo.CurrentCulture);
                
            }

            return -9999;
        }

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetAttribute(ref Entity entity, string propertyName, int value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            var osv = new OptionSetValue(value);
            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = osv;
            }
            else
            {
                entity.Attributes.Add(propertyName, osv);
            }
        }

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetAttributeNull(ref Entity entity, string propertyName)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = null;
            }
            else
            {
                entity.Attributes.Add(propertyName, null);
            }
        }


    }
}
