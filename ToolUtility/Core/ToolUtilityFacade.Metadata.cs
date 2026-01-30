using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace.Core
{
    public partial class ToolUtilityFacade
    {
        /// <summary>
        /// Retrieve attribute logical names for an entity from CRM metadata
        /// </summary>
        public HashSet<string> GetEntityAttributeNames(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var attrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName)) attrs.Add(a.LogicalName);
                }
                return attrs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeNames failed for '{entityLogicalName}': {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Retrieve attribute logical names and their attribute type strings for an entity from CRM metadata
        /// </summary>
        public Dictionary<string, string> GetEntityAttributeTypes(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName))
                    {
                        var typeName = a.AttributeType?.ToString() ?? a.GetType().Name;
                        dict[a.LogicalName] = typeName;
                    }
                }
                return dict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeTypes failed for '{entityLogicalName}': {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Retrieve AttributeMetadata map for an entity from CRM metadata
        /// </summary>
        public Dictionary<string, AttributeMetadata> GetEntityAttributeMetadata(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var dict = new Dictionary<string, AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName))
                    {
                        dict[a.LogicalName] = a;
                    }
                }
                return dict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeMetadata failed for '{entityLogicalName}': {ex.Message}");
                return new Dictionary<string, AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
