#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using System.Globalization;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// DateTimeAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
    /// </summary>
    public class DateTimeAttributeService : IAttributeValueReader<DateTime>, IAttributeValueWriter<DateTime>, IRefAttributeClearer
    {

        /// <summary>
        /// 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
        /// </summary>
        public DateTimeAttributeService(object logger)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
        }

        /// <summary>
        /// 缺值回 DateTime.MinValue，原 DateTime 的 Kind／日期不變；其他值沿用 caller CurrentCulture，不做時區轉換。 不保存結果，真正故障交回 host。
        /// </summary>
        public DateTime GetAttribute(Entity entity, string propertyName)
        {
            if (entity == null) return DateTime.MinValue;
            if (string.IsNullOrEmpty(propertyName)) return DateTime.MinValue;

            if (entity.Attributes.Contains(propertyName))
            {
                var value = entity.Attributes[propertyName];
                if (value is DateTime dt) return dt;
                                    return Convert.ToDateTime(value, CultureInfo.CurrentCulture);
                
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetAttribute(ref Entity entity, string propertyName, DateTime value)
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

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetAttributeToNull(ref Entity entity, string propertyName)
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
