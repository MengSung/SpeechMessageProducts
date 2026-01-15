using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 屬性操作 (Partial Class 7/10)
    /// 包含：所有 Entity 屬性的 Get/Set 方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 布林屬性
        public bool GetEntityBoolAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityBoolAttribute(aEntity, PropertyName);
        public bool GetEntityBoolAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityBoolAttribute(aEntity, PropertyName);
        public void SetEntityBoolAttribute(ref Entity aEntity, string PropertyName, bool PropertyValue) => _facade.SetEntityBoolAttribute(ref aEntity, PropertyName, PropertyValue);
        public void SetEntityBoolAttributeToNull(ref Entity aEntity, string PropertyName) => aEntity[PropertyName] = null;
        #endregion

        #region 整數屬性
        public int GetEntityIntAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityIntAttribute(aEntity, PropertyName);
        public int GetEntityIntAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityIntAttribute(aEntity, PropertyName);
        public void SetEntityIntAttribute(ref Entity aEntity, string PropertyName, int PropertyValue) => aEntity[PropertyName] = PropertyValue;
        public void SetEntityIntAttributeToNull(ref Entity aEntity, string PropertyName) => aEntity[PropertyName] = null;
        #endregion

        #region 浮點屬性
        public float GetEntityFloatAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityFloatAttribute(aEntity, PropertyName);
        public float GetEntityFloatAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityFloatAttribute(aEntity, PropertyName);
        public void SetEntityFloatAttribute(ref Entity aEntity, string PropertyName, float PropertyValue) => _facade.SetEntityFloatAttribute(ref aEntity, PropertyName, PropertyValue);
        public void SetEntityFloatAttribute(Entity aEntity, string PropertyName, float PropertyValue) { Entity temp = aEntity; _facade.SetEntityFloatAttribute(ref temp, PropertyName, PropertyValue); }
        public void SetEntityFloatAttributeToNull(Entity aEntity, string PropertyName) => _facade.SetEntityFloatAttributeToNull(aEntity, PropertyName);
        #endregion

        #region 金額屬性
        public Money GetEntityMoneyAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityMoneyAttribute(aEntity, PropertyName);
        public Money GetEntityMoneyAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityMoneyAttribute(aEntity, PropertyName);
        public void SetEntityMoneyAttribute(ref Entity aEntity, string PropertyName, Money PropertyValue) { if (PropertyValue.Value != -9999) aEntity[PropertyName] = PropertyValue; }
        public void SetEntityMoneyAttribute(Entity aEntity, string PropertyName, Money PropertyValue) => aEntity[PropertyName] = PropertyValue;
        public void SetEntityMoneyAttributeToNull(Entity aEntity, string PropertyName) { Entity temp = aEntity; _facade.SetEntityMoneyAttributeToNull(ref temp, PropertyName); }
        #endregion

        #region 小數點屬性
        public Double GetEntityDoubleAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityDoubleAttribute(aEntity, PropertyName);
        public Double GetEntityDoubleAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityDoubleAttribute(aEntity, PropertyName);
        public void SetEntityDoubleAttribute(ref Entity aEntity, string PropertyName, Double PropertyValue) => _facade.SetEntityDoubleAttribute(ref aEntity, PropertyName, PropertyValue);
        public void SetEntityDoubleAttribute(Entity aEntity, string PropertyName, Double PropertyValue) { Entity temp = aEntity; _facade.SetEntityDoubleAttribute(ref temp, PropertyName, PropertyValue); }
        public void SetEntityDoubleAttributeToNull(Entity aEntity, string PropertyName) => _facade.SetEntityDoubleAttributeToNull(aEntity, PropertyName);
        #endregion

        #region 時間屬性
        public DateTime GetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityDateTimeAttribute(aEntity, PropertyName);
        public DateTime GetEntityDateTimeAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityDateTimeAttribute(aEntity, PropertyName);
        public void SetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName, DateTime PropertyValue) => aEntity[PropertyName] = PropertyValue;
        public void SetEntityDateTimeAttributeToNull(ref Entity aEntity, string PropertyName) => _facade.SetEntityDateTimeAttributeToNull(ref aEntity, PropertyName);
        #endregion

        #region 文字屬性
        public String GetEntityStringAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityStringAttribute(aEntity, PropertyName);
        public String GetEntityStringAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityStringAttribute(aEntity, PropertyName);
        public void SetEntityStringAttribute(ref Entity aEntity, string PropertyName, String PropertyValue) => aEntity[PropertyName] = PropertyValue;
        public void SetEntityStringAttribute(Entity aEntity, string PropertyName, String PropertyValue) => aEntity[PropertyName] = PropertyValue;
        #endregion

        #region 選項屬性
        public int GetOptionSetAttribute(ref Entity aEntity, string PropertyName) => _facade.GetOptionSetAttribute(aEntity, PropertyName);
        public int GetOptionSetAttribute(Entity aEntity, string PropertyName) => _facade.GetOptionSetAttribute(aEntity, PropertyName);
        public void SetOptionSetAttribute(ref Entity aEntity, string PropertyName, int PropertyValue) => _facade.SetOptionSetAttribute(ref aEntity, PropertyName, PropertyValue);
        public void SetOptionSetAttribute(Entity aEntity, string PropertyName, int PropertyValue) { Entity temp = aEntity; _facade.SetOptionSetAttribute(ref temp, PropertyName, PropertyValue); }
        public void SetOptionSetAttributeNull(ref Entity aEntity, string PropertyName) => _facade.SetOptionSetAttributeNull(ref aEntity, PropertyName);
        public void SetOptionSetAttributeNull(Entity aEntity, string PropertyName) { Entity temp = aEntity; _facade.SetOptionSetAttributeNull(ref temp, PropertyName); }
        #endregion

        #region LookUp 屬性
        public Guid GetEntityLookupAttribute(ref Entity aEntity, string PropertyName) => _facade.GetEntityLookupAttribute(aEntity, PropertyName);
        public Guid GetEntityLookupAttribute(Entity aEntity, string PropertyName) => _facade.GetEntityLookupAttribute(aEntity, PropertyName);
        public String GetEntityLookupDisplayName(ref Entity aEntity, string PropertyName) => _facade.GetEntityLookupDisplayName(aEntity, PropertyName);
        public String GetEntityLookupDisplayName(Entity aEntity, string PropertyName) => _facade.GetEntityLookupDisplayName(aEntity, PropertyName);
        
        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            if (GuidValue != null && GuidValue != Guid.Empty)
                aEntity[PropertyName] = new EntityReference(LookupEntityName, GuidValue);
        }
        
        public void SetEntityLookUpAttribute(Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            if (GuidValue != null && GuidValue != Guid.Empty)
                aEntity[PropertyName] = new EntityReference(LookupEntityName, GuidValue);
        }
        
        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, ref EntityReference aEntityReference) 
            => _facade.SetEntityLookUpAttribute(ref aEntity, PropertyName, ref aEntityReference);
        
        public void SetEntityLookUpToNull(ref Entity aEntity, string PropertyName) 
            => _facade.SetEntityLookUpToNull(ref aEntity, PropertyName);
        #endregion

        #region 負責人屬性
        public Guid GetOwnerId(Entity aEntity) => _facade.GetOwnerId(aEntity);
        public void AssignOwner(String EntityName, Entity aEntity, Guid OwnerId) => _facade.AssignOwner(EntityName, aEntity, OwnerId);
        public String GetOwnerName(Entity aEntity) => _facade.GetOwnerName(aEntity);
        #endregion
    }
}
