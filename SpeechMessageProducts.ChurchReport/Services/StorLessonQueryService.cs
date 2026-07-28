// ============================================================================
// 檔案：ChurchReport/Services/StorLessonQueryService.cs
// 目的：上課紀錄（stor lessons）查詢，可切換 Package 1 no-SDK 或舊 ToolUtility。
//
// 保母教學：
// 1. Package01 啟用時：
//    - 依 contact 走 lessons.stor.retrieve.by.contact
//    - 依 discipleLesson 走 lessons.stor.retrieve.by.disciplelesson
// 2. 舊呼叫端若仍需要 Entity / EntityCollection，可先拿 ID 再 RetrieveEntity 補齊。
//    這仍比整段 FetchXML 綁 SDK 好，且 list 查詢已 no-SDK。
// 3. Package01 關閉時，行為與舊 RetrieveStorLessons* 完全一致。
// 4. 這不是 per-user CRM session pool。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// 上課紀錄查詢投影（控制器 / 服務共用）。
    /// </summary>
    public sealed class StorLessonProjection
    {
        public string StorLessonsEntityId { get; init; } = string.Empty;
        public Guid? DiscipleLessonId { get; init; }
        public Guid? ContactId { get; init; }
        public string DiscipleLessonsName { get; init; } = string.Empty;
        public bool CurrentComplete { get; init; }
        public DateTime DiscipleLessonsDateTime { get; init; }
        public string StageName { get; init; } = string.Empty;
        public string? ContactMobile { get; init; }
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
        /// 是否啟用 Package 1 路徑。
        /// </summary>
        public bool IsPackage01Enabled => _package01Enabled;

        /// <summary>
        /// 依聯絡人讀取上課紀錄投影。
        /// </summary>
        public IReadOnlyList<StorLessonProjection> GetByContact(string? contactName, string contactId)
        {
            if (string.IsNullOrWhiteSpace(contactId) || !Guid.TryParse(contactId, out var contactGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            if (_package01Enabled)
            {
                return GetByContactViaPackage01(contactName, contactGuid);
            }

            return GetByContactViaLegacy(contactName ?? string.Empty, contactId);
        }

        /// <summary>
        /// 依 disciple lesson 讀取上課紀錄投影。
        /// </summary>
        public IReadOnlyList<StorLessonProjection> GetByDiscipleLesson(string? lessonName, string discipleLessonId)
        {
            if (string.IsNullOrWhiteSpace(discipleLessonId) ||
                !Guid.TryParse(discipleLessonId, out var lessonGuid))
            {
                return Array.Empty<StorLessonProjection>();
            }

            if (_package01Enabled)
            {
                return GetByDiscipleLessonViaPackage01(lessonName, lessonGuid);
            }

            return GetByDiscipleLessonViaLegacy(lessonName ?? string.Empty, discipleLessonId);
        }

        /// <summary>
        /// 依 contact 回傳 EntityCollection，供舊 Process* / Equipment 路徑沿用。
        /// Package01 啟用時：先 no-SDK 取 ID，再 RetrieveEntity 組成 collection。
        /// </summary>
        public EntityCollection GetEntityCollectionByContact(string? contactName, string contactId)
        {
            var projections = GetByContact(contactName, contactId);
            return ToEntityCollection(projections);
        }

        /// <summary>
        /// 依 disciple lesson 回傳 EntityCollection，供舊 Process* 路徑沿用。
        /// Package01 啟用時：先 no-SDK 取 ID，再 RetrieveEntity 組成 collection。
        /// </summary>
        public EntityCollection GetEntityCollectionByDiscipleLesson(string? lessonName, string discipleLessonId)
        {
            var projections = GetByDiscipleLesson(lessonName, discipleLessonId);
            return ToEntityCollection(projections);
        }

        /// <summary>
        /// 找「某聯絡人在某課程」的 stor-lesson Id。
        /// 對應舊 4 參數 RetrieveStorLessonsByFetchXml(lessonName, lessonId, userName, userId)。
        /// </summary>
        public Guid? FindStorLessonId(string? lessonName, string discipleLessonId, string? contactName, string contactId)
        {
            if (!Guid.TryParse(discipleLessonId, out var lessonGuid) ||
                !Guid.TryParse(contactId, out var contactGuid))
            {
                return null;
            }

            if (_package01Enabled)
            {
                // 先用 contact 縮小結果，再比對 discipleLessonId。
                var rows = GetByContactViaPackage01(contactName, contactGuid);
                var hit = rows.FirstOrDefault(r =>
                    r.DiscipleLessonId == lessonGuid &&
                    !string.IsNullOrWhiteSpace(r.StorLessonsEntityId) &&
                    Guid.TryParse(r.StorLessonsEntityId, out _));

                if (hit is not null && Guid.TryParse(hit.StorLessonsEntityId, out var id))
                {
                    return id;
                }

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

        private IReadOnlyList<StorLessonProjection> GetByContactViaPackage01(string? contactName, Guid contactId)
        {
            var profileAlias = RequireProfileAlias();
            IReadOnlyList<StorLessonRecordDto> rows = _package01!
                .RetrieveStorLessonsByContactAsync(
                    profileAlias,
                    WorkloadSubjectId,
                    contactId,
                    contactName)
                .GetAwaiter()
                .GetResult();

            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-P01] byContact ContactId={contactId} Name={contactName} Returned={rows.Count}");

            return MapDtos(rows, defaultDiscipleLessonId: null);
        }

        private IReadOnlyList<StorLessonProjection> GetByDiscipleLessonViaPackage01(string? lessonName, Guid discipleLessonId)
        {
            var profileAlias = RequireProfileAlias();
            IReadOnlyList<StorLessonRecordDto> rows = _package01!
                .RetrieveStorLessonsByDiscipleLessonAsync(
                    profileAlias,
                    WorkloadSubjectId,
                    discipleLessonId,
                    lessonName)
                .GetAwaiter()
                .GetResult();

            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-P01] byDiscipleLesson LessonId={discipleLessonId} Name={lessonName} Returned={rows.Count}");

            return MapDtos(rows, defaultDiscipleLessonId: discipleLessonId);
        }

        private IReadOnlyList<StorLessonProjection> GetByContactViaLegacy(string contactName, string contactId)
        {
            EntityCollection storLessons = _utility.RetrieveStorLessonsByFetchXml(contactName, contactId);
            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-LEGACY] byContact ContactId={contactId} Name={contactName} Returned={storLessons?.Entities?.Count ?? 0}");
            return MapEntities(storLessons);
        }

        private IReadOnlyList<StorLessonProjection> GetByDiscipleLessonViaLegacy(string lessonName, string discipleLessonId)
        {
            EntityCollection storLessons = _utility.RetrieveStorLessonsByDiscipleLessonsFetchXml(
                lessonName,
                discipleLessonId);
            System.Diagnostics.Trace.WriteLine(
                $"[STORLESSON-LEGACY] byDiscipleLesson LessonId={discipleLessonId} Name={lessonName} Returned={storLessons?.Entities?.Count ?? 0}");
            return MapEntities(storLessons);
        }

        private IReadOnlyList<StorLessonProjection> MapDtos(
            IReadOnlyList<StorLessonRecordDto> rows,
            Guid? defaultDiscipleLessonId)
        {
            var list = new List<StorLessonProjection>(rows.Count);
            foreach (var row in rows)
            {
                var stageName = string.Empty;
                var classStart = DateTime.MinValue;
                var discipleId = row.DiscipleLessonId ?? defaultDiscipleLessonId;

                if (discipleId is Guid dId && dId != Guid.Empty)
                {
                    try
                    {
                        var discipleLesson = _utility.RetrieveEntity("new_disciple_lessons", dId);
                        if (discipleLesson is not null)
                        {
                            classStart = _utility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date").ToLocalTime();
                            stageName = _utility.GetEntityStringAttribute(ref discipleLesson, "new_now_stage_name") ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[STORLESSON-P01] disciple lesson enrich failed: {ex.Message}");
                    }
                }

                list.Add(new StorLessonProjection
                {
                    StorLessonsEntityId = row.StorLessonId?.ToString() ?? string.Empty,
                    DiscipleLessonId = discipleId,
                    ContactId = row.ContactId,
                    DiscipleLessonsName = row.DiscipleLessonName ?? string.Empty,
                    CurrentComplete = row.CurrentComplete ?? false,
                    DiscipleLessonsDateTime = classStart,
                    StageName = stageName,
                    ContactMobile = row.ContactMobile,
                    ContactName = row.ContactName
                });
            }

            return list;
        }

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