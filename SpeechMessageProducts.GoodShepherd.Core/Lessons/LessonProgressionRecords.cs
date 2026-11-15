/*
 * 檔案整體說明
 * 目的與定位：P5-07的CRM欄位適配器，從本人正式教會account讀取版本化
 * 進階模式，套用到重新讀取的正式課程記錄。由Plugin以當次使用者SDK呼叫，不用
 * 教會名稱、組織代碼或顯示名稱選擇規則，不引用合成門檻LessonProgressionRule。
 * 主要功能與流程：來源→本人contact→parentcustomerid驗證教會，再核對課程
 * 主要聯絡人的教會；取得最多256筆本人歷程，按建立時間排序、完整驗證ID。
 * 階段模式使用最新同階設定，保留舊前階、同階既有通過及下階傳遞、區名與
 * Create教會回寫。學分模式Create複製既有欄位，Update保留歷史設定再計算。
 * 使用邊界與維護：SDK只借用，所有Entity、字典及候選更新只活於同步Run；
 * 先完成全部讀取、授權及計算才寫入，需同步PostOperation由平台交易回滾。
 * 無cache、credential、Session、背景工作或全域可變狀態；SDK故障原樣拋出，
 * 不重試不吞錯。影子服務由外殼擁有及清理，本檔不Dispose外借服務。
 * 學籍卡先驗證本人關聯，產生五值純投影交給當次外殼，再觸碰原卡；不猜測
 * 未取得來源的下游彙總公式。phase5須部署account模式與new_saved_flag兩欄。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using static SpeechMessageProducts.GoodShepherd.Core.Lessons.LessonProgressionEvaluation;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>既有欄位適配器的固定模式，由正式account設定提供而不是資料內容猜測。</summary>
public enum LessonProgressionMode
{
    /// <summary>跨同階歷程合併時數、必修條件並傳遞下階。</summary>
    StageHistory,
    /// <summary>依歷史排課及權重計算預估學分與總分。</summary>
    CreditScore
}

/// <summary>同步操作範圍內的CRM課程進階適配器；沒有需要長期持有的服務或集合。</summary>
public static class LessonProgressionRecords
{
    /// <summary>
    /// 在借用SDK的目前使用者權限下重新驗證來源與教會設定，完整組候選後寫入。
    /// 呼叫端必須使用同步PostOperation，禁止以非同步步驟繞過多列交易保證。
    /// 學籍卡投影回呼只可在本次同步範圍消費，不得保留SDK或跨呼叫共用資料。
    /// </summary>
    public static void Run(IOrganizationService service, Guid sourceId, bool creating,
        Action<LessonProgressionRollCardProjection>? publishRollCard = null)
    {
#if NETFRAMEWORK
        if (service is null) throw new ArgumentNullException(nameof(service));
#else
        ArgumentNullException.ThrowIfNull(service);
#endif
        if (sourceId == Guid.Empty) throw new InvalidOperationException("Lesson progression source is invalid.");
        var source = Read(service, "new_stor_lessons", sourceId, StorColumns());
        var memberId = Lookup(source, "new_contact_new_stor_lessons", "contact");
        var member = Read(service, "contact", memberId, "parentcustomerid", "new_cell_list_contact");
        var configuredChurchId = Lookup(member, "parentcustomerid", "account");
        VerifyOptionalChurch(source, configuredChurchId);
        var account = Read(service, "account", configuredChurchId, "new_lessonprogressionprofile");
        var mode = Text(account, "new_lessonprogressionprofile") switch
        {
            "stage-history-v1" => LessonProgressionMode.StageHistory,
            "credit-score-v1" => LessonProgressionMode.CreditScore,
            _ => throw new InvalidOperationException("Lesson progression account profile is missing or unsupported.")
        };
        // 兩套按鈕契約不同：階段模式有欄位便略過，學分模式只有指定前綴略過。
        if (source.Contains("new_saved_flag") &&
            (mode == LessonProgressionMode.StageHistory ||
             Text(source, "new_saved_flag").StartsWith(LessonText.Get("Lessons.Progression.SavedGridPrefix"), StringComparison.Ordinal))) return;
        var courseCache = new Dictionary<Guid, Entity>();
        var sourceCourse = Course(service, Lookup(source, "new_new_disciple_lessons_new_stor_les",
            "new_disciple_lessons"), configuredChurchId, courseCache);
        var candidate = Copy(source);
        if (mode == LessonProgressionMode.CreditScore)
        {
            if (creating) CopyCreditSettings(candidate, sourceCourse);
            var result = Credits(candidate, Text(sourceCourse, "new_name"));
            candidate["new_estimated_credit"] = result.EstimatedCredit;
            candidate["new_score"] = result.Score;
            Entity? card = null;
            LessonProgressionRollCardProjection? projection = null;
            if (OptionalLookup(source, "new_roll_card_new_stor_lessons", "new_roll_card") is { } cardId)
            {
                card = Read(service, "new_roll_card", cardId, "new_contact_student_roll_card");
                if (Lookup(card, "new_contact_student_roll_card", "contact") != memberId)
                    throw new InvalidOperationException("Lesson progression roll-card member scope mismatch.");
                projection = new LessonProgressionRollCardProjection(sourceId,
                    candidate.GetAttributeValue<OptionSetValue>("new_classification")?.Value,
                    Double(candidate, "new_achieved_credit"), result.EstimatedCredit);
                if (publishRollCard is null)
                    throw new InvalidOperationException("Lesson progression roll-card publication is required.");
            }
            // 所有CRM讀取與投影驗證先完成；發布或Update故障由同步平台交易回滾。
            if (projection is not null) publishRollCard!(projection);
            service.Update(Patch(source, candidate));
            if (card is not null) service.Update(card);
            return;
        }

        var query = new QueryExpression("new_stor_lessons") { ColumnSet = new ColumnSet(StorColumns()), TopCount = 257 };
        query.Criteria.AddCondition("new_contact_new_stor_lessons", ConditionOperator.Equal, memberId);
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("new_stor_lessonsid", OrderType.Ascending);
        var collection = service.RetrieveMultiple(query);
        if (collection is null || collection.MoreRecords || collection.Entities.Count > 256)
            throw new InvalidOperationException("Lesson progression history capacity exceeded.");
        var rows = new List<Entity>(collection.Entities.Count);
        var ids = new HashSet<Guid>();
        foreach (var row in collection.Entities)
        {
            Identity(row, "new_stor_lessons");
            if (!ids.Add(row.Id)) throw new InvalidOperationException("Lesson progression identity conflict.");
            if (Lookup(row, "new_contact_new_stor_lessons", "contact") != memberId)
                throw new InvalidOperationException("Lesson progression member scope mismatch.");
            VerifyOptionalChurch(row, configuredChurchId);
            var courseId = Lookup(row, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons");
            var course = Course(service, courseId, configuredChurchId, courseCache);
            var detached = Copy(row);
            detached["new_new_disciple_lessons_new_stor_les"] =
                new EntityReference("new_disciple_lessons", courseId) { Name = Text(course, "new_name") };
            rows.Add(detached);
        }
        if (!ids.Contains(sourceId)) throw new InvalidOperationException("Lesson progression source is missing from its history.");
        var patches = new Dictionary<Guid, Entity>();
        Previous(candidate, sourceCourse, rows, courseCache);
        Current(candidate, sourceCourse, rows, courseCache, patches);
        if (creating) candidate["new_account_new_stor_lessons"] = new EntityReference("account", configuredChurchId);
        var listId = OptionalLookup(member, "new_cell_list_contact", "list");
        // 舊SetAreaName缺小組時仍寫空字串，不能自行補入提示文字污染既有資料。
        candidate["new_area_name"] = listId is { } id
            ? Text(Read(service, "list", id, "new_area_name"), "new_area_name") : string.Empty;
        if (!creating)
        {
            var count = 0;
            var effective = 0;
            for (var i = 1; i <= 30; i++)
            {
                if (Bool(source, "new_" + Number(i) + "_present")) count++;
                if (Text(sourceCourse, "new_l" + Number(i) + "_name").Length != 0) effective++;
            }
            candidate["new_singon_number"] = count;
            // 原碼先float相除再轉decimal，SDK Double欄位在邊界使用double。
            if (effective != 0) candidate["new_present_rate"] = (double)(decimal)((float)count / effective);
        }
        var sourcePatch = Patch(source, candidate);
        // 舊版先更新下階再寫來源，重疊時來源最後評估的前階結果優先。
        if (patches.TryGetValue(sourceId, out var overlapping))
            foreach (var field in overlapping.Attributes)
                if (!candidate.Contains(field.Key)) sourcePatch[field.Key] = field.Value;
        patches[sourceId] = sourcePatch;
        // 所有讀取、驗證及計算均在寫入之前；平台同步交易負責SDK中途失敗時回滾。
        foreach (var patch in patches.Values) service.Update(patch);
    }

    /// <summary>前階依完整本人集合計算；沒有前階課名與完全沒有紀錄保留不同舊文字。</summary>
    private static void Previous(Entity target, Entity course, List<Entity> rows, Dictionary<Guid, Entity> courses)
    {
        var keyword = WithoutSpaces(Text(course, "new_previous_class"));
        if (keyword.Length == 0)
        {
            target["new_not_yet"] = LessonText.Get("Lessons.Progression.PreviousNameMissing");
            target["new_previous_next_level_condition"] = true;
            target["new_previous_complete_condition"] = true;
            return;
        }
        var previous = Matching(rows, keyword);
        if (previous.Count == 0) { target["new_not_yet"] = LessonText.Get("Lessons.Progression.PreviousRowsMissing"); return; }
        var result = Stage(courses[Lookup(previous[0], "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons")], previous);
        var enter = result.EnterNext;
        var complete = result.Complete;
        var report = result.ClassReport;
        foreach (var row in previous)
        {
            var name = row.GetAttributeValue<EntityReference>("new_new_disciple_lessons_new_stor_les").Name;
            if (Bool(row, "new_current_enter_next_level"))
            {
                enter = true;
                report += LessonText.Get("Lessons.Progression.Because") + name + LessonText.Get("Lessons.Progression.PreviousEnterSticky");
            }
            if (Bool(row, "new_current_complete"))
            {
                complete = true;
                report += LessonText.Get("Lessons.Progression.Because") + name + LessonText.Get("Lessons.Progression.PreviousCompleteSticky");
            }
        }
        target["new_previous_next_level_condition"] = enter;
        target["new_previous_complete_condition"] = complete;
        target["new_not_yet"] = BoundedReport(report + "\r\n" + result.HomeworkReport);
    }

    /// <summary>
    /// 本階只更新觸發列，保留其他同階既有通過；下階收到原始重新評估結果，
    /// 不是同階sticky覆寫結果。這是舊版刻意不重寫其他同階記錄的相容契約。
    /// </summary>
    private static void Current(Entity target, Entity course, List<Entity> rows,
        Dictionary<Guid, Entity> courses, Dictionary<Guid, Entity> patches)
    {
        var keyword = WithoutSpaces(Text(course, "new_now_stage_name"));
        if (keyword.Length == 0) { target["new_memo"] = LessonText.Get("Lessons.Progression.CurrentNameMissing"); return; }
        var current = Matching(rows, keyword);
        if (current.Count == 0) { target["new_memo"] = LessonText.Get("Lessons.Progression.CurrentRowsMissing"); return; }
        var result = Stage(courses[Lookup(current[0], "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons")], current);
        var enter = result.EnterNext;
        var complete = result.Complete;
        var homework = result.HomeworkReport;
        foreach (var row in current)
        {
            if (row.Id == target.Id) continue;
            var name = row.GetAttributeValue<EntityReference>("new_new_disciple_lessons_new_stor_les").Name;
            if (Bool(row, "new_current_enter_next_level"))
            {
                enter = true;
                homework += LessonText.Get("Lessons.Progression.Because") + name + LessonText.Get("Lessons.Progression.CurrentEnterSticky");
            }
            if (Bool(row, "new_current_complete"))
            {
                complete = true;
                homework += LessonText.Get("Lessons.Progression.Because") + name + LessonText.Get("Lessons.Progression.CurrentCompleteSticky");
            }
        }
        target["new_current_enter_next_level"] = enter;
        target["new_current_complete"] = complete;
        target["new_memo"] = BoundedReport(result.ClassReport + "\r\n" + homework);
        var keywords = Text(course, "new_nextlevel_class").Split(',');
        if (keywords.Length > 64) throw new InvalidOperationException("Lesson progression stage capacity exceeded.");
        foreach (var nextKeyword in keywords)
        {
            var next = WithoutSpaces(nextKeyword);
            if (next.Length == 0) continue;
            foreach (var row in Matching(rows, next))
            {
                if (!patches.TryGetValue(row.Id, out var patch))
                {
                    patch = new Entity("new_stor_lessons", row.Id);
                    patches.Add(row.Id, patch);
                }
                patch["new_previous_next_level_condition"] = result.EnterNext;
                patch["new_previous_complete_condition"] = result.Complete;
                // 舊傳遞使用已附加sticky說明的文字，但旗標使用raw結果，不能自行統一。
                patch["new_not_yet"] = BoundedReport(result.ClassReport + "\r\n" + homework);
            }
        }
    }

    /// <summary>課名包含判斷只用於舊階段群組，保留每個正式ID，不用名稱去重。</summary>
    private static List<Entity> Matching(List<Entity> rows, string keyword) =>
        rows.Where(row => Contains(row.GetAttributeValue<EntityReference>(
            "new_new_disciple_lessons_new_stor_les").Name ?? string.Empty, keyword)).ToList();

    /// <summary>每次操作最多256個課程快照，每份課程須透過正式主要聯絡人重驗教會。</summary>
    private static Entity Course(IOrganizationService service, Guid id, Guid churchId, Dictionary<Guid, Entity> cache)
    {
        if (cache.TryGetValue(id, out var existing)) return existing;
        if (cache.Count >= 256) throw new InvalidOperationException("Lesson progression course capacity exceeded.");
        var row = Read(service, "new_disciple_lessons", id, CourseColumns());
        var owner = Read(service, "contact", Lookup(row, "new_contact_new_disciple_lessons", "contact"), "parentcustomerid");
        VerifyChurch(owner, churchId);
        cache.Add(id, row);
        return row;
    }

    /// <summary>Create保留舊課程設定複製，包括舊classification取semester行為，不能自行更正。</summary>
    private static void CopyCreditSettings(Entity target, Entity course)
    {
        target["new_elijah"] = Bool(course, "new_elijah_class");
        var student = OptionalLookup(target, "new_roll_card_new_stor_lessons", "new_roll_card") is not null &&
            OptionalLookup(target, "new_registration_form_new_stor_lesson", "new_registration_form") is not null;
        if (student) target["new_god_student"] = true;
        if (student) target["new_fee"] = new Money(400m);
        else if (course.GetAttributeValue<Money>("new_lessons_fee") is { } money && money.Value != -9999m)
            target["new_fee"] = new Money(money.Value);
        for (var i = 1; i <= 15; i++)
        {
            target["new_l" + Number(i) + "_name"] = Text(course, "new_l" + Number(i) + "_name");
            if (course.GetAttributeValue<DateTime?>("new_l" + Number(i) + "_date") is { Year: > 1900 } date)
                target[DateField(i)] = date;
        }
        for (var i = 0; i < 8; i++)
        {
            var field = "new_" + (char)('a' + i) + "_homework" + Number(i + 1);
            target[field] = Text(course, field);
        }
        if (course.GetAttributeValue<OptionSetValue>("new_semester") is { } semester)
        {
            target["new_classification"] = new OptionSetValue(semester.Value);
            target["new_semester"] = new OptionSetValue(semester.Value);
        }
        if (Double(course, "new_credit") >= 0) target["new_credit"] = Double(course, "new_credit");
        foreach (var field in new[] { "new_present", "new_homework", "new_exam", "new_practice" })
            if (Integer(course, field) >= 0) target[field] = Integer(course, field);
    }

    /// <summary>只擷取已核對metadata的欄位，讀取後重驗正式ID，不允許借用服務回錯列。</summary>
    private static Entity Read(IOrganizationService service, string table, Guid id, params string[] columns)
    {
        var row = service.Retrieve(table, id, new ColumnSet(columns));
        Identity(row, table);
        if (row.Id != id) throw new InvalidOperationException("Lesson progression retrieved identity mismatch.");
        return Copy(row);
    }

    /// <summary>建立本次owned SDK快照；只接受本模組欄位所需的純值，未知可變型別拒絕。</summary>
    private static Entity Copy(Entity row)
    {
        var result = new Entity(row.LogicalName, row.Id);
        foreach (var item in row.Attributes)
            result[item.Key] = item.Value switch
            {
                null => null,
                EntityReference reference when reference.KeyAttributes.Count == 0 =>
                    new EntityReference(reference.LogicalName, reference.Id) { Name = reference.Name },
                OptionSetValue option => new OptionSetValue(option.Value),
                Money money => new Money(money.Value),
                string or bool or int or double or decimal or DateTime or Guid => item.Value,
                _ => throw new InvalidOperationException("Lesson progression field type is invalid.")
            };
        return result;
    }

    /// <summary>候選比較只產生變更欄位，避免回寫來源lookup或不相關歷史資料。</summary>
    private static Entity Patch(Entity source, Entity target)
    {
        var patch = new Entity(source.LogicalName, source.Id);
        foreach (var item in target.Attributes)
            if (!source.Attributes.TryGetValue(item.Key, out var old) || !Equals(old, item.Value))
                patch[item.Key] = item.Value;
        return patch;
    }

    /// <summary>必填lookup必須為正式ID與精確目標表，不能以Name或替代鍵代替。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        OptionalLookup(row, field, table) ?? throw new InvalidOperationException("Lesson progression scope is missing.");

    /// <summary>可缺lookup仍驗證存在值的表名與正式身分，杜絕錯誤型別被當缺值。</summary>
    private static Guid? OptionalLookup(Entity row, string field, string table)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is null) return null;
        if (value is EntityReference reference && reference.LogicalName == table &&
            reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0) return reference.Id;
        throw new InvalidOperationException("Lesson progression lookup is invalid.");
    }

    /// <summary>平台重新讀取的contact教會必須與伺服器設定一致，設定本身不能授權資料。</summary>
    private static void VerifyChurch(Entity contact, Guid churchId)
    {
        if (Lookup(contact, "parentcustomerid", "account") != churchId)
            throw new InvalidOperationException("Lesson progression church scope mismatch.");
    }

    /// <summary>既有歷程若保存另一教會則拒絕，不因本人目前教會改變而讀寫舊組織資料。</summary>
    private static void VerifyOptionalChurch(Entity row, Guid churchId)
    {
        if (OptionalLookup(row, "new_account_new_stor_lessons", "account") is { } existing && existing != churchId)
            throw new InvalidOperationException("Lesson progression historical church scope mismatch.");
    }

    /// <summary>所有顯示文字只屬操作候選，超量不截斷且在SDK寫入前拒絕。</summary>
    private static string BoundedReport(string report) => report.Length <= 100000 ? report :
        throw new InvalidOperationException("Lesson progression report capacity exceeded.");

    /// <summary>只移除舊版指定的ASCII空白，兩目標均為Ordinal，不擴張成Trim或Unicode正規化。</summary>
    private static string WithoutSpaces(string value)
    {
#if NETFRAMEWORK
        return value.Replace(" ", string.Empty);
#else
        return value.Replace(" ", string.Empty, StringComparison.Ordinal);
#endif
    }

    /// <summary>本模組需讀的歷程欄位，不包含token、credential、個資文字或未確認schema。</summary>
    private static string[] StorColumns()
    {
        var values = new List<string> { "new_contact_new_stor_lessons", "new_account_new_stor_lessons",
            "new_new_disciple_lessons_new_stor_les", "new_roll_card_new_stor_lessons",
            "new_registration_form_new_stor_lesson", "new_current_complete", "new_current_enter_next_level",
            "new_previous_next_level_condition", "new_previous_complete_condition", "new_memo", "new_not_yet",
            "new_credit", "new_present", "new_homework", "new_exam", "new_practice", "new_estimated_credit",
            "new_score", "new_area_name", "new_singon_number", "new_present_rate", "createdon",
            "new_saved_flag", "new_achieved_credit", "new_classification", "new_fee" };
        for (var i = 1; i <= 30; i++) values.Add("new_" + Number(i) + "_present");
        for (var i = 1; i <= 5; i++) { values.Add("new_finishhomework_date" + Number(i)); values.Add("new_homework" + Number(i)); }
        for (var i = 1; i <= 15; i++) values.Add(DateField(i));
        for (var i = 0; i < 8; i++)
        {
            var prefix = "new_" + (char)('a' + i);
            values.Add(prefix + "_homework" + Number(i + 1));
            values.Add(prefix + "_complete_date"); values.Add(prefix + "_score");
        }
        return values.ToArray();
    }

    /// <summary>課程設定同時保留兩套既有欄位，模式只從正式account設定選取。</summary>
    private static string[] CourseColumns()
    {
        var values = new List<string> { "new_name", "new_contact_new_disciple_lessons", "new_previous_class",
            "new_now_stage_name", "new_nextlevel_class", "new_class_number_fornext_level",
            "new_complete_classes_number", "new_classes_for_next_level", "new_complete_need_classes",
            "new_credit", "new_present", "new_homework", "new_exam", "new_practice", "new_semester",
            "new_elijah_class", "new_lessons_fee" };
        for (var i = 1; i <= 30; i++) values.Add("new_l" + Number(i) + "_name");
        for (var i = 1; i <= 15; i++) values.Add("new_l" + Number(i) + "_date");
        for (var i = 1; i <= 5; i++) values.Add("new_homework" + Number(i));
        for (var i = 0; i < 8; i++) values.Add("new_" + (char)('a' + i) + "_homework" + Number(i + 1));
        return values.ToArray();
    }
}
