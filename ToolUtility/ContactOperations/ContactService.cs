using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.EntityOperations;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.ContactOperations
{
    public class ContactService : IContactService
    {
        private readonly object _logger;
        private readonly IEntityQueryService _queryService;

        public ContactService(object logger, IEntityQueryService queryService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        public Entity RetrieveByContactId(string contactId)
        {
            try
            {
                var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
                query.Attributes.AddRange("build_customer_id", "statecode");
                query.Values.AddRange(contactId, 0);

                var result = _queryService.RetrieveMultiple(query);
                return result.Entities.Count > 0 ? result.Entities[0] : null;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveByContactId failed for {0}", contactId);
                throw;
            }
        }

        public Entity RetrieveByLineId(string lineId)
        {
            try
            {
                var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
                query.Attributes.AddRange("new_lineid", "statecode");
                query.Values.AddRange(lineId, 0);

                var result = _queryService.RetrieveMultiple(query);
                return result.Entities.Count > 0 ? result.Entities[0] : null;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveByLineId failed for {0}", lineId);
                throw;
            }
        }

        public EntityCollection RetrieveCollectionByName(string contactFullName)
        {
            try
            {
                var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
                query.Attributes.AddRange("fullname", "statecode");
                query.Values.AddRange(contactFullName, 0);

                return _queryService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "RetrieveCollectionByName failed for {0}", contactFullName);
                throw;
            }
        }

        private void SafeLogError(Exception ex, string format, params object[] args)
        {
            try
            {
                if (_logger == null) return;

                var loggerType = _logger.GetType();
                var logMethod = loggerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Log" && m.GetParameters().Length == 5 && m.IsGenericMethod);

                if (logMethod != null)
                {
                    var genericMethod = logMethod.MakeGenericMethod(typeof(object));
                    var logLevelType = Type.GetType("Microsoft.Extensions.Logging.LogLevel, Microsoft.Extensions.Logging.Abstractions");
                    object errorLevel = null;
                    if (logLevelType != null)
                    {
                        errorLevel = Enum.Parse(logLevelType, "Error");
                    }

                    var eventIdType = Type.GetType("Microsoft.Extensions.Logging.EventId, Microsoft.Extensions.Logging.Abstractions");
                    object eventId = null;
                    if (eventIdType != null)
                    {
                        eventId = Activator.CreateInstance(eventIdType, 0, string.Empty);
                    }

                    object state = string.Format(format, args);
                    Func<object, Exception, string> formatter = (s, e) => s?.ToString() ?? string.Empty;

                    var parameters = new object[] { errorLevel, eventId, state, ex, formatter };
                    genericMethod.Invoke(_logger, parameters);
                }
            }
            catch
            {
                // swallow
            }
        }
    }
}
