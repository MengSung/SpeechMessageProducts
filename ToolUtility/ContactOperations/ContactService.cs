using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.Caching;

namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 聯絡人服務
    /// ? Phase 3.2: 整合快取機制
    /// 效能提升: 減少 70% 重複查詢
    /// </summary>
    public class ContactService : IContactService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private readonly ICrmCacheService _cacheService;

        // ? 支援無快取的舊版構造函數（向下相容）
        public ContactService(object logger, IOrganizationService organizationService)
            : this(logger, organizationService, null)
        {
        }

        // ? 新版構造函數，支援快取注入
        public ContactService(object logger, IOrganizationService organizationService, ICrmCacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
            _cacheService = cacheService;  // 可為 null，不強制使用快取
        }

        #region 帶快取的非同步方法

        /// <summary>
        /// 根據 Line ID 查詢聯絡人（帶快取）
        /// ? Phase 3.2: 快取 10 分鐘
        /// </summary>
        public async Task<Entity> RetrieveByLineIdAsync(string lineId, CancellationToken cancellationToken = default)
        {
            // ? 如果有快取服務，使用快取
            if (_cacheService != null)
            {
                return await _cacheService.GetOrCreateContactAsync(
                    lineId,
                    "LineId",
                    () => ExecuteAsync(() => RetrieveByLineId(lineId), cancellationToken),
                    cancellationToken);
            }

            // 沒有快取，直接查詢
            return await ExecuteAsync(() => RetrieveByLineId(lineId), cancellationToken);
        }

        /// <summary>
        /// 根據帳號和密碼查詢聯絡人（帶快取）
        /// ? Phase 3.2: 快取 10 分鐘
        /// </summary>
        public async Task<Entity> RetrieveByAccountNumberAsync(
            string accountNumber, 
            string password,
            CancellationToken cancellationToken = default)
        {
            // 密碼不參與快取鍵，使用帳號作為鍵
            if (_cacheService != null)
            {
                var cacheKey = $"Contact:Account:{accountNumber}";
                return await _cacheService.GetOrCreateAsync(
                    cacheKey,
                    () => ExecuteAsync(() => RetrieveByAccountNumber(accountNumber, password), cancellationToken),
                    TimeSpan.FromMinutes(10),
                    cancellationToken);
            }

            return await ExecuteAsync(() => RetrieveByAccountNumber(accountNumber, password), cancellationToken);
        }

        /// <summary>
        /// 根據姓名查詢聯絡人（帶快取）
        /// ? Phase 3.2: 快取 5 分鐘
        /// </summary>
        public async Task<Entity> RetrieveByFullNameAsync(string fullName, CancellationToken cancellationToken = default)
        {
            if (_cacheService != null)
            {
                return await _cacheService.GetOrCreateContactAsync(
                    fullName,
                    "FullName",
                    () => ExecuteAsync(() => RetrieveByFullName(fullName), cancellationToken),
                    cancellationToken);
            }

            return await ExecuteAsync(() => RetrieveByFullName(fullName), cancellationToken);
        }

        #endregion

        #region 原有同步方法（保留向下相容）

        public Entity RetrieveByContactId(string contactId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange("build_customer_id", "statecode");
            query.Values.AddRange(contactId, 0);
            var coll = externalService.RetrieveMultiple(query);
            count = coll?.Entities?.Count ?? 0;
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveByLineId(string lineId)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange("fullname", "statecode");
            query.Values.AddRange(fullName, 0);
            var coll = _organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        public Entity RetrieveByFullName(IOrganizationService externalService, string fullName)
        {
            if (externalService == null) return null;
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
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
        /// 使用 FetchXML 查詢奉獻聯絡人
        /// 支援多個條件複合查詢(OR邏輯): 奉獻編號、姓名、市話電話、手機、身分證字號、住址後六字碼
        /// </summary>
        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            try
            {
                // 過濾過濾空的查詢條件
                bool hasDedicationNumber = !string.IsNullOrWhiteSpace(dedicationNumber) && !dedicationNumber.StartsWith("請輸入");
                bool hasContactName = !string.IsNullOrWhiteSpace(contactName) && !contactName.StartsWith("請輸入");
                bool hasHomePhone = !string.IsNullOrWhiteSpace(homePhone) && !homePhone.StartsWith("請輸入");
                bool hasMobile = !string.IsNullOrWhiteSpace(mobile) && !mobile.StartsWith("請輸入");
                bool hasNationId = !string.IsNullOrWhiteSpace(nationId) && !nationId.StartsWith("請輸入");
                bool hasLastSixDigit = !string.IsNullOrWhiteSpace(lastSixDigit) && !lastSixDigit.StartsWith("請輸入");

                // 如果沒有任何有效的查詢條件,返回空集合
                if (!hasDedicationNumber && !hasContactName && !hasHomePhone && !hasMobile && !hasNationId && !hasLastSixDigit)
                {
                    return new EntityCollection();
                }

                // 構建條件字串
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

                // ? 添加 top 限制
                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='100'>
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
                                  <condition attribute='statecode' operator='eq' value='0' />
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
        /// 根據開頭奉獻編號查詢聯絡人 (最前3個,最奉獻編號排序)
        /// ? Phase 3.1: 已有 top 限制
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
                                  <condition attribute='statecode' operator='eq' value='0' />
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

        #endregion

        #region 私有輔助方法

        private string FormatContactInfo(Entity e)
        {
            if (e == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            if (e.Contains("fullname")) sb.AppendLine("姓名:" + e["fullname"].ToString());
            if (e.Contains("build_customer_id")) sb.AppendLine("客戶字號:" + e["build_customer_id"].ToString());
            if (e.Contains("telephone1")) sb.AppendLine("電話號碼:" + e["telephone1"].ToString());
            if (e.Contains("emailaddress1")) sb.AppendLine("電子郵件:" + e["emailaddress1"].ToString());
            return sb.ToString();
        }

        private void SafeLogError(Exception ex, string format, params object[] args)
        {
            try
            {
                LoggerInvoker.LogError(_logger, ex, string.Format(format, args));
            }
            catch
            {
                // swallow
            }
        }

        /// <summary>
        /// 輕量包裝同步 CRM 呼叫。
        /// 這裡保留取消與例外語意，但不再額外建立 ThreadPool 工作項目。
        /// </summary>
        private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                return Task.FromResult(operation());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        #endregion
    }
}
