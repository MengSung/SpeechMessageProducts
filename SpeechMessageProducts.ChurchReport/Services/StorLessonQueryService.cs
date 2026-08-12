// ============================================================================
// 檔案：ChurchReport/Services/StorLessonQueryService.cs
// 目的：上課紀錄（stor lessons）查詢，可切換 Package 1 no-SDK 或舊 ToolUtility。
//
// 保母教學：
// 1. Package01 啟用時：
//    - 依 contact 走 lessons.stor.retrieve.by.contact
//    - 依 discipleLesson 走 lessons.stor.retrieve.by.disciplelesson
// 2. 僅需畫面 projection 的新路徑全程使用 DTO；仍要求 Entity / EntityCollection 的既有呼叫端
//    維持 legacy-only，直到其 owner capability 能整體遷移，絕不以 RetrieveEntity 補查冒充 no-SDK。
// 3. Package01 關閉時，行為與舊 RetrieveStorLessons* 完全一致。
// 4. 這不是 per-user CRM session pool。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// 上課紀錄查詢投影（控制器 / 服務共用）。每個 instance 只由單一 request 的 DTO mapping
    /// 建立，沒有 CRM SDK wrapper、shared cache、profile、credential 或可釋放資源；屬性皆為 init-only，
    /// 使呼叫端不會在另一個使用者或非同步 continuation 中改寫已建立的結果。
    /// </summary>
    public sealed class StorLessonProjection
    {
        /// <summary>
        /// 上課記錄的顯示用 GUID 字串。無法安全取得 GUID 時為空字串，不能用作 caller-controlled
        /// connector 選擇或跨請求快取索引。
        /// </summary>
        public string StorLessonsEntityId { get; init; } = string.Empty;

        /// <summary>
        /// 關聯門徒課程的純值識別碼。null 保留來源缺失，禁止以 CRM SDK 查詢或前次結果補齊。
        /// </summary>
        public Guid? DiscipleLessonId { get; init; }

        /// <summary>
        /// 關聯聯絡人的純值識別碼。授權與 profile 由既有 server-side composition 處理，本欄位不構成
        /// 可由 controller 或使用者指定的路由 authority。
        /// </summary>
        public Guid? ContactId { get; init; }

        /// <summary>
        /// 門徒課程顯示名稱。資料只活在目前 projection 集合，不能寫入 static／singleton／session cache。
        /// </summary>
        public string DiscipleLessonsName { get; init; } = string.Empty;

        /// <summary>
        /// 是否完成本筆課程。此純值由單一 response 投影，不能被另一筆或另一位使用者的結果覆蓋。
        /// </summary>
        public bool CurrentComplete { get; init; }

        /// <summary>
        /// 顯示用開課本機時間。Package01 的 UTC 值只在目前 request 投影為 local time；無值時保留
        /// <see cref="DateTime.MinValue"/> 以維持舊 UI 合約，絕不另以 SDK 補查或共用快取推測。
        /// </summary>
        public DateTime DiscipleLessonsDateTime { get; init; }

        /// <summary>
        /// 顯示用目前階段名稱。值來自同一 DTO 或 legacy response，不持有 alias wrapper 或 CRM Entity。
        /// </summary>
        public string StageName { get; init; } = string.Empty;

        /// <summary>
        /// 關聯聯絡人的顯示電話。此可選 PII 欄位僅限目前 response，禁止留存在未分割的快取或診斷輸出。
        /// </summary>
        public string? ContactMobile { get; init; }

        /// <summary>
        /// 關聯聯絡人的顯示名稱。此值是 presentation data，不是可讓 caller 改選 profile 或 owner 的依據。
        /// </summary>
        public string? ContactName { get; init; }
    }

    /// <summary>
    /// stor-lesson 查詢服務。
    /// </summary>
    public sealed class StorLessonQueryService
    {
        private const string WorkloadSubjectId = "church-report-service";

        private readonly ToolUtilityClass _utility;
        private readonly IPackage01FeeReadClient? _package01;
        private readonly ProductDynamicsOptions? _options;
        private readonly bool _package01Enabled;

        public StorLessonQueryService(ToolUtilityClass utility, IConfiguration configuration)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            ArgumentNullException.ThrowIfNull(configuration);

            _package01 = DonationDynamicsAccessBootstrap.TryCreatePackage01Client(configuration);
            _package01Enabled = _package01 is not null;
            if (_package01Enabled)
            {
                _options = DonationDynamicsAccessBootstrap.BindOptions(configuration);
            }
        }

        /// <summary>
        /// 建立供組合根與測試使用的 Package01 typed 讀取服務。注入的 client、profile 與 flag 都是
        /// server-owned composition 結果；此建構式不公開接受 endpoint、credential、connector kind 或
        /// caller 指定 owner，因此不會擴張跨 profile/session 的路由權限。utility 仍只屬於 legacy path。
        /// </summary>
        public StorLessonQueryService(
            ToolUtilityClass utility,
            IPackage01FeeReadClient? package01,
            IOptions<ProductDynamicsOptions>? options,
            bool package01FeeReadsEnabled = false)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _package01 = package01;
            _options = options?.Value;
            _package01Enabled = package01FeeReadsEnabled && package01 is not null;
        }

        /// <summary>
        /// 取得由 deployment-owned composition 決定的 Package01 狀態。此唯讀結果只控制本服務的
        /// 既有 read path，不能由 HTTP 呼叫端、query string 或使用者資料覆寫，也不會在存取時建立外部資源。
        /// </summary>
        public bool IsPackage01Enabled => _package01Enabled;

        /// <summary>
        /// 依聯絡人讀取 legacy 上課紀錄投影的同步相容 API。為避免同步阻塞與 cancellation 遺失，
        /// 它刻意永不走 Package01 typed I/O；需要 typed projection 的 controller 必須使用 async API。
        /// legacy Entity 與補查只在此呼叫的同步 scope 存活，資源 owner 仍是既有 ToolUtility。
        /// </summary>
        public IReadOnlyList<StorLessonProjection> GetByContact(string? contactName, string contactId)
        {
            if (string.IsNullOrWhiteSpace(contactId) || !Guid.TryParse(contactId, out var contactGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            return GetByContactViaLegacy(contactName ?? string.Empty, contactId);
        }

        /// <summary>
        /// 以 contact 讀取畫面專用 projection。Package01 開啟時此 API 唯一允許的 typed 分支會 await
        /// ProductClient 並原樣傳遞 request cancellation；關閉時才保留 legacy FetchXML 行為。輸入只可
        /// 是既有控制器已授權的 contact ID，不會成為 profile、endpoint 或 credential 的路由 authority。
        /// </summary>
        public async Task<IReadOnlyList<StorLessonProjection>> GetByContactAsync(
            string? contactName,
            string contactId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(contactId) || !Guid.TryParse(contactId, out var contactGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            if (!_package01Enabled)
            {
                return GetByContactViaLegacy(contactName ?? string.Empty, contactId);
            }

            return await GetByContactViaPackage01Async(contactName, contactGuid, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 依門徒課程讀取 legacy 上課紀錄投影的同步相容 API。它不會因為 feature gate 開啟就偷偷
        /// sync-over-async；仍需要 typed 路徑的 caller 必須改用非同步 API 並傳入自身 cancellation token。
        /// </summary>
        public IReadOnlyList<StorLessonProjection> GetByDiscipleLesson(string? lessonName, string discipleLessonId)
        {
            if (string.IsNullOrWhiteSpace(discipleLessonId) ||
                !Guid.TryParse(discipleLessonId, out var lessonGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            return GetByDiscipleLessonViaLegacy(lessonName ?? string.Empty, discipleLessonId);
        }

        /// <summary>
        /// 以 disciple lesson 讀取 projection 的非同步 typed API。它與 contact API 使用相同的
        /// request-local DTO 投影與取消規則；同步 bridge 維持 legacy-only，避免未遷移 caller 在 flag
        /// 開啟時偷偷經過 sync-over-async 或 SDK Entity 補查。
        /// </summary>
        public async Task<IReadOnlyList<StorLessonProjection>> GetByDiscipleLessonAsync(
            string? lessonName,
            string discipleLessonId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(discipleLessonId) ||
                !Guid.TryParse(discipleLessonId, out var lessonGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            if (!_package01Enabled)
            {
                return GetByDiscipleLessonViaLegacy(lessonName ?? string.Empty, discipleLessonId);
            }

            return await GetByDiscipleLessonViaPackage01Async(lessonName, lessonGuid, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 依聯絡人回傳 EntityCollection 的 legacy-only 相容 API。由於回傳型別本身是 CRM SDK graph，
        /// 本方法永遠不使用 Package01 DTO 重新水化或補查；它只服務尚未遷移的 owner，不能計入 P7.4
        /// migrated consumer，且外部資源生命週期完全由既有 ToolUtility 呼叫範圍管理。
        /// </summary>
        public EntityCollection GetEntityCollectionByContact(string? contactName, string contactId)
        {
            return ToEntityCollection(GetByContactViaLegacy(contactName ?? string.Empty, contactId));
        }

        /// <summary>
        /// 依門徒課程回傳 EntityCollection 的 legacy-only 相容 API。其 SDK 物件只供尚未遷移的既有
        /// caller 使用；不得把這條路徑標記為 typed cutover，也不允許因 feature gate 改變同步資源行為。
        /// </summary>
        public EntityCollection GetEntityCollectionByDiscipleLesson(string? lessonName, string discipleLessonId)
        {
            return ToEntityCollection(GetByDiscipleLessonViaLegacy(lessonName ?? string.Empty, discipleLessonId));
        }

        /// <summary>
        /// 尋找 stor-lesson ID 的 legacy 相容 API。它被寫入相鄰與 EntityCollection consumer 使用，
        /// 因此在 P7.4 期間刻意只走既有 ToolUtility 查詢；不能在 Package01 開啟時偷偷同步等待 typed I/O，
        /// 也不能把 request-local DTO 重新水化成 CRM Entity。後續遷移由其 owner task 明確處理。
        /// </summary>
        public Guid? FindStorLessonId(string? lessonName, string discipleLessonId, string? contactName, string contactId)
        {
            if (!Guid.TryParse(discipleLessonId, out _) ||
                !Guid.TryParse(contactId, out _))
            {
                return null;
            }
            EntityCollection legacy = _utility.RetrieveStorLessonsByFetchXml(
                lessonName ?? string.Empty,
                discipleLessonId,
                contactName ?? string.Empty,
                contactId);

            if (legacy?.Entities is null || legacy.Entities.Count == 0)
            {
                return null;
            }

            return legacy.Entities[0].Id;
        }

        /// <summary>
        /// 執行 contact 的唯一 Package01 typed read。profile alias 只能來自組合根綁定的 options，
        /// cancellation 由目前 request 原樣傳遞；await 完成前不建立 projection，成功後只回傳本次 DTO
        /// 建立的集合，避免 timeout、取消或 fault 留下部分可被另一個 request 觀察的 mutable state。
        /// </summary>
        private async Task<IReadOnlyList<StorLessonProjection>> GetByContactViaPackage01Async(
            string? contactName,
            Guid contactId,
            CancellationToken cancellationToken)
        {
            var profileAlias = RequireProfileAlias();
            IReadOnlyList<StorLessonRecordDto> rows = await _package01!
                .RetrieveStorLessonsByContactAsync(
                    profileAlias,
                    WorkloadSubjectId,
                    contactId,
                    contactName,
                    cancellationToken)
                .ConfigureAwait(false);

            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-P01] byContact ContactId={contactId} Name={contactName} Returned={rows.Count}");

            return MapDtos(rows, defaultDiscipleLessonId: null);
        }

        /// <summary>
        /// 執行門徒課程的唯一 Package01 typed read。方法不接受 endpoint、credential、connector kind
        /// 或 caller 指定 owner；任何 operation fault／取消都由 await 原樣傳回，沒有 legacy fallback、
        /// retry 或 background continuation，因此 client/lease 的清理仍由既有 executor owner 決定。
        /// </summary>
        private async Task<IReadOnlyList<StorLessonProjection>> GetByDiscipleLessonViaPackage01Async(
            string? lessonName,
            Guid discipleLessonId,
            CancellationToken cancellationToken)
        {
            var profileAlias = RequireProfileAlias();
            IReadOnlyList<StorLessonRecordDto> rows = await _package01!
                .RetrieveStorLessonsByDiscipleLessonAsync(
                    profileAlias,
                    WorkloadSubjectId,
                    discipleLessonId,
                    lessonName,
                    cancellationToken)
                .ConfigureAwait(false);

            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-P01] byDiscipleLesson LessonId={discipleLessonId} Name={lessonName} Returned={rows.Count}");

            return MapDtos(rows, defaultDiscipleLessonId: discipleLessonId);
        }

        /// <summary>
        /// 執行 contact 的既有 ToolUtility 查詢並立刻投影為 request-local 資料。此 helper 僅供 feature
        /// gate 關閉與 legacy-only API 使用；它不與 Package01 結果共用集合、cache 或例外狀態。
        /// </summary>
        private IReadOnlyList<StorLessonProjection> GetByContactViaLegacy(string contactName, string contactId)
        {
            EntityCollection storLessons = _utility.RetrieveStorLessonsByFetchXml(contactName, contactId);
            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-LEGACY] byContact ContactId={contactId} Name={contactName} Returned={storLessons?.Entities?.Count ?? 0}");
            return MapEntities(storLessons);
        }

        /// <summary>
        /// 執行門徒課程的既有 ToolUtility 查詢並在同一同步 scope 投影。legacy SDK 實體不會穿越
        /// Package01 boundary；此方法不能作為 feature-on 的 fallback 或 background retry。
        /// </summary>
        private IReadOnlyList<StorLessonProjection> GetByDiscipleLessonViaLegacy(string lessonName, string discipleLessonId)
        {
            EntityCollection storLessons = _utility.RetrieveStorLessonsByDiscipleLessonsFetchXml(
                lessonName,
                discipleLessonId);
            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-LEGACY] byDiscipleLesson LessonId={discipleLessonId} Name={lessonName} Returned={storLessons?.Entities?.Count ?? 0}");
            return MapEntities(storLessons);
        }

        /// <summary>
        /// 將已驗證的 Package01 DTO 一次性投影為畫面資料。集合和每個 projection 均由本次呼叫建立；
        /// 不讀取 ToolUtility、Entity 或 shared cache。日期和階段名稱必須使用同一 DTO 的純值，以確保
        /// A/B 交錯請求不會透過補查或可變集合交叉污染。
        /// </summary>
        private IReadOnlyList<StorLessonProjection> MapDtos(
            IReadOnlyList<StorLessonRecordDto> rows,
            Guid? defaultDiscipleLessonId)
        {
            var list = new List<StorLessonProjection>(rows.Count);
            foreach (var row in rows)
            {
                var discipleId = row.DiscipleLessonId ?? defaultDiscipleLessonId;

                list.Add(new StorLessonProjection
                {
                    StorLessonsEntityId = row.StorLessonId?.ToString() ?? string.Empty,
                    DiscipleLessonId = discipleId,
                    ContactId = row.ContactId,
                    DiscipleLessonsName = row.DiscipleLessonName ?? string.Empty,
                    CurrentComplete = row.CurrentComplete ?? false,
                    DiscipleLessonsDateTime = ToLegacyDisplayDateTime(row.ClassStartDate),
                    StageName = row.StageName ?? string.Empty,
                    ContactMobile = row.ContactMobile,
                    ContactName = row.ContactName
                });
            }

            return list;
        }

        /// <summary>
        /// 將 connector 已正規化的 UTC 開課時間轉為既有畫面使用的本機 <see cref="DateTime"/>。
        /// <c>null</c> 與 UTC 最小值都代表 legacy 未設定日期，必須回傳 <see cref="DateTime.MinValue"/>，
        /// 不能因伺服器正偏移而顯示成凌晨八點等看似有效的時間。對其餘極端值，先依目前時區的
        /// UTC offset 檢查可表示範圍，再呼叫 <see cref="DateTimeOffset.LocalDateTime"/>；此方法不快取
        /// 時區、DTO 或 request 資料，故每筆 projection 均維持 request-local，且不會建立資源或跨使用者狀態。
        /// </summary>
        /// <param name="classStartDate">connector 在本次 DTO response 提供的 nullable UTC 開課時間。</param>
        /// <returns>可安全呈現的本機時間，或 legacy 未設定日期哨兵。</returns>
        private static DateTime ToLegacyDisplayDateTime(DateTimeOffset? classStartDate)
        {
            if (!classStartDate.HasValue || classStartDate.Value.UtcDateTime == DateTime.MinValue)
            {
                return DateTime.MinValue;
            }

            var utcDateTime = classStartDate.Value.UtcDateTime;
            var offset = TimeZoneInfo.Local.GetUtcOffset(utcDateTime);
            if ((offset < TimeSpan.Zero && utcDateTime.Ticks < -offset.Ticks) ||
                (offset > TimeSpan.Zero && utcDateTime.Ticks > DateTime.MaxValue.Ticks - offset.Ticks))
            {
                return offset < TimeSpan.Zero ? DateTime.MinValue : DateTime.MaxValue;
            }

            return classStartDate.Value.LocalDateTime;
        }

        /// <summary>
        /// 將 legacy EntityCollection 投影為相容的畫面資料。SDK Entity 與必要的 legacy 補查都只
        /// 存在於本同步方法；失敗時維持舊行為以空顯示欄位繼續，不把例外、Entity 或 session 狀態
        /// 存到後續 request。這不是 Package01 typed path，不能用來證明 ToolUtility 已移除。
        /// </summary>
        private IReadOnlyList<StorLessonProjection> MapEntities(EntityCollection? storLessons)
        {
            var list = new List<StorLessonProjection>();
            if (storLessons?.Entities is null)
            {
                return list;
            }

            foreach (var lessonEntity in storLessons.Entities)
            {
                var lesson = lessonEntity;
                var discipleLessonId = _utility.GetEntityLookupAttribute(ref lesson, "new_new_disciple_lessons_new_stor_les");
                var contactId = _utility.GetEntityLookupAttribute(ref lesson, "new_contact_new_stor_lessons");
                var classStartDate = DateTime.MinValue;
                var stageName = string.Empty;

                if (discipleLessonId != Guid.Empty)
                {
                    try
                    {
                        var discipleLesson = _utility.RetrieveEntity("new_disciple_lessons", discipleLessonId);
                        classStartDate = _utility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date").ToLocalTime();
                        stageName = _utility.GetEntityStringAttribute(ref discipleLesson, "new_now_stage_name") ?? string.Empty;
                    }
                    catch
                    {
                        // 與舊 controller 行為一致：補欄位失敗不中斷整批。
                    }
                }

                list.Add(new StorLessonProjection
                {
                    StorLessonsEntityId = lesson.Id.ToString(),
                    DiscipleLessonId = discipleLessonId == Guid.Empty ? null : discipleLessonId,
                    ContactId = contactId == Guid.Empty ? null : contactId,
                    DiscipleLessonsName = _utility.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les") ?? string.Empty,
                    CurrentComplete = _utility.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
                    DiscipleLessonsDateTime = classStartDate,
                    StageName = stageName
                });
            }

            return list;
        }

        /// <summary>
        /// 將 legacy projection 重取為 EntityCollection 的相容 helper。此方法只可由 legacy-only API
        /// 呼叫，因為它會進行 CRM SDK Retrieve；失敗資料不快取且逐筆處理後立即交還既有 ToolUtility
        /// owner，避免讓 typed DTO 路徑混入 Entity 或誤宣稱已完成 P7.4 cutover。
        /// </summary>
        private EntityCollection ToEntityCollection(IReadOnlyList<StorLessonProjection> projections)
        {
            var collection = new EntityCollection();
            foreach (var row in projections)
            {
                if (!Guid.TryParse(row.StorLessonsEntityId, out var id) || id == Guid.Empty)
                {
                    continue;
                }

                try
                {
                    var entity = _utility.RetrieveEntity("new_stor_lessons", id);
                    if (entity is not null)
                    {
                        collection.Entities.Add(entity);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[STORLESSON] RetrieveEntity failed for {id}: {ex.Message}");
                }
            }

            return collection;
        }

        /// <summary>
        /// 取得 deployment-owned Package01 profile alias。缺值即在 outbound operation 前 fail-closed，
        /// 不使用 caller name、contact ID 或任何預設 profile 猜測；回傳字串僅用於本次 client 呼叫，
        /// 不會保存 credential、endpoint 或跨 request 的連線狀態。
        /// </summary>
        private string RequireProfileAlias()
        {
            var profileAlias = _options?.ProfileAlias;
            if (string.IsNullOrWhiteSpace(profileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess:ProfileAlias is required when Package01 lesson reads are enabled.");
            }

            return profileAlias;
        }
    }
}
