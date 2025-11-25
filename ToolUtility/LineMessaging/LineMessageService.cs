using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.LineMessaging
{
    public class LineMessageService : ILineMessageService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public LineMessageService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ;
        }

        public void CreatePushMessage(string userId, string subject, string message)
        {
            // Simplified: create a Message entity in CRM
            var entity = new Microsoft.Xrm.Sdk.Entity("linemessage")
            {
                ["userid"] = userId,
                ["subject"] = subject,
                ["message"] = message
            };

            _organizationService.Create(entity);
        }
    }
}
