using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// Backward compatibility extensions for legacy code expecting ExecuteRetrieveMultiple.
    /// </summary>
    public static class OrganizationServiceExtensions
    {
        /// <summary>
        /// Legacy wrapper: executes a RetrieveMultipleRequest and returns the EntityCollection.
        /// </summary>
        public static EntityCollection ExecuteRetrieveMultiple(this IOrganizationService service, RetrieveMultipleRequest request)
        {
            if (service == null || request == null) return new EntityCollection();
            var resp = (RetrieveMultipleResponse)service.Execute(request);
            return resp?.EntityCollection ?? new EntityCollection();
        }
    }
}
