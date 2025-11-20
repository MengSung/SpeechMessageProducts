using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    public interface IAttachmentService
    {
        EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId);

        void UploadAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId);
    }
}
