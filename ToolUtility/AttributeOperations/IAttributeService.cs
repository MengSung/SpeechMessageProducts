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

        // Money
        Money GetMoneyAttribute(Entity entity, string propertyName);
        void SetMoneyAttribute(ref Entity entity, string propertyName, Money value);

        // Lookup
        Guid GetLookupAttribute(Entity entity, string propertyName);
        void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue);
    }
}
