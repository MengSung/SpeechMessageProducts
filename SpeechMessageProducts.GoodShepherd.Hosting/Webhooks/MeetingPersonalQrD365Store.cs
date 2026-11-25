/*
 * 檔案整體說明
 * 目的與定位：好牧人 Hosting 的 P5-09 schema2 圖片工作 Dataverse adapter，補上
 * 無 church lookup 的 meeting 權威歸屬鏈與 personal 原子防重附件；這是 scoped SDK 借用者，不自行連線。
 * 主要流程：meeting→完整出席列→contact.parentcustomerid(account)，每頁一百筆、
 * 最多一百頁／一萬筆；正式 ID 重複、缺 cookie、無來源、多教會、缺版本全部拒絕。
 * 取得每條出席關聯、每個 contact 與 meeting 的 RowVersion，全部同交易 CAS 後
 * 一次提交三十八 URL；每欄仍重驗 metadata 五百字元上限，交易最多一千 requests，
 * 超限明確拒絕，不截斷來源。
 * personal 重讀 fullname／parentcustomerid，contact 歸屬 CAS 與固定 annotation ID
 * 的 Create 同交易。已存在只在逐欄相同時以版本 guard 保護；並行 Create 衝突往外拋，
 * 由原有工作重送再讀回，絕不建立另一附件或覆寫不同內容。
 * 資源與隔離：SDK 由每筆 async scope 唯一擁有；本檔同步排空，不建立 Task.Run、
 * timer 或快取。候選只保存本次正式 ID、版本與必要純值；最多一千 guard，生命期到
 * Commit 返回及 scope 釋放。不保存 HttpContext、Session、token 或其他使用者狀態。
 * 失敗與驗證：SDK 異常保留原物件交 worker，正常取消只由 caller token 判定；
 * 交易成功不因後到取消偽報失敗。no-op CAS 會觸發 Update Plugin，部署驗收必須核對
 * optimistic concurrency 與遞迴行為。CAS 保護已讀列，並非範圍鎖；新增來源列的
 * phantom 必須由來源寫入端同交易推進 meeting 版本，否則不能宣稱跨新增的快照隔離。
 * 離線故障測試驗證請求與所有既存列版本，不把合成交易模型當成遠端實機證據。
 */
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;

/// <summary>單筆 schema2 QR 的 SDK 授權與交易適配器，不擴張共用 Outbound contract。</summary>
public static partial class GoodShepherdWebhookRegistration
{
    /// <summary>本次 scoped SDK 的 schema2 授權與交易來源讀取器；SDK owner 仍為 queue scope。</summary>
    /// <param name="sdk">本次工作 scope 擁有的 Dataverse SDK，不由 store Dispose 或跨工作保存。</param>
    private sealed class MeetingPersonalQrD365Store(IOrganizationService sdk) : IMeetingPersonalQrPublicationStore
    {
        private const string MeetingLookup = "new_meeting_statistics_new_present_re";
        private const string ContactLookup = "new_contact_new_present_record";

        /// <summary>完成來源及容量驗證才回傳候選；沒有授權只讀不寫，不產圖。</summary>
        public IMeetingPersonalQrCommit Authorize(Guid churchId, MeetingPersonalQrPayload payload,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (churchId == Guid.Empty) throw new InvalidOperationException("QR church identity is required.");
            token.ThrowIfCancellationRequested();
            if (payload.Kind == "personal")
            {
                var contact = Read("contact", payload.RecordId, new ColumnSet("parentcustomerid", "fullname", "versionnumber"), token);
                ValidateChurch(contact, churchId);
                var name = ReadName(contact);
                return new MeetingPersonalQrD365Commit(sdk, payload, name,
                    new[] { Guard(contact, "parentcustomerid") });
            }

            var meeting = Read("new_meeting_statistics", payload.RecordId, new ColumnSet("versionnumber"), token);
            var rows = ReadAllPresents(payload.RecordId, token);
            if (rows.Count == 0) throw new InvalidOperationException("Meeting QR has no authoritative ownership source.");
            var contactIds = new HashSet<Guid>();
            foreach (var row in rows) contactIds.Add(Lookup(row, ContactLookup, "contact"));
            // 來源已完整讀完才判斷交易容量，不能只保留可容納的前 N 筆。
            if (1 + rows.Count + contactIds.Count > 1000)
                throw new InvalidOperationException("Meeting QR authorization exceeds atomic transaction capacity.");
            var guards = new List<Entity>(rows.Count + contactIds.Count);
            foreach (var row in rows) guards.Add(Guard(row, MeetingLookup, ContactLookup));
            foreach (var contactId in contactIds)
            {
                var contact = Read("contact", contactId, new ColumnSet("parentcustomerid", "versionnumber"), token);
                ValidateChurch(contact, churchId);
                guards.Add(Guard(contact, "parentcustomerid"));
            }
            return new MeetingPersonalQrD365Commit(sdk, payload, "", guards.AsReadOnly(), meeting.RowVersion);
        }

        /// <summary>完整有界來源；每頁都驗正式 ID、版本、精確 meeting lookup，拒絕缺頁與重複。</summary>
        private IReadOnlyList<Entity> ReadAllPresents(Guid meetingId, CancellationToken token)
        {
            var query = new QueryExpression("new_present_record")
            {
                ColumnSet = new ColumnSet("new_present_recordid", MeetingLookup, ContactLookup, "versionnumber"),
                PageInfo = new PagingInfo { Count = 100, PageNumber = 1 }
            };
            query.Criteria.AddCondition(MeetingLookup, ConditionOperator.Equal, meetingId);
            query.AddOrder("new_present_recordid", OrderType.Ascending);
            var ids = new HashSet<Guid>();
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<Entity>();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = sdk.RetrieveMultiple(query);
                if (page is null || page.Entities.Count > 100)
                    throw new InvalidOperationException("Invalid meeting QR source page.");
                foreach (var row in page.Entities)
                {
                    ValidateRow(row, "new_present_record", row?.Id ?? Guid.Empty);
                    if (!ids.Add(row!.Id) || rows.Count == 10000 ||
                        Lookup(row, MeetingLookup, "new_meeting_statistics") != meetingId)
                        throw new InvalidOperationException("Meeting QR source identity conflicts.");
                    _ = Lookup(row, ContactLookup, "contact");
                    // 複製最小 guard，避免 service／替身回傳 Entity 後被其他持有者修改。
                    rows.Add(Guard(row, MeetingLookup, ContactLookup));
                }
                if (!page.MoreRecords) return rows.AsReadOnly();
                if (query.PageInfo.PageNumber == 100 || page.Entities.Count == 0 ||
                    string.IsNullOrWhiteSpace(page.PagingCookie) || page.PagingCookie.Length > 16384 ||
                    !cookies.Add(page.PagingCookie))
                    throw new InvalidOperationException("Meeting QR source paging is incomplete.");
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        /// <summary>精確主鍵最小投影；缺版本不得回退到無條件 Update。</summary>
        private Entity Read(string table, Guid id, ColumnSet columns, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var row = sdk.Retrieve(table, id, columns);
            ValidateRow(row, table, id);
            return row;
        }

        /// <summary>固定表名、正式 ID、無替代鍵與有界版本共同構成來源身分。</summary>
        private static void ValidateRow(Entity? row, string table, Guid id)
        {
            if (row is null || id == Guid.Empty || row.Id != id || row.LogicalName != table ||
                row.KeyAttributes.Count != 0 || string.IsNullOrWhiteSpace(row.RowVersion) ||
                row.RowVersion.Length > 128 ||
                (row.Attributes.TryGetValue(table + "id", out var key) && (key is not Guid guid || guid != id)))
                throw new InvalidOperationException("Invalid QR source identity or version.");
        }

        /// <summary>lookup 只能是精確正式主鍵；名稱與 caller routing 都不是授權。</summary>
        private static Guid Lookup(Entity row, string column, string table) =>
            row.Attributes.TryGetValue(column, out var value) && value is EntityReference reference &&
            reference.Id != Guid.Empty && reference.LogicalName == table && reference.KeyAttributes.Count == 0
                ? reference.Id : throw new InvalidOperationException("Invalid QR source ownership.");

        /// <summary>contact 自身 account 歸屬必須與待辦教會一致，其他 account 不可 fallback。</summary>
        private static void ValidateChurch(Entity contact, Guid churchId)
        {
            if (Lookup(contact, "parentcustomerid", "account") != churchId)
                throw new InvalidOperationException("QR church authorization failed.");
        }

        /// <summary>缺姓名保留舊空尾欄語意，錯型別或超長必須拒絕，不能回顯個資。</summary>
        private static string ReadName(Entity contact)
        {
            if (!contact.Attributes.TryGetValue("fullname", out var value) || value is null) return "";
            return value is string text && text.Length <= 1024 ? text :
                throw new InvalidOperationException("Invalid personal QR source name.");
        }

        /// <summary>複製最小 no-op CAS patch，不保留 SDK Entity 或可變 lookup 參照。</summary>
        private static Entity Guard(Entity source, params string[] columns)
        {
            var guard = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
            foreach (var column in columns)
            {
                var value = (EntityReference)source[column];
                guard[column] = new EntityReference(value.LogicalName, value.Id);
            }
            return guard;
        }
    }

    /// <summary>本筆授權的單交易候選；版本與必要欄位只留到本次工作結束，不跨 scope 重用。</summary>
    /// <param name="sdk">由本次工作 scope 擁有的 Dataverse SDK。</param>
    /// <param name="payload">已驗證的單筆 meeting 或 personal QR 工作純值。</param>
    /// <param name="fullName">personal 工作中由權威 contact 讀取的顯示名稱。</param>
    /// <param name="guards">授權時複製的來源列及版本候選。</param>
    /// <param name="meetingVersion">meeting 工作的來源版本；personal 工作不使用。</param>
    private sealed class MeetingPersonalQrD365Commit(IOrganizationService sdk, MeetingPersonalQrPayload payload,
        string fullName, IReadOnlyList<Entity> guards, string? meetingVersion = null) : IMeetingPersonalQrCommit
    {
        /// <summary>已由 contact 讀取及驗型別的姓名，不含在待辦 payload 中。</summary>
        public string FullName => fullName;

        /// <summary>重建完整寫入候選，所有關聯版本與資料同交易；任一失敗不重試或部分提交。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, byte[]? personalPng, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(urls);
            token.ThrowIfCancellationRequested();
            var requests = new OrganizationRequestCollection();
            foreach (var guard in guards)
            {
                var target = new Entity(guard.LogicalName, guard.Id) { RowVersion = guard.RowVersion };
                foreach (var (key, value) in guard.Attributes)
                {
                    var reference = (EntityReference)value;
                    target[key] = new EntityReference(reference.LogicalName, reference.Id);
                }
                requests.Add(Cas(target));
            }
            if (payload.Kind == "meeting")
            {
                if (urls.Count != 38 || personalPng is not null || string.IsNullOrWhiteSpace(meetingVersion))
                    throw new InvalidOperationException("Invalid meeting QR commit candidate.");
                var plan = MeetingPersonalQrPlan.Create(payload,
                    new MeetingPersonalQrOptions("https://validation.invalid/?QrCodeId=",
                        "https://validation.invalid/?QrCodeId="), "");
                var meeting = new Entity("new_meeting_statistics", payload.RecordId) { RowVersion = meetingVersion };
                foreach (var descriptor in plan)
                {
                    if (!urls.TryGetValue(descriptor.FieldName, out var url) ||
                        string.IsNullOrWhiteSpace(url) || url.Length > 500 ||
                        !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                        uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                        throw new InvalidOperationException("Invalid meeting QR URL candidate.");
                    meeting[descriptor.FieldName] = url;
                }
                requests.Add(Cas(meeting));
            }
            else
            {
                if (urls.Count != 0 || personalPng is null || personalPng.Length is < 8 or > 512 * 1024 ||
                    !personalPng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidOperationException("Invalid personal QR PNG candidate.");
                var annotation = new Entity("annotation", payload.AnnotationId);
                annotation["objectid"] = new EntityReference("contact", payload.RecordId);
                annotation["objecttypecode"] = "contact";
                annotation["isdocument"] = true;
                annotation["subject"] = fullName + "QR Code";
                annotation["notetext"] = fullName + "QR Code";
                annotation["filename"] = $"{payload.RecordId:N}_QRCode.png";
                annotation["mimetype"] = "image/png";
                annotation["documentbody"] = Convert.ToBase64String(personalPng);
                requests.Add(CreateOrGuardAnnotation(annotation, token));
            }
            if (requests.Count > 1000)
                throw new InvalidOperationException("QR commit exceeds atomic transaction capacity.");
            token.ThrowIfCancellationRequested();
            if (sdk.Execute(new ExecuteTransactionRequest { Requests = requests, ReturnResponses = false })
                is not ExecuteTransactionResponse)
                throw new InvalidOperationException("Invalid QR transaction response.");
        }

        /// <summary>精確讀固定附件 ID；存在時逐欄比對及 CAS，未知／衝突內容不覆寫、不另建附件。</summary>
        private OrganizationRequest CreateOrGuardAnnotation(Entity candidate, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var query = new QueryExpression("annotation")
            {
                ColumnSet = new ColumnSet(candidate.Attributes.Keys.Concat(new[] { "versionnumber" }).ToArray()),
                TopCount = 2
            };
            query.Criteria.AddCondition("annotationid", ConditionOperator.Equal, candidate.Id);
            var result = sdk.RetrieveMultiple(query);
            if (result is null || result.MoreRecords || result.Entities.Count > 1)
                throw new InvalidOperationException("Personal QR annotation identity conflicts.");
            if (result.Entities.Count == 0) return new CreateRequest { Target = candidate };
            var existing = result.Entities[0];
            if (existing.Id != candidate.Id || existing.LogicalName != "annotation" ||
                existing.KeyAttributes.Count != 0 || string.IsNullOrWhiteSpace(existing.RowVersion))
                throw new InvalidOperationException("Personal QR annotation version is missing.");
            foreach (var (key, expected) in candidate.Attributes)
            {
                if (!existing.Attributes.TryGetValue(key, out var actual) || !SameValue(expected, actual))
                    throw new InvalidOperationException("Personal QR annotation content conflicts.");
            }
            candidate.RowVersion = existing.RowVersion;
            return Cas(candidate);
        }

        /// <summary>lookup 比對包含表名與非空主鍵，名稱或替代鍵不能使錯誤附件通過。</summary>
        private static bool SameValue(object expected, object actual) =>
            expected is EntityReference left
                ? actual is EntityReference right && left.Id == right.Id &&
                    left.LogicalName == right.LogicalName && right.KeyAttributes.Count == 0
                : Equals(expected, actual);

        /// <summary>每個來源 guard 與最終目標一律使用 RowVersion，不允許預設覆寫。</summary>
        private static UpdateRequest Cas(Entity target) =>
            new() { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches };
    }
}
