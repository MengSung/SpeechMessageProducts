// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/EntityOperations/EntityAttributeHandler.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class EntityAttributeHandler
// 主要成員：GetStringAttribute、SetStringAttribute、GetOptionSetAttribute、SetOptionSetAttribute、GetMoneyAttribute、SetMoneyAttribute、GetLookupAttribute、GetLookupDisplayName、SetLookupAttribute、GetDateTimeAttribute
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Diagnostics;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// Entity 屬性處理器
    /// 專責處理 Entity 屬性的讀取與設定，遵循 Single Responsibility Principle
    /// </summary>
    public static class EntityAttributeHandler
    {
        #region String 屬性操作

        /// <summary>
        /// 取得字串屬性值
        /// </summary>
        public static string GetStringAttribute(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return string.Empty;

                return entity[attributeName]?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetStringAttribute failed for {attributeName}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 設定字串屬性值
        /// </summary>
        public static void SetStringAttribute(ref Entity entity, string attributeName, string value)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                entity[attributeName] = value ?? string.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetStringAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region OptionSet 屬性操作

        /// <summary>
        /// 取得 OptionSet 屬性值
        /// </summary>
        public static int GetOptionSetAttribute(Entity entity, string attributeName, int defaultValue = -1)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return defaultValue;

                if (entity[attributeName] is OptionSetValue optionSetValue)
                    return optionSetValue.Value;

                return defaultValue;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetOptionSetAttribute failed for {attributeName}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// 設定 OptionSet 屬性值
        /// </summary>
        public static void SetOptionSetAttribute(ref Entity entity, string attributeName, int value)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                entity[attributeName] = new OptionSetValue(value);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetOptionSetAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Money 屬性操作

        /// <summary>
        /// 取得 Money 屬性值
        /// </summary>
        public static Money GetMoneyAttribute(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return new Money(0);

                return entity[attributeName] as Money ?? new Money(0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetMoneyAttribute failed for {attributeName}: {ex.Message}");
                return new Money(0);
            }
        }

        /// <summary>
        /// 設定 Money 屬性值
        /// </summary>
        public static void SetMoneyAttribute(ref Entity entity, string attributeName, Money value)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                entity[attributeName] = value ?? new Money(0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetMoneyAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Lookup 屬性操作

        /// <summary>
        /// 取得 Lookup 屬性的 Guid
        /// </summary>
        public static Guid GetLookupAttribute(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return Guid.Empty;

                if (entity[attributeName] is EntityReference entityReference)
                    return entityReference.Id;

                return Guid.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetLookupAttribute failed for {attributeName}: {ex.Message}");
                return Guid.Empty;
            }
        }

        /// <summary>
        /// 取得 Lookup 屬性的顯示名稱
        /// </summary>
        public static string GetLookupDisplayName(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return string.Empty;

                if (entity[attributeName] is EntityReference entityReference)
                    return entityReference.Name ?? string.Empty;

                return string.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetLookupDisplayName failed for {attributeName}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 設定 Lookup 屬性
        /// </summary>
        public static void SetLookupAttribute(ref Entity entity, string attributeName, string entityName, Guid entityId)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (string.IsNullOrWhiteSpace(entityName))
                    throw new ArgumentException("Entity name 不可為空", nameof(entityName));

                if (entityId == Guid.Empty)
                    throw new ArgumentException("Entity ID 不可為空", nameof(entityId));

                entity[attributeName] = new EntityReference(entityName, entityId);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetLookupAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region DateTime 屬性操作

        /// <summary>
        /// 取得 DateTime 屬性值
        /// </summary>
        public static DateTime GetDateTimeAttribute(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return DateTime.MinValue;

                return entity.GetAttributeValue<DateTime>(attributeName);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetDateTimeAttribute failed for {attributeName}: {ex.Message}");
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 設定 DateTime 屬性值
        /// </summary>
        public static void SetDateTimeAttribute(ref Entity entity, string attributeName, DateTime value)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                entity[attributeName] = value;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetDateTimeAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Boolean 屬性操作

        /// <summary>
        /// 取得 Boolean 屬性值
        /// </summary>
        public static bool GetBooleanAttribute(Entity entity, string attributeName, bool defaultValue = false)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                    return defaultValue;

                return entity.GetAttributeValue<bool>(attributeName);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetBooleanAttribute failed for {attributeName}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// 設定 Boolean 屬性值
        /// </summary>
        public static void SetBooleanAttribute(ref Entity entity, string attributeName, bool value)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                entity[attributeName] = value;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] SetBooleanAttribute failed for {attributeName}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Owner 操作

        /// <summary>
        /// 取得擁有者 ID
        /// </summary>
        public static Guid GetOwnerId(Entity entity)
        {
            try
            {
                if (entity == null || !entity.Contains("ownerid"))
                    return Guid.Empty;

                if (entity["ownerid"] is EntityReference ownerRef)
                    return ownerRef.Id;

                return Guid.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityAttributeHandler] GetOwnerId failed: {ex.Message}");
                return Guid.Empty;
            }
        }

        #endregion
    }
}
