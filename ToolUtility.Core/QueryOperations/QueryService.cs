/*
 * 檔案整體說明
 *
 * 目的與定位：Repository型別的五個共用查詢；維持舊namespace、參數與查詢形狀，不建立CRM連線。 查詢值和資料庫ID不是授權；呼叫端必須先驗證subject、organization、產品及資料範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 QueryService。
 * - 保留原雙參數建構契約；只保存本次資料操作邊界的Repository，不保存未使用的SDK別名。 Repository、SDK及其授權範圍仍由host建立、排空及處置，不能把本服務註冊成跨使用者singleton。
 * - 可注入無scope狀態的純時鐘，讓舊本地時間查詢可控測試；時鐘生命週期不含request、SDK或credential。
 * - 以欄位等值取得最多一筆並回傳null或該列；完整頁驗證先於選取，不能讓重複ID或錯表混入結果。 使用者欄位值只用於此次查詢，不會記錄、快取或由此取得授權。
 * - 以欄位等值讀取一頁；保留SDK分頁metadata，完整驗證後才發布且不按欄位值去重。
 * 主要操作入口包含 RetrieveEntityByField、RetrieveEntityCollectionByField、QueryByFetchXml、QueryBloodReportByContactId、
 * QueryPresentRecordByContactIdAndSunday、GetRepository、GetClock、Failure；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Tests/DataOperations/RepositoryQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/RepositoryQuerySafetyTests.cs、
 * ToolUtility.Tests/EntityOperations/RepositoryCallerRouteTests.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.TimeOperations;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// Repository型別的五個共用查詢；維持舊namespace、參數與查詢形狀，不建立CRM連線。
    /// 查詢值和資料庫ID不是授權；呼叫端必須先驗證subject、organization、產品及資料範圍。
    /// </summary>
    /// <remarks>
    /// 每次呼叫僅取得局部Repository參照；不保存SDK、logger、查詢值、結果、credential或使用者狀態。
    /// 每頁最多5000列，先完整驗證根表與非空唯一資料庫ID，再回傳原候選；合法同名異ID不合併。
    /// Dispose拒絕新入口並清Repository參照；已取得參照的同步操作由host排空，依賴的Repository及SDK不由本類Dispose。
    /// 舊catch仍包固定操作訊息並保留原Exception為InnerException，但不寫入欄位值、查詢XML或例外文字。
    /// 舊present查詢仍使用伺服器LocalNow且仍不把listEntityId加入條件；教會時間與業務修正不屬本次抽取。
    /// </remarks>
    public class QueryService : IQueryService, IDisposable
    {
        private IEntityRepository? _repository;
        private IClock? _clock;
        private int _disposed;

        /// <summary>
        /// 保留原雙參數建構契約；只保存本次資料操作邊界的Repository，不保存未使用的SDK別名。
        /// Repository、SDK及其授權範圍仍由host建立、排空及處置，不能把本服務註冊成跨使用者singleton。
        /// </summary>
        /// <param name="repository">借入的純資料Repository。</param>
        /// <param name="organizationService">保留舊建構契約的SDK參數；由呼叫端擁有，本類不延長其生命。</param>
        public QueryService(IEntityRepository repository, IOrganizationService organizationService)
            : this(repository, organizationService, new SystemClock())
        {
        }

        /// <summary>
        /// 可注入無scope狀態的純時鐘，讓舊本地時間查詢可控測試；時鐘生命週期不含request、SDK或credential。
        /// </summary>
        /// <param name="repository">借入的純資料Repository。</param>
        /// <param name="organizationService">保留舊建構契約的SDK參數；由呼叫端擁有。</param>
        /// <param name="clock">無scope狀態的時鐘，生命週期不得持有使用者或租戶資料。</param>
        public QueryService(IEntityRepository repository, IOrganizationService organizationService, IClock clock)
        {
#if NETFRAMEWORK
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (organizationService == null) throw new ArgumentNullException(nameof(organizationService));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
#else
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(organizationService);
            ArgumentNullException.ThrowIfNull(clock);
#endif
            _repository = repository;
            _clock = clock;
        }

        /// <summary>
        /// 以欄位等值取得最多一筆並回傳null或該列；完整頁驗證先於選取，不能讓重複ID或錯表混入結果。
        /// 使用者欄位值只用於此次查詢，不會記錄、快取或由此取得授權。
        /// </summary>
        /// <param name="entityName">可信程式指定的CRM根實體logical name。</param>
        /// <param name="fieldName">可信程式指定的欄位logical name。</param>
        /// <param name="fieldValue">本次等值條件，不會出現在診斷輸出。</param>
        /// <returns>沒有符合資料時為null；有資料時為已驗證的正式資料列。</returns>
        public Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            try
            {
                var repository = GetRepository();
                SdkDataQuery.RequireLogicalName(entityName, nameof(entityName));
                SdkDataQuery.RequireLogicalName(fieldName, nameof(fieldName));
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions = { new ConditionExpression(fieldName, ConditionOperator.Equal, fieldValue) }
                    },
                    TopCount = 1
                };
                var page = SdkDataQuery.ValidatePage(repository.RetrieveMultiple(query), entityName, 1);
                return page.Entities.Count == 0 ? null : page.Entities[0];
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception failure)
            {
                throw Failure("RetrieveEntityByField", failure);
            }
        }

        /// <summary>
        /// 以欄位等值讀取一頁；保留SDK分頁metadata，完整驗證後才發布且不按欄位值去重。
        /// </summary>
        /// <param name="entityName">可信程式指定的CRM根實體logical name。</param>
        /// <param name="fieldName">可信程式指定的欄位logical name。</param>
        /// <param name="fieldValue">本次等值條件，不會出現在診斷輸出。</param>
        /// <returns>已驗證的單頁SDK集合；呼叫端仍須依metadata處理後續頁面。</returns>
        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            try
            {
                var repository = GetRepository();
                SdkDataQuery.RequireLogicalName(entityName, nameof(entityName));
                SdkDataQuery.RequireLogicalName(fieldName, nameof(fieldName));
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions = { new ConditionExpression(fieldName, ConditionOperator.Equal, fieldValue) }
                    }
                };
                return SdkDataQuery.ValidatePage(repository.RetrieveMultiple(query), entityName, 5000);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception failure)
            {
                throw Failure("RetrieveEntityCollectionByField", failure);
            }
        }

        /// <summary>
        /// 執行既有FetchXML單頁；安全串流讀取根表後核對完整回應，不允許DTD、外部解析或多根實體。
        /// 查詢文字只在本次呼叫存活，拒絕錯誤時也不回顯查詢或條件值。
        /// </summary>
        /// <param name="fetchXml">受信任程式建立且已完成scope授權的FetchXML。</param>
        /// <returns>根表及整頁正式ID通過驗證的SDK回應。</returns>
        public EntityCollection QueryByFetchXml(string fetchXml)
        {
            try
            {
                var repository = GetRepository();
                var entityName = SdkDataQuery.ExpectedFetchEntity(fetchXml, nameof(fetchXml));
                return SdkDataQuery.ValidatePage(repository.RetrieveMultiple(new FetchExpression(fetchXml)), entityName, 5000);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception failure)
            {
                throw Failure("QueryByFetchXml", failure);
            }
        }

        /// <summary>
        /// 依聯絡人正式ID查血液報告並沿用createdon降冪及第一列語義；仍驗證整個SDK頁面後才選取。
        /// 敏感欄位資料不進入共享狀態或診斷。
        /// </summary>
        /// <param name="contactId">伺服器已授權scope內的非空聯絡人ID。</param>
        /// <returns>最新排序頁的第一筆，或查無資料時null。</returns>
        public Entity? QueryBloodReportByContactId(Guid contactId)
        {
            try
            {
                var repository = GetRepository();
                RequireId(contactId, nameof(contactId));
                var query = new QueryExpression
                {
                    EntityName = "new_blood_report",
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions = { new ConditionExpression("new_blood_contact_relation", ConditionOperator.Equal, contactId) }
                    }
                };
                query.AddOrder("createdon", OrderType.Descending);
                var page = SdkDataQuery.ValidatePage(repository.RetrieveMultiple(query), query.EntityName, 5000);
                return page.Entities.Count == 0 ? null : page.Entities[0];
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception failure)
            {
                throw Failure("QueryBloodReportByContactId", failure);
            }
        }

        /// <summary>
        /// 保留既有LocalNow.AddMonths算法、AllColumns投影、日期GreaterEqual及降冪排序與舊版未使用的list參數。
        /// list與contact由呼叫端先授權；本抽取不自行把原本缺漏的list條件加進查詢。
        /// </summary>
        /// <param name="listEntityId">相容舊API的list ID；原查詢未使用，不能視為服務端授權依據。</param>
        /// <param name="contactId">伺服器已授權scope內的非空聯絡人ID。</param>
        /// <param name="monthPeriod">正月份期間；由舊本地瞬間往前移動指定月份，不改成週數或教會時區。</param>
        /// <returns>完整驗證的單頁出席記錄。</returns>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod)
        {
            try
            {
                var repository = GetRepository();
                RequireId(listEntityId, nameof(listEntityId));
                RequireId(contactId, nameof(contactId));
                if (monthPeriod < 1) throw new ArgumentOutOfRangeException(nameof(monthPeriod), "A positive period is required.");
                var startDate = GetClock().LocalNow.AddMonths(-monthPeriod);
                var query = new QueryExpression("new_individual_meeting_and_spiritual_meditation_records")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_contact_present_record", ConditionOperator.Equal, contactId),
                            new ConditionExpression("new_sunday_date", ConditionOperator.GreaterEqual, startDate)
                        }
                    }
                };
                query.AddOrder("new_sunday_date", OrderType.Descending);
                return SdkDataQuery.ValidatePage(repository.RetrieveMultiple(query), query.EntityName, 5000);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception failure)
            {
                throw Failure("QueryPresentRecordByContactIdAndSunday", failure);
            }
        }

        /// <summary>服務Dispose後拒絕新工作；先前已取得的操作局部參照可同步排空。</summary>
        private IEntityRepository GetRepository()
        {
            var repository = Volatile.Read(ref _repository);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(QueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return repository ?? throw new ObjectDisposedException(nameof(QueryService));
        }

        /// <summary>時鐘只作本次同步讀值；Dispose清owner參照，既有局部讀值不包含request或資料scope。</summary>
        private IClock GetClock()
        {
            var clock = Volatile.Read(ref _clock);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(QueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return clock ?? throw new ObjectDisposedException(nameof(QueryService));
        }

        /// <summary>保留舊失敗包裝形狀，訊息固定且不含request值；原始錯誤型別與堆疊保留在InnerException。</summary>
        private static InvalidOperationException Failure(string operation, Exception failure) =>
            new InvalidOperationException("The query operation failed: " + operation + ".", failure);

        /// <summary>正式資料列必須具有非空資料庫ID；查詢參數不是建立新授權的來源。</summary>
        private static void RequireId(Guid id, string parameter)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", parameter);
        }

        /// <summary>冪等清除借入Repository參照；不Dispose Repository／SDK，也不截斷已入場同步呼叫。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Exchange(ref _repository, null);
                Interlocked.Exchange(ref _clock, null);
            }
        }

        /// <summary>停止後續呼叫並清除owner參照；服務沒有自有背景工作或unmanaged資源。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
