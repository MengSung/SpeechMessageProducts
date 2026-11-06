// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ContactOperations/ContactService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ContactService
// 主要成員：RetrieveByLineIdAsync、RetrieveByAccountNumberAsync、RetrieveByFullNameAsync、RetrieveByContactId、GetContactInfoByContactId、RetrieveByLineId、RetrieveByLineIdForCollection、RetrieveCollectionByLineId、RetrieveCollectionByName、GetContactInfoByFullName
// 引用命名空間：System、System.Threading、System.Threading.Tasks、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Messages、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.Diagnostics、ToolUtilityNameSpace.Caching
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    /// 保留舊聯絡人查詢與 host 可選快取；服務只限原已授權的 SDK scope，不得跨使用者／租戶共用。
    /// 帳密核對永不從快取讀取或寫回驗證結果，每次檢查同一授權 SDK 的當前資料。
    /// 其他名稱／LINE 快取入口仍屬 host，後續須各自驗證完整隔離邊界，不能因純介面已抽取就宣稱安全。
    /// SDK、logger 與 cache 由 host 擁有；本服務不建立背景工作，host 在操作 drain 後釋放自己的資源。
    /// </summary>
    public class ContactService : IContactService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private readonly ICrmCacheService _cacheService;

        /// <summary>保留無快取建構入口，借用已授權 SDK 與 host 診斷物件至原 scope 結束，不接管 Dispose。</summary>
        /// <param name="logger">host 擁有的診斷物件，不應帶有其他使用者狀態。</param>
        /// <param name="organizationService">host 已授權且有界存活的 SDK，不得跨租戶借用。</param>
        public ContactService(object logger, IOrganizationService organizationService)
            : this(logger, organizationService, null)
        {
        }

        /// <summary>保留可選 host 快取注入，但帳密核對不使用它；三個依賴均由 host 原 scope 管理。</summary>
        /// <param name="logger">host 擁有的診斷物件。</param>
        /// <param name="organizationService">同一已驗證 scope 的借入 SDK，host 先 drain 再釋放。</param>
        /// <param name="cacheService">可為 null 的 host 快取；不得保存密碼或已核對身份供另一次登入重用。</param>
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
        /// 每次直接以原已授權 SDK 核對帳號及當前密碼，不快取成功身份，也不把密碼／雜湊加入快取鍵。
        /// 只以帳號命中的舊快取可能屬其他使用者、組織或已變更密碼，絕不能用它略過本次驗證。
        /// 同步 I/O 前後檢查 caller 取消；真正 SDK 故障保留原例外，交最終 host 共用告警而不回舊資料。
        /// </summary>
        /// <param name="accountNumber">僅供此次查詢的帳號，不是伺服器授權的替代物。</param>
        /// <param name="password">僅在此次核對期間使用的機密，不記錄、不存入快取或背景工作。</param>
        /// <param name="cancellationToken">明確 caller 取消；不能中斷已進入 SDK 的同步 I/O。</param>
        /// <returns>當前密碼相符的操作自有 Entity，否則 null；不建立登入票證或 Session。</returns>
        public Task<Entity> RetrieveByAccountNumberAsync(
            string accountNumber,
            string password,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() => RetrieveByAccountNumber(accountNumber, password), cancellationToken);
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
        /// 在 caller 執行緒包裝同步 CRM 呼叫，不排程背景工作、不保存候選或 caller 機密。
        /// 明確 caller 取消在 I/O 前後檢查；內部取消而 caller 未取消仍是故障，不能假稱正常取消。
        /// 正常完成／故障／取消均不持有 cancellation registration 或其他需處置資源。
        /// </summary>
        private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                var result = operation();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        #endregion
    }
}
