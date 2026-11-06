/*
 * 檔案整體說明
 *
 * 目的與定位：靜態 typed 屬性操作，保留原缺值預設與型別規則；真正轉換故障直接交回 host，禁止 Trace 原值或吞例外。
 *
 * 主要功能與流程：
 * 本檔宣告 EntityAttributeHandler。
 * - 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
 * - 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
 * - 只回傳 caller 已持有的 Lookup 顯示名，缺少時為空；顯示文字不是資料庫身份、授權或去重依據。
 * - 只讀 caller 實體的 ownerid 正式參照；缺少或非參照回 Guid.Empty，不把顯示名稱轉成身份。
 * 主要操作入口包含 GetStringAttribute、SetStringAttribute、GetOptionSetAttribute、SetOptionSetAttribute、GetMoneyAttribute、
 * SetMoneyAttribute、GetLookupAttribute、GetLookupDisplayName；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Tests/DataOperations/AttributeConversionTests.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using Microsoft.Xrm.Sdk;
using System;


namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 靜態 typed 屬性操作，保留原缺值預設與型別規則；真正轉換故障直接交回 host，禁止 Trace 原值或吞例外。
    /// </summary>
    public static class EntityAttributeHandler
    {
        #region String 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static string GetStringAttribute(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName))
                return string.Empty;

            return entity[attributeName]?.ToString() ?? string.Empty;

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetStringAttribute(ref Entity entity, string attributeName, string value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            entity[attributeName] = value ?? string.Empty;

        }

        #endregion

        #region OptionSet 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static int GetOptionSetAttribute(Entity entity, string attributeName, int defaultValue = -1)
        {
            if (entity == null || !entity.Contains(attributeName))
                return defaultValue;

            if (entity[attributeName] is OptionSetValue optionSetValue)
                return optionSetValue.Value;

            return defaultValue;

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetOptionSetAttribute(ref Entity entity, string attributeName, int value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            entity[attributeName] = new OptionSetValue(value);

        }

        #endregion

        #region Money 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static Money GetMoneyAttribute(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName))
                return new Money(0);

            return entity[attributeName] as Money ?? new Money(0);

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetMoneyAttribute(ref Entity entity, string attributeName, Money value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            entity[attributeName] = value ?? new Money(0);

        }

        #endregion

        #region Lookup 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static Guid GetLookupAttribute(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName))
                return Guid.Empty;

            if (entity[attributeName] is EntityReference entityReference)
                return entityReference.Id;

            return Guid.Empty;

        }
        /// <summary>
        /// 只回傳 caller 已持有的 Lookup 顯示名，缺少時為空；顯示文字不是資料庫身份、授權或去重依據。
        /// </summary>
        public static string GetLookupDisplayName(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName))
                return string.Empty;

            if (entity[attributeName] is EntityReference entityReference)
                return entityReference.Name ?? string.Empty;

            return string.Empty;

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetLookupAttribute(ref Entity entity, string attributeName, string entityName, Guid entityId)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("An entity name is required.", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("A database identifier is required.", nameof(entityId));

            entity[attributeName] = new EntityReference(entityName, entityId);

        }

        #endregion

        #region DateTime 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static DateTime GetDateTimeAttribute(Entity entity, string attributeName)
        {
            if (entity == null || !entity.Contains(attributeName))
                return DateTime.MinValue;

            return entity.GetAttributeValue<DateTime>(attributeName);

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetDateTimeAttribute(ref Entity entity, string attributeName, DateTime value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            entity[attributeName] = value;

        }

        #endregion

        #region Boolean 屬性操作
        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public static bool GetBooleanAttribute(Entity entity, string attributeName, bool defaultValue = false)
        {
            if (entity == null || !entity.Contains(attributeName))
                return defaultValue;

            return entity.GetAttributeValue<bool>(attributeName);

        }
        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public static void SetBooleanAttribute(ref Entity entity, string attributeName, bool value)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            entity[attributeName] = value;

        }

        #endregion

        #region Owner 操作
        /// <summary>
        /// 只讀 caller 實體的 ownerid 正式參照；缺少或非參照回 Guid.Empty，不把顯示名稱轉成身份。
        /// </summary>
        public static Guid GetOwnerId(Entity entity)
        {
            if (entity == null || !entity.Contains("ownerid"))
                return Guid.Empty;

            if (entity["ownerid"] is EntityReference ownerRef)
                return ownerRef.Id;

            return Guid.Empty;

        }

        #endregion
    }
}
