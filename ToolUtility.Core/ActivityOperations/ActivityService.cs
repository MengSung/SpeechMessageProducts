/*
 * 檔案整體說明
 *
 * 目的與定位：活動參與者解析與原狀態碼轉移；只借用 host 已授權的 scope SDK，不保存結果、logger 或個資。 異質記錄以 logical name＋正式 GUID 識別，全部 
 * I/O／識別驗證通過後才追加對齊的輸出清單。
 *
 * 主要功能與流程：
 * 本檔宣告 ActivityService。
 * - 保留舊簽章及 null 拒絕；不保存未使用的 logger，SDK 僅在同一授權 scope 借用。 不建立連線或取得任何使用者權限，借入 SDK 永遠由原 host owner 釋放。
 * - 依原參與者順序追加完整實體與類型，保留前綴與 AllColumns／IN 批次查詢。 缺 partyid 的外部地址沿用略過；正式參照的缺列、錯表、重複或故障一律整體拒絕，不偷偷裁剪。 一般單頁保留原無排序查詢；
 * 只有發現 MoreRecords 才丟棄探測頁，另以主鍵排序重新完整分頁。
 * - 不執行 SDK I/O，先完整驗證全部 partyid 及既有前綴，再追加對齊的 GUID／類型候選。 活動容器不是輸出記錄，不替缺 partyid 的外部地址補造 ID；任何壞參照都不留下前半清單。
 * - 使用同一 scope 借入 SDK 完成活動；維持原 1／4 狀態碼，不自行查權限或重試不確定寫入。
 * 主要操作入口包含 GetActivityPartyList、GetActivityPartyIdList、SetActivityStatusToCompleted、
 * SetAppointmentStatusToScheduled、ExecuteState、RequireInputs、RequireStateInputs、ValidatePrefix；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.ActivityOperations
{
    /// <summary>
    /// 活動參與者解析與原狀態碼轉移；只借用 host 已授權的 scope SDK，不保存結果、logger 或個資。
    /// 異質記錄以 logical name＋正式 GUID 識別，全部 I/O／識別驗證通過後才追加對齊的輸出清單。
    /// </summary>
    /// <remarks>
    /// 每次來源容器及輸出至多 5000 列、每批 500 個識別，為本地資源預算而非組織實測上限。
    /// 所有結果與暫存集合皆為操作局部，不建立 cache／背景工作／隱藏重試。完整欄位傳輸的 bytes／逾時由 host gateway 限制。
    /// caller 必須獨占普通 ArrayList，不可同時修改或列舉；兩個既有參數無法對違約並行讀者提供跨物件原子交換。
    /// Dispose 停止新操作並清除借用參照；已取得 SDK 的同步操作可完成，host 必須 drain 後才處置其 SDK。
    /// 真故障保留同一 InnerException 交由 host 最終共用告警入口，純核心不寫檔、不發 LINE、不輸出原訊息。
    /// </remarks>
    public class ActivityService : IActivityService, IDisposable
    {
        private const int MaximumParties = 5000;
        private const int BatchSize = 500;
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>
        /// 保留舊簽章及 null 拒絕；不保存未使用的 logger，SDK 僅在同一授權 scope 借用。
        /// 不建立連線或取得任何使用者權限，借入 SDK 永遠由原 host owner 釋放。
        /// </summary>
        /// <param name="logger">不可為 null 的相容參數；不呼叫、不保存或 Dispose。</param>
        /// <param name="organizationService">host 已驗證授權及隔離的借入服務，不得跨 scope 重用本服務。</param>
        public ActivityService(object logger, IOrganizationService organizationService)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 依原參與者順序追加完整實體與類型，保留前綴與 AllColumns／IN 批次查詢。
        /// 缺 partyid 的外部地址沿用略過；正式參照的缺列、錯表、重複或故障一律整體拒絕，不偷偷裁剪。
        /// 一般單頁保留原無排序查詢；只有發現 MoreRecords 才丟棄探測頁，另以主鍵排序重新完整分頁。
        /// </summary>
        /// <param name="activityEntity">caller 操作獨占、已授權活動，不在服務保存。</param>
        /// <param name="fromOrTo">原參與者欄位，亦接受 requiredattendees／optionalattendees 等合法 logical name。</param>
        /// <param name="partyList">caller 獨占的普通 ArrayList，既有正式實體不可重複且須與類型對齊。</param>
        /// <param name="partyTypeList">不同於實體清單的普通 ArrayList；所有元素為對應 logical name。</param>
        public void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList)
        {
            RequireInputs(activityEntity, partyList, partyTypeList, nameof(partyList));
            var sdk = GetService();
            try
            {
                var identities = ValidatePrefix(partyList, partyTypeList, false);
                var references = ReadReferences(activityEntity, fromOrTo, identities);
                var result = new Dictionary<Tuple<string, Guid>, Entity>(references.Count);
                using var reader = new EntityQueryService(organizationService: sdk);
                // GroupBy 只決定批次，不去重；重複正式識別已在完整消費集合驗證時拒絕。
                foreach (var group in references.GroupBy(reference => reference.LogicalName, StringComparer.Ordinal))
                {
                    var ids = group.Select(reference => reference.Id).ToArray();
                    for (var start = 0; start < ids.Length; start += BatchSize)
                    {
                        var batch = ids.Skip(start).Take(BatchSize).ToArray();
                        foreach (var row in ReadBatch(reader, group.Key, batch))
                            result.Add(Tuple.Create(row.LogicalName, row.Id), row);
                    }
                }
                var candidates = new object[references.Count];
                var types = new object[references.Count];
                for (var index = 0; index < references.Count; index++)
                {
                    var reference = references[index];
                    candidates[index] = result[Tuple.Create(reference.LogicalName, reference.Id)];
                    types[index] = reference.LogicalName;
                }
                AppendComplete(partyList, partyTypeList, candidates, types);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The activity parties could not be retrieved.", failure);
            }
        }

        /// <summary>
        /// 不執行 SDK I/O，先完整驗證全部 partyid 及既有前綴，再追加對齊的 GUID／類型候選。
        /// 活動容器不是輸出記錄，不替缺 partyid 的外部地址補造 ID；任何壞參照都不留下前半清單。
        /// </summary>
        /// <param name="activityEntity">已授權且操作期間獨占的活動資料。</param>
        /// <param name="fromOrTo">原參與者欄位的合法 logical name，不是授權依據。</param>
        /// <param name="partyIdList">不同於類型清單的普通 ArrayList；既有元素須為非空 GUID。</param>
        /// <param name="partyTypeList">與每個 ID 對齊的 logical name，caller 不可並行讀寫。</param>
        public void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList)
        {
            RequireInputs(activityEntity, partyIdList, partyTypeList, nameof(partyIdList));
            ThrowIfDisposed();
            try
            {
                var identities = ValidatePrefix(partyIdList, partyTypeList, true);
                var references = ReadReferences(activityEntity, fromOrTo, identities);
                AppendComplete(partyIdList, partyTypeList, references.Select(reference => (object)reference.Id).ToArray(),
                    references.Select(reference => (object)reference.LogicalName).ToArray());
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The activity party identities could not be retrieved.", failure);
            }
        }

        /// <summary>使用同一 scope 借入 SDK 完成活動；維持原 1／4 狀態碼，不自行查權限或重試不確定寫入。</summary>
        /// <param name="activityName">host 已授權的活動 logical name，狀態碼相容性仍由原組織決定。</param>
        /// <param name="activityId">同一授權 scope 的非空正式 ID。</param>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId)
            => SetActivityStatusToCompleted(activityName, activityId, GetService());

        /// <summary>只使用此次明確借入 SDK 執行一次狀態轉移，不回退到 ctor SDK，不接管任何服務所有權。</summary>
        /// <param name="activityName">已授權的合法活動 logical name；空字串維持舊 ArgumentNullException。</param>
        /// <param name="activityId">非空正式資料庫 ID。</param>
        /// <param name="organizationService">host 擁有的同 scope SDK，caller 須維持至同步呼叫排空。</param>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(activityName)) throw new ArgumentNullException(nameof(activityName));
            RequireName(activityName, nameof(activityName));
            RequireStateInputs(activityId, organizationService);
            try { ExecuteState(organizationService, activityName, activityId, 1, 4); }
            catch (Exception failure) { throw new InvalidOperationException("The activity could not be completed.", failure); }
        }

        /// <summary>以 ctor 借入 SDK 將 appointment 轉為原 3／5 排程狀態；不變更既有組織狀態設計。</summary>
        /// <param name="activityId">同 scope 的非空正式約會 ID。</param>
        public void SetAppointmentStatusToScheduled(Guid activityId) => SetAppointmentStatusToScheduled(activityId, GetService());

        /// <summary>以明確外部 SDK 排程約會，必須確認 SetStateResponse 才成功；故障保留原例外且不重試。</summary>
        /// <param name="activityId">已授權、非空正式約會 ID。</param>
        /// <param name="organizationService">此次操作借用且由 caller 擁有的服務，永不 Dispose。</param>
        public void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService)
        {
            ThrowIfDisposed();
            RequireStateInputs(activityId, organizationService);
            try { ExecuteState(organizationService, "appointment", activityId, 3, 5); }
            catch (Exception failure) { throw new InvalidOperationException("The appointment could not be scheduled.", failure); }
        }

        /// <summary>以一次 SDK 寫入維持原三參數；null 或其他訊息回應是未知結果，不可回報成功或自動重送。</summary>
        private static void ExecuteState(IOrganizationService service, string name, Guid id, int state, int status)
        {
            var request = new SetStateRequest
            {
                EntityMoniker = new EntityReference(name, id), State = new OptionSetValue(state), Status = new OptionSetValue(status)
            };
            if (service.Execute(request) is not SetStateResponse)
                throw new InvalidOperationException("The SDK returned an invalid state response.");
        }

        /// <summary>保留公開 null 前置條件，不以操作包裝隱藏 caller 的程式錯誤。</summary>
        private static void RequireInputs(Entity activityEntity, ArrayList values, ArrayList partyTypeList, string valueParameter)
        {
#if NETFRAMEWORK
            if (activityEntity == null) throw new ArgumentNullException(nameof(activityEntity));
            if (values == null) throw new ArgumentNullException(valueParameter);
            if (partyTypeList == null) throw new ArgumentNullException(nameof(partyTypeList));
#else
            ArgumentNullException.ThrowIfNull(activityEntity);
            ArgumentNullException.ThrowIfNull(values, valueParameter);
            ArgumentNullException.ThrowIfNull(partyTypeList);
#endif
        }

        /// <summary>狀態寫入前驗 caller 的 ID／SDK，不讓空正式識別進入不確定寫入路徑。</summary>
        private static void RequireStateInputs(Guid activityId, IOrganizationService organizationService)
        {
#if NETFRAMEWORK
            if (organizationService == null) throw new ArgumentNullException(nameof(organizationService));
#else
            ArgumentNullException.ThrowIfNull(organizationService);
#endif
            if (activityId == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(activityId));
        }

        /// <summary>驗證兩個獨占普通清單及整個既有消費前綴；異質表同 GUID 合法，但同表同 ID 必須拒絕。</summary>
        private static HashSet<Tuple<string, Guid>> ValidatePrefix(ArrayList values, ArrayList types, bool ids)
        {
            if (ReferenceEquals(values, types) || values.GetType() != typeof(ArrayList) || types.GetType() != typeof(ArrayList)
                || values.Count != types.Count || values.Count > MaximumParties)
                throw new InvalidOperationException("The activity output lists are not exclusive aligned lists.");
            var identities = new HashSet<Tuple<string, Guid>>();
            for (var index = 0; index < values.Count; index++)
            {
                if (types[index] is not string name) throw new InvalidOperationException("An activity party type is invalid.");
                RequireName(name, "partyTypeList");
                Guid id;
                if (ids && values[index] is Guid value) id = value;
                else if (!ids && values[index] is Entity row && string.Equals(row.LogicalName, name, StringComparison.Ordinal)) id = row.Id;
                else throw new InvalidOperationException("An activity output identity is invalid.");
                if (id == Guid.Empty || !identities.Add(Tuple.Create(name, id)))
                    throw new InvalidOperationException("The activity output contains invalid or duplicate identities.");
            }
            return identities;
        }

        /// <summary>一次讀完且驗證所有參照後才允許 I/O；總量含既有前綴，缺 partyid 容器只略過、不補造正式列。</summary>
        private static List<EntityReference> ReadReferences(Entity activity, string field, HashSet<Tuple<string, Guid>> identities)
        {
            RequireName(field, "fromOrTo");
            var result = new List<EntityReference>();
            if (!activity.Attributes.TryGetValue(field, out var value) || value == null) return result;
            if (value is not EntityCollection collection) throw new InvalidOperationException("The activity party collection is invalid.");
            if (collection.Entities.Count > MaximumParties) throw new InvalidOperationException("The activity party source exceeds the row budget.");
            foreach (var party in collection.Entities)
            {
                if (party == null) throw new InvalidOperationException("The activity party container is invalid.");
                if (!party.Attributes.TryGetValue("partyid", out var referenceValue)) continue;
                if (referenceValue is not EntityReference reference || reference.Id == Guid.Empty)
                    throw new InvalidOperationException("The activity party reference is invalid.");
                RequireName(reference.LogicalName, "partyid");
                if (!identities.Add(Tuple.Create(reference.LogicalName, reference.Id)))
                    throw new InvalidOperationException("The activity parties contain duplicate database identities.");
                if (identities.Count > MaximumParties) throw new InvalidOperationException("The activity output exceeds the row budget.");
                // 不保存輸入 EntityReference 的額外內容；只複製本次已驗識別，不跨操作保存候選。
                result.Add(new EntityReference(reference.LogicalName, reference.Id));
            }
            return result;
        }

        /// <summary>
        /// 單批完整讀取且至多 ids.Length 個正式頁；每頁須有進度且只含本批唯一識別。
        /// 保留一般舊單頁 golden；發現分頁才丟棄探測候選，改用明確主鍵排序重讀，不能混入兩次讀取的部分快照。
        /// SDK 若未提供 cookie，仍按頁碼有界讀取；任何變動造成的缺列／重複均 fail closed，供 caller 明確重試。
        /// </summary>
        private static Dictionary<Guid, Entity>.ValueCollection ReadBatch(EntityQueryService reader, string name, Guid[] ids)
        {
            var query = new QueryExpression(name) { ColumnSet = new ColumnSet(true), PageInfo = new PagingInfo { Count = BatchSize, PageNumber = 1 } };
            query.Criteria.AddCondition(name + "id", ConditionOperator.In, ids.Cast<object>().ToArray());
            var requested = new HashSet<Guid>(ids);
            var candidate = new Dictionary<Guid, Entity>(ids.Length);
            var probe = reader.RetrieveMultiple(query);
            AddPage(probe, name, requested, candidate);
            if (probe.MoreRecords)
            {
                if (candidate.Count == 0 || candidate.Count == ids.Length)
                    throw new InvalidOperationException("The SDK returned an inconsistent activity page.");
                candidate.Clear();
                query.AddOrder(name + "id", OrderType.Ascending);
                for (var pageNumber = 1; pageNumber <= ids.Length; pageNumber++)
                {
                    query.PageInfo.PageNumber = pageNumber;
                    var page = reader.RetrieveMultiple(query);
                    AddPage(page, name, requested, candidate);
                    if (!page.MoreRecords) break;
                    if (page.Entities.Count == 0 || candidate.Count >= ids.Length)
                        throw new InvalidOperationException("The SDK returned an inconsistent activity page.");
                    query.PageInfo.PagingCookie = page.PagingCookie;
                }
            }
            if (candidate.Count != ids.Length) throw new InvalidOperationException("The SDK did not return every requested activity party.");
            return candidate.Values;
        }

        /// <summary>共用讀取器已驗每頁 ID／根表；此處再驗回應 metadata、查詢集合及跨頁唯一性，不合併或略過壞列。</summary>
        private static void AddPage(EntityCollection page, string name, HashSet<Guid> requested, Dictionary<Guid, Entity> candidate)
        {
            if ((!string.IsNullOrEmpty(page.EntityName) && !string.Equals(page.EntityName, name, StringComparison.Ordinal))
                || page.Entities.Count > requested.Count)
                throw new InvalidOperationException("The SDK returned an invalid activity page.");
            foreach (var row in page.Entities)
            {
                if (!requested.Contains(row.Id) || candidate.ContainsKey(row.Id))
                    throw new InvalidOperationException("The SDK returned unrequested or duplicate activity parties.");
                candidate.Add(row.Id, row);
            }
        }

        /// <summary>
        /// 所有候選與容量配置均先完成，普通 ArrayList 的兩次陣列複製不再執行 I/O／自訂 callback。
        /// 成功只 append，不清除前綴；caller 獨占是舊 API 必要前置，不宣稱跨物件原子交換或支援非同步終止執行緒。
        /// </summary>
        private static void AppendComplete(ArrayList values, ArrayList types, object[] candidates, object[] candidateTypes)
        {
            var total = checked(values.Count + candidates.Length);
            if (values.Capacity < total) values.Capacity = total;
            if (types.Capacity < total) types.Capacity = total;
            values.AddRange(candidates);
            types.AddRange(candidateTypes);
        }

        /// <summary>只接受合法結構字元，不將字串非空誤當成表名合法或授權完成。</summary>
        private static void RequireName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>單次取得借用 SDK，不新增鎖或跨 scope 全域序列化；host 負責 SDK 本身的並行與逾時政策。</summary>
        private IOrganizationService GetService()
        {
            var service = Volatile.Read(ref _organizationService);
            ThrowIfDisposed();
            return service ?? throw new ObjectDisposedException(nameof(ActivityService));
        }

        /// <summary>Dispose 後所有新入口（含純 ID 與 external SDK）都拒絕；正在執行的局部操作由 host 排空。</summary>
        private void ThrowIfDisposed()
        {
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ActivityService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
        }

        /// <summary>冪等停止新操作並清除借用參照；不呼叫外部 SDK／logger 的 Dispose，不建立等待或背景清理。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
            GC.SuppressFinalize(this);
        }
    }
}
