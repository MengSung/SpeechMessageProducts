using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 實體屬性工具服務介面
    /// 處理實體屬性的通用操作
    /// </summary>
    public interface IEntityAttributeUtilityService
    {
        /// <summary>
        /// 取得屬性值（支援 AliasedValue）
        /// </summary>
        string GetAttributeValue(Entity targetEntity, string attributeName);

        /// <summary>
        /// 移除實體的指定屬性
        /// </summary>
        void RemoveAttribute(ref Entity entity, string propertyName);

        /// <summary>
        /// 將實體的指定屬性設為 null
        /// </summary>
        void SetEntityAttributeToNull(ref Entity entity, string propertyName);
    }
}
