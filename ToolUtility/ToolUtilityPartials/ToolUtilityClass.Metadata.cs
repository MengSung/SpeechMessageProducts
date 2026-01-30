using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace
{
    public partial class ToolUtilityClass
    {
        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeNames
        /// </summary>
        public HashSet<string> GetEntityAttributeNames(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeNames(entityLogicalName) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeTypes
        /// </summary>
        public Dictionary<string, string> GetEntityAttributeTypes(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeTypes(entityLogicalName) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeMetadata
        /// </summary>
        public Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata> GetEntityAttributeMetadata(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeMetadata(entityLogicalName) ?? new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
