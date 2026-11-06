/*
 * 檔案整體說明
 *
 * 目的與定位：LookupAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 LookupAttributeService。
 * - 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
 * - 缺少／清空或無法解析的文字回 Guid.Empty；EntityReference、Guid 與可解析 GUID 依原順序處理，顯示名不是身份。 不保存結果，真正故障交回 host。
 * - 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
 * - 只回傳 caller 已持有的 Lookup 顯示名，缺少時為空；顯示文字不是資料庫身份、授權或去重依據。
 * 主要操作入口包含 GetAttribute、SetAttribute、GetDisplayName、SetToNull。
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
    /// LookupAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
    /// </summary>
    public class LookupAttributeService : ILookupAttributeService
    {

        /// <summary>
        /// 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
        /// </summary>
        public LookupAttributeService(object logger)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
        }

        /// <summary>
        /// 缺少／清空或無法解析的文字回 Guid.Empty；EntityReference、Guid 與可解析 GUID 依原順序處理，顯示名不是身份。 不保存結果，真正故障交回 host。
        /// </summary>
        public Guid GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return Guid.Empty;
            if (string.IsNullOrEmpty(propertyName)) return Guid.Empty;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is EntityReference er) return er.Id;
                                    // attempt to parse if value is Guid or string
                    if (value is Guid g) return g;
                    if (value == null) return Guid.Empty;
                    var s = value.ToString();
                    if (Guid.TryParse(s, out var parsed)) return parsed;
                
            }

            return Guid.Empty;
        }

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));
            if (string.IsNullOrEmpty(lookupEntityName)) throw new ArgumentNullException(nameof(lookupEntityName));

            var reference = new EntityReference(lookupEntityName, guidValue);

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = reference;
            }
            else
            {
                entity.Attributes.Add(propertyName, reference);
            }
        }

        /// <summary>
        /// 只回傳 caller 已持有的 Lookup 顯示名，缺少時為空；顯示文字不是資料庫身份、授權或去重依據。
        /// </summary>
        public string GetDisplayName(Entity entity, string propertyName)
        {
            if (entity == null) return string.Empty;
            if (string.IsNullOrEmpty(propertyName)) return string.Empty;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is EntityReference er) return er.Name ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (entity.Attributes.Contains(propertyName))
            {
                entity.Attributes[propertyName] = entityReference;
            }
            else
            {
                entity.Attributes.Add(propertyName, entityReference);
            }
        }

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetToNull(ref Entity entity, string propertyName)
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
