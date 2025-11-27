using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.EntityOperations;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Extensions;

namespace ToolUtilityNameSpace.ContactOperations
{
    public class ContactService : IContactService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public ContactService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        public Entity RetrieveByContactId(string contactId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);
            var result = _organizationService.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        public string GetContactInfoByContactId(string contactId)
        {
            var e = RetrieveByContactId(contactId);
            return FormatContactInfo(e);
        }

        public Entity RetrieveByContactId(IOrganizationService externalService, string contactId, ref int count)
        {
            if (externalService == null) { count = 0; return null; }
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
            var result = _organizationService.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        public Entity RetrieveByLineIdForCollection(string lineId) => RetrieveByLineId(lineId);

        public EntityCollection RetrieveCollectionByLineId(string lineId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_lineid", "statecode");
            query.Values.AddRange(lineId, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveCollectionByName(string contactFullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(contactFullName, 0);
            return _organizationService.RetrieveMultiple(query);
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
            var coll = _organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveByFullName(IOrganizationService externalService, string fullName)
        {
            if (externalService == null) return null;
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
            var result = _organizationService.RetrieveMultiple(query);
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
            var coll = _organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveCollectionByNationId(string nationId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("new_personal_id", "statecode");
            query.Values.AddRange(nationId, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        public Entity RetrieveByFullNameAndMobile(string fullName, string mobileNumber)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "mobilephone", "statecode");
            query.Values.AddRange(fullName, mobileNumber, 0);
            var coll = _organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public EntityCollection RetrieveCollectionByFullName(string fullName)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 使用 FetchXML 查詢奉獻者連絡人
        /// 支援多個條件組合查詢(OR邏輯): 奉獻編號、姓名、住家電話、手機、身分證字號、帳戶後六碼
        /// </summary>
        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            try
            {
                // 過濾無效的查詢條件
                bool hasDedicationNumber = !string.IsNullOrWhiteSpace(dedicationNumber) && !dedicationNumber.StartsWith("未填");
                bool hasContactName = !string.IsNullOrWhiteSpace(contactName) && !contactName.StartsWith("未填");
                bool hasHomePhone = !string.IsNullOrWhiteSpace(homePhone) && !homePhone.StartsWith("未填");
                bool hasMobile = !string.IsNullOrWhiteSpace(mobile) && !mobile.StartsWith("未填");
                bool hasNationId = !string.IsNullOrWhiteSpace(nationId) && !nationId.StartsWith("未填");
                bool hasLastSixDigit = !string.IsNullOrWhiteSpace(lastSixDigit) && !lastSixDigit.StartsWith("未填");

                // 如果沒有任何有效的查詢條件,返回空集合
                if (!hasDedicationNumber && !hasContactName && !hasHomePhone && !hasMobile && !hasNationId && !hasLastSixDigit)
                {
                    return new EntityCollection();
                }

                // 構建條件子句
                var conditions = new System.Text.StringBuilder();
                
                if (hasDedicationNumber)
                    conditions.AppendLine($"                    <condition attribute='pager' operator='eq' value='{System.Security.SecurityElement.Escape(dedicationNumber)}' />");
                
                if (hasContactName)
                    conditions.AppendLine($"                    <condition attribute='fullname' operator='like' value='%{System.Security.SecurityElement.Escape(contactName)}%' />");
                
                if (hasHomePhone)
                    conditions.AppendLine($"                    <condition attribute='telephone2' operator='like' value='%{System.Security.SecurityElement.Escape(homePhone)}%' />");
                
                if (hasMobile)
                    conditions.AppendLine($"                    <condition attribute='mobilephone' operator='like' value='%{System.Security.SecurityElement.Escape(mobile)}%' />");
                
                if (hasNationId)
                    conditions.AppendLine($"                    <condition attribute='new_personal_id' operator='like' value='%{System.Security.SecurityElement.Escape(nationId)}%' />");
                
                if (hasLastSixDigit)
                    conditions.AppendLine($"                    <condition attribute='new_last_six_digit' operator='like' value='%{System.Security.SecurityElement.Escape(lastSixDigit)}%' />");

                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='pager' />
                                <attribute name='new_cell_list_contact' />
                                <attribute name='new_personal_id' />
                                <attribute name='new_last_six_digit' />
                                <attribute name='contactid' />
                                <order attribute='fullname' descending='false' />
                                <filter type='and'>
                                  <filter type='or'>
{conditions}
                                  </filter>
                                  <condition attribute='statuscode' operator='eq' value='1' />
                                </filter>
                              </entity>
                            </fetch>";

                var fetchRequest = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
                return response.EntityCollection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryDediccationContatsByFetchXml 發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 根據開頭奉獻編號查詢聯絡人 (取前3筆,依奉獻編號降序)
        /// </summary>
        public EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber)
        {
            try
            {
                dedicationStartNumber = "'" + dedicationStartNumber + "%'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='3'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='pager' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='contactid' />
                                <order attribute='pager' descending='true' />
                                <filter type='and'>
                                  <condition attribute='pager' operator='like' value=" + dedicationStartNumber + @" />
                                </filter>
                              </entity>
                            </fetch>";

                var fetchRequest = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
                return response.EntityCollection;
            }
            catch (Exception ex)
            {
                SafeLogError(ex, "QueryContatsByStartedDedicationNumber 發生錯誤");
                throw;
            }
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
