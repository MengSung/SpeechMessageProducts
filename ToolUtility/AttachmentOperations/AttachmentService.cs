using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    public class AttachmentService : IAttachmentService
    {
        private readonly object _logger;
        private readonly ICrmClient _crmClient;

        public AttachmentService(object logger, ICrmClient crmClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _crmClient = crmClient ?? throw new ArgumentNullException(nameof(crmClient));
        }

        public EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId)
        {
            // Simplified: return empty collection; real impl would query annotation by objectid
            return new EntityCollection();
        }

        public void UploadAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
        {
            var annotation = new Entity("annotation")
            {
                ["subject"] = subject,
                ["notetext"] = noteText,
                ["filename"] = fileName,
                ["mimetype"] = mimeType,
                ["documentbody"] = Convert.ToBase64String(documentBody),
                ["objectid"] = new EntityReference(entityName, toBeAttachedEntityId)
            };

            _crmClient.Create(annotation);
        }
    }
}
