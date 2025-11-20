using System;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.LineMessaging
{
    public class LineMessageService : ILineMessageService
    {
        private readonly object _logger;
        private readonly IEntityCrudService _crudService;

        public LineMessageService(object logger, IEntityCrudService crudService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _crudService = crudService ?? throw new ArgumentNullException(nameof(crudService));
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

            _crudService.CreateEntity(entity);
        }
    }
}
