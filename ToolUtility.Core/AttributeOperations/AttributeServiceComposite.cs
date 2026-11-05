#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// AttributeServiceComposite的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、Entity、身分或跨請求狀態。
    /// </summary>
    public class AttributeServiceComposite : IAttributeService
    {
        private readonly BoolAttributeService _boolService;
        private readonly IntAttributeService _intService;
        private readonly StringAttributeService _stringService;
        private readonly DateTimeAttributeService _dateTimeService;
        private readonly MoneyAttributeService _moneyService;
        private readonly LookupAttributeService _lookupService;
        private readonly OptionSetAttributeService _optionSetService;
        private readonly FloatAttributeService _floatService;
        private readonly DoubleAttributeService _doubleService;

        /// <summary>
        /// 保留原 logger 建構契約並拒絕 null；不使用、反射、保存或處置此物件，避免延長 request／憑證生命週期。
        /// </summary>
        public AttributeServiceComposite(object logger)
        {
            _boolService = new BoolAttributeService(logger);
            _intService = new IntAttributeService(logger);
            _stringService = new StringAttributeService(logger);
            _dateTimeService = new DateTimeAttributeService(logger);
            _moneyService = new MoneyAttributeService(logger);
            _lookupService = new LookupAttributeService(logger);
            _optionSetService = new OptionSetAttributeService(logger);
            _floatService = new FloatAttributeService(logger);
            _doubleService = new DoubleAttributeService(logger);
        }

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public bool GetBoolAttribute(Entity entity, string propertyName)
            => _boolService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetBoolAttribute(ref Entity entity, string propertyName, bool value)
            => _boolService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public int GetIntAttribute(Entity entity, string propertyName)
            => _intService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetIntAttribute(ref Entity entity, string propertyName, int value)
            => _intService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public string GetStringAttribute(Entity entity, string propertyName)
            => _stringService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetStringAttribute(ref Entity entity, string propertyName, string value)
            => _stringService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public DateTime GetDateTimeAttribute(Entity entity, string propertyName)
            => _dateTimeService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetDateTimeAttribute(ref Entity entity, string propertyName, DateTime value)
            => _dateTimeService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetDateTimeAttributeToNull(ref Entity entity, string propertyName)
            => _dateTimeService.SetAttributeToNull(ref entity, propertyName);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public Money GetMoneyAttribute(Entity entity, string propertyName)
            => _moneyService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetMoneyAttribute(ref Entity entity, string propertyName, Money value)
            => _moneyService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetMoneyAttributeToNull(ref Entity entity, string propertyName)
            => _moneyService.SetAttributeToNull(ref entity, propertyName);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public Guid GetLookupAttribute(Entity entity, string propertyName)
            => _lookupService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只回傳 caller 已持有的 Lookup 顯示名，缺少時為空；顯示文字不是資料庫身份、授權或去重依據。
        /// </summary>
        public string GetLookupDisplayName(Entity entity, string propertyName)
            => _lookupService.GetDisplayName(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
            => _lookupService.SetAttribute(ref entity, propertyName, lookupEntityName, guidValue);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetLookupAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
            => _lookupService.SetAttribute(ref entity, propertyName, ref entityReference);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetLookupToNull(ref Entity entity, string propertyName)
            => _lookupService.SetToNull(ref entity, propertyName);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public int GetOptionSetAttribute(Entity entity, string propertyName)
            => _optionSetService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetOptionSetAttribute(ref Entity entity, string propertyName, int value)
            => _optionSetService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetOptionSetAttributeNull(ref Entity entity, string propertyName)
            => _optionSetService.SetAttributeNull(ref entity, propertyName);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public float GetFloatAttribute(Entity entity, string propertyName)
            => _floatService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetFloatAttribute(ref Entity entity, string propertyName, float value)
            => _floatService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetFloatAttributeToNull(Entity entity, string propertyName)
            => _floatService.SetAttributeToNull(entity, propertyName);

        /// <summary>
        /// 在此次 caller 的 Entity 讀取對應型別，保留原缺值預設；不保存結果。真正轉換失敗保留原例外交最終 host 共用告警。
        /// </summary>
        public double GetDoubleAttribute(Entity entity, string propertyName)
            => _doubleService.GetAttribute(entity, propertyName);

        /// <summary>
        /// 只變更 caller 獨占的 Entity 欄位，保留原 add／replace 及 SDK 型別包裝；不發布共享快照或執行 I/O，無效參數不應被吞掉。
        /// </summary>
        public void SetDoubleAttribute(ref Entity entity, string propertyName, double value)
            => _doubleService.SetAttribute(ref entity, propertyName, value);

        /// <summary>
        /// 在 caller 獨占的 Entity 明確寫入 null，保留欄位以供 SDK 清空；無 SDK I/O、不改變原 Entity 所有權。
        /// </summary>
        public void SetDoubleAttributeToNull(Entity entity, string propertyName)
            => _doubleService.SetAttributeToNull(entity, propertyName);
    }
}
