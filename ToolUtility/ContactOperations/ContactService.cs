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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);
            var result = _queryService.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        public string GetContactInfoByContactId(string contactId)
        {
            var e = RetrieveByContactId(contactId);
            return FormatContactInfo(e);
        }

        public Entity RetrieveByContactId(IOrganizationService externalService, string contactId, ref int count)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);
            var coll = externalService.RetrieveMultiple(query);
            count = coll?.Entities?.Count ?? 0;
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveByLineId(string lineId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineId, 0);
            var result = _queryService.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        public Entity RetrieveByLineIdForCollection(string lineId) => RetrieveByLineId(lineId);

        public EntityCollection RetrieveCollectionByName(string contactFullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(contactFullName, 0);
            return _queryService.RetrieveMultiple(query);
        }

        public string GetContactInfoByFullName(string fullName)
        {
            var e = RetrieveByFullName(fullName);
            return FormatContactInfo(e);
        }

        public Entity RetrieveByFullName(string fullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            var coll = _queryService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveByFullName(IOrganizationService externalService, string fullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            var coll = externalService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public string GetContactInfoByFullName(IOrganizationService externalService, string fullName)
        {
            var e = RetrieveByFullName(externalService, fullName);
            return FormatContactInfo(e);
        }

        public Entity RetrieveByAccountNumber(string accountNumber, string password)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_app_acount", "statecode");
            query.Values.AddRange(accountNumber, 0);
            var result = _queryService.RetrieveMultiple(query);
            if (result.Entities.Count > 0)
            {
                var entity = result.Entities[0];
                if (entity.Attributes.Contains("new_app_pass") && entity.GetAttributeValue<string>("new_app_pass") == password)
                {
                    return entity;
                }
            }
            return null;
        }

        public string AccountLogin(string accountNumber, string password)
        {
            var entity = RetrieveByAccountNumber(accountNumber, password);
            if (entity != null) return entity.Id.ToString();
            var exists = RetrieveAccountEntity(accountNumber);
            if (exists == null) return "帳號錯誤";
            if (!exists.Contains("new_app_pass")) return "系統沒有設定密碼";
            return "密碼錯誤";
        }

        public Entity RetrieveAccountEntity(string accountNumber)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_app_acount", "statecode");
            query.Values.AddRange(accountNumber, 0);
            var coll = _queryService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveCollectionByNationId(string nationId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_personal_id", "statecode");
            query.Values.AddRange(nationId, 0);
            return _queryService.RetrieveMultiple(query);
        }

        public Entity RetrieveByFullNameAndMobile(string fullName, string mobileNumber)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "mobilephone", "statecode");
            query.Values.AddRange(fullName, mobileNumber, 0);
            var coll = _queryService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveCollectionByFullName(string fullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            return _queryService.RetrieveMultiple(query);
        }

        private string FormatContactInfo(Entity e)
        {
            if (e == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            if (e.Contains("fullname")) sb.AppendLine("姓名:" + e["fullname"].ToString());
            if (e.Contains("build_customer_id")) sb.AppendLine("身分證字號:" + e["build_customer_id"].ToString());
            if (e.Contains("telephone1")) sb.AppendLine("電話號碼:" + e["telephone1"].ToString());
            if (e.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + e["emailaddress1"].ToString());
            return sb.ToString();
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
