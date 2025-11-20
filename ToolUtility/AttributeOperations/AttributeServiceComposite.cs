using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttributeOperations
{
    public class AttributeServiceComposite : IAttributeService
    {
        private readonly BoolAttributeService _boolService;
        private readonly IntAttributeService _intService;
        private readonly StringAttributeService _stringService;
        private readonly DateTimeAttributeService _dateTimeService;
        private readonly MoneyAttributeService _moneyService;
        private readonly LookupAttributeService _lookupService;

        public AttributeServiceComposite(object logger)
        {
            _boolService = new BoolAttributeService(logger);
            _intService = new IntAttributeService(logger);
            _stringService = new StringAttributeService(logger);
            _dateTimeService = new DateTimeAttributeService(logger);
            _moneyService = new MoneyAttributeService(logger);
            _lookupService = new LookupAttributeService(logger);
        }

        public bool GetBoolAttribute(Entity entity, string propertyName)
            => _boolService.GetAttribute(entity, propertyName);

        public void SetBoolAttribute(ref Entity entity, string propertyName, bool value)
            => _boolService.SetAttribute(ref entity, propertyName, value);

        public int GetIntAttribute(Entity entity, string propertyName)
            => _intService.GetAttribute(entity, propertyName);

        public void SetIntAttribute(ref Entity entity, string propertyName, int value)
            => _intService.SetAttribute(ref entity, propertyName, value);

        public string GetStringAttribute(Entity entity, string propertyName)
            => _stringService.GetAttribute(entity, propertyName);

        public void SetStringAttribute(ref Entity entity, string propertyName, string value)
            => _stringService.SetAttribute(ref entity, propertyName, value);

        public DateTime GetDateTimeAttribute(Entity entity, string propertyName)
            => _dateTimeService.GetAttribute(entity, propertyName);

        public void SetDateTimeAttribute(ref Entity entity, string propertyName, DateTime value)
            => _dateTimeService.SetAttribute(ref entity, propertyName, value);

        public Money GetMoneyAttribute(Entity entity, string propertyName)
            => _moneyService.GetAttribute(entity, propertyName);

        public void SetMoneyAttribute(ref Entity entity, string propertyName, Money value)
            => _moneyService.SetAttribute(ref entity, propertyName, value);

        public Guid GetLookupAttribute(Entity entity, string propertyName)
            => _lookupService.GetAttribute(entity, propertyName);

        public void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
            => _lookupService.SetAttribute(ref entity, propertyName, lookupEntityName, guidValue);
    }
}
