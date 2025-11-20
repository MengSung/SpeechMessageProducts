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
        private readonly OptionSetAttributeService _optionSetService;
        private readonly FloatAttributeService _floatService;
        private readonly DoubleAttributeService _doubleService;

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

        public void SetDateTimeAttributeToNull(ref Entity entity, string propertyName)
            => _dateTimeService.SetAttributeToNull(ref entity, propertyName);

        public Money GetMoneyAttribute(Entity entity, string propertyName)
            => _moneyService.GetAttribute(entity, propertyName);

        public void SetMoneyAttribute(ref Entity entity, string propertyName, Money value)
            => _moneyService.SetAttribute(ref entity, propertyName, value);

        public void SetMoneyAttributeToNull(ref Entity entity, string propertyName)
            => _moneyService.SetAttributeToNull(ref entity, propertyName);

        public Guid GetLookupAttribute(Entity entity, string propertyName)
            => _lookupService.GetAttribute(entity, propertyName);

        public string GetLookupDisplayName(Entity entity, string propertyName)
            => _lookupService.GetDisplayName(entity, propertyName);

        public void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue)
            => _lookupService.SetAttribute(ref entity, propertyName, lookupEntityName, guidValue);

        public void SetLookupAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference)
            => _lookupService.SetAttribute(ref entity, propertyName, ref entityReference);

        public void SetLookupToNull(ref Entity entity, string propertyName)
            => _lookupService.SetToNull(ref entity, propertyName);

        public int GetOptionSetAttribute(Entity entity, string propertyName)
            => _optionSetService.GetAttribute(entity, propertyName);

        public void SetOptionSetAttribute(ref Entity entity, string propertyName, int value)
            => _optionSetService.SetAttribute(ref entity, propertyName, value);

        public void SetOptionSetAttributeNull(ref Entity entity, string propertyName)
            => _optionSetService.SetAttributeNull(ref entity, propertyName);

        public float GetFloatAttribute(Entity entity, string propertyName)
            => _floatService.GetAttribute(entity, propertyName);

        public void SetFloatAttribute(ref Entity entity, string propertyName, float value)
            => _floatService.SetAttribute(ref entity, propertyName, value);

        public void SetFloatAttributeToNull(Entity entity, string propertyName)
            => _floatService.SetAttributeToNull(entity, propertyName);

        public double GetDoubleAttribute(Entity entity, string propertyName)
            => _doubleService.GetAttribute(entity, propertyName);

        public void SetDoubleAttribute(ref Entity entity, string propertyName, double value)
            => _doubleService.SetAttribute(ref entity, propertyName, value);

        public void SetDoubleAttributeToNull(Entity entity, string propertyName)
            => _doubleService.SetAttributeToNull(entity, propertyName);
    }
}
