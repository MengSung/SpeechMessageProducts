using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttributeOperations
{
    public interface IAttributeService
    {
        // Bool
        bool GetBoolAttribute(Entity entity, string propertyName);
        void SetBoolAttribute(ref Entity entity, string propertyName, bool value);

        // Int
        int GetIntAttribute(Entity entity, string propertyName);
        void SetIntAttribute(ref Entity entity, string propertyName, int value);

        // String
        string GetStringAttribute(Entity entity, string propertyName);
        void SetStringAttribute(ref Entity entity, string propertyName, string value);

        // DateTime
        DateTime GetDateTimeAttribute(Entity entity, string propertyName);
        void SetDateTimeAttribute(ref Entity entity, string propertyName, DateTime value);
        void SetDateTimeAttributeToNull(ref Entity entity, string propertyName);

        // Money
        Money GetMoneyAttribute(Entity entity, string propertyName);
        void SetMoneyAttribute(ref Entity entity, string propertyName, Money value);
        void SetMoneyAttributeToNull(ref Entity entity, string propertyName);

        // Lookup
        Guid GetLookupAttribute(Entity entity, string propertyName);
        string GetLookupDisplayName(Entity entity, string propertyName);
        void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue);
        void SetLookupAttribute(ref Entity entity, string propertyName, ref EntityReference entityReference);
        void SetLookupToNull(ref Entity entity, string propertyName);

        // OptionSet
        int GetOptionSetAttribute(Entity entity, string propertyName);
        void SetOptionSetAttribute(ref Entity entity, string propertyName, int value);
        void SetOptionSetAttributeNull(ref Entity entity, string propertyName);

        // Float
        float GetFloatAttribute(Entity entity, string propertyName);
        void SetFloatAttribute(ref Entity entity, string propertyName, float value);
        void SetFloatAttributeToNull(Entity entity, string propertyName);

        // Double
        double GetDoubleAttribute(Entity entity, string propertyName);
        void SetDoubleAttribute(ref Entity entity, string propertyName, double value);
        void SetDoubleAttributeToNull(Entity entity, string propertyName);
    }
}
