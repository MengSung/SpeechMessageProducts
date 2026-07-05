// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AttributeOperations/LookupAttributeService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class LookupAttributeService
// 主要成員：GetAttribute、SetAttribute、GetDisplayName、SetToNull、SafeLogError
// 引用命名空間：System、Microsoft.Xrm.Sdk、System.Linq
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using System.Linq;

namespace ToolUtilityNameSpace.AttributeOperations
{
    public class LookupAttributeService
    {
        private readonly object _logger;

        public LookupAttributeService(object logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Guid GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return Guid.Empty;
            if (string.IsNullOrEmpty(propertyName)) return Guid.Empty;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is EntityReference er) return er.Id;
                try
                {
                    // attempt to parse if value is Guid or string
                    if (value is Guid g) return g;
                    var s = value.ToString();
                    if (Guid.TryParse(s, out var parsed)) return parsed;
                }
                catch (Exception ex)
                {
                    SafeLogError(ex, "GetAttribute lookup invalid cast for {0}", propertyName);
                    throw;
                }
            }

            return Guid.Empty;
        }

        public void SetAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
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

        public void SetAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
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

        public void SetToNull(ref Entity entity, string propertyName)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
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

        private void SafeLogError(Exception ex, string format, params object[] args)
        {
            try
            {
                if (_logger == null) return;
                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                if (logMethod != null)
                {
                    var genericMethod = logMethod.MakeGenericMethod(typeof(object));
                    var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                    object errorLevel = null;
                    if (logLevelType != null)
                    {
                        errorLevel = Enum.Parse(logLevelType, "Error");
                    }

                    var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                    object eventId = null;
                    if (eventIdType != null)
                    {
                        eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                    }

                    object state = string.Format(format, args);
                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;

                    var parameters = new object[] { errorLevel, eventId, state, ex, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // swallow
            }
        }
    }
}
