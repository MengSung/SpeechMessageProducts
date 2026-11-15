/*
 * 檔案整體說明
 * 目的與定位：用真正LessonProgressionPlugin及嚴格SDK替身驗證P5-07設定接線，
 * 避免只測合成門檻卻冒充兩套舊碼相容。reading基準為第一套來源306～846行
 * 的階段傳遞與第二套來源340～898行的學分／權重；沒有執行舊DLL或連接實機。
 * 主要功能與流程：以獨立會友、課程、教會、歷程建立呼叫，驗證同步Live實際
 * 更新欄位及Shadow零真寫入；注入重複ID、跨教會、查詢故障及學籍卡錯配，
 * 決定性斷言完整候選拒絕、原例外保留、同名不同ID保留及重用Plugin不串資料。
 * 使用邊界與維護：Fixture擁有本次Mock、Entity集合與寫入清單，Dispose清空；
 * SDK借用無真連線，並行測試await全部工作後才清理，無timer或未觀察背景工作。
 * 不測LINE、不讀token或credential，不將離線通知測試描述成部署或未知下游彙總驗收。
 * 來源lookup及課程設定均為合成正式ID，測試固定比較真正CRM欄位及純值結果。
 * 三案直接共用Core的scenarios／expected，完整比對變更欄位、舊診斷原文與
 * Shadow安全雜湊；期望來自閱讀來源，不由產品執行輸出重生，也不冒充實機。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>驗證真正設定外殼與既有欄位適配器，所有案例皆為離線reading驗證。</summary>
public sealed class LessonProgressionShellTests
{
    /// <summary>三個共用reading情境經真Plugin完整比對寫入欄位，不能只比較兩個計算旗標。</summary>
    [Fact]
    public void Lessons_Progression_Scenarios_MatchExpected() => AssertReadingCases(false);

    /// <summary>同份reading期望經真影子外殼，逐欄比對安全摘要且真服務零寫入。</summary>
    [Fact]
    public void Lessons_Progression_ShadowMode_MatchesExpected() => AssertReadingCases(true);

    /// <summary>
    /// 每案重建合成正式ID及本地owner，執行後核對完整欄位集合、值及來源不可變性。
    /// 長文字與區名以獨立SHA-256計算核對影子摘要，不以產品投影產生自己的期望；
    /// 所有資料只在本次同步測試保留，Fixture與讀取串流確定清理。
    /// </summary>
    private static void AssertReadingCases(bool shadow)
    {
        var scenarios = ReadProgressionGolden("scenarios");
        var expected = ReadProgressionGolden("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        Assert.Equal(scenarios["cases"]!.Count(), expected["cases"]!.Count());
        foreach (var input in scenarios["cases"]!)
        {
            using var data = new Fixture();
            var output = expected["cases"]!.Single(x => (string?)x["name"] == (string?)input["name"]);
            var seeds = input["seed"]!.ToArray();
            var rows = seeds.ToDictionary(x => (string)x["alias"]!, x => (string)x["alias"]! switch
            {
                "church" => data.Rows[data.Church],
                "member" => data.Member,
                "course" => data.Course,
                "record" => data.Source,
                "record-2" => data.AddHistory(data.Course),
                _ => throw new InvalidOperationException("Unsupported progression alias.")
            }, StringComparer.Ordinal);
            foreach (var seed in seeds)
            {
                var row = rows[(string)seed["alias"]!];
                Assert.Equal((string?)seed["entity"], row.LogicalName);
                row.Attributes.Clear();
                foreach (var field in ((JObject)seed["fields"]!).Properties())
                    row[field.Name] = field.Value.Type switch
                    {
                        JTokenType.Object => new EntityReference((string)field.Value["entity"]!,
                            rows[(string)field.Value["ref"]!].Id),
                        JTokenType.Boolean => (bool)field.Value,
                        JTokenType.Integer => (int)field.Value,
                        JTokenType.Float => (double)field.Value,
                        JTokenType.String when field.Name.Contains("date") => DateTime.Parse(
                            field.Value.Value<string>()!, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind),
                        JTokenType.String => field.Value.Value<string>()!,
                        _ => throw new InvalidOperationException("Unsupported progression field.")
                    };
            }
            var initial = data.Source.Attributes.ToDictionary(x => x.Key, x => x.Value);
            var profile = data.Rows[data.Church].GetAttributeValue<string>("new_lessonprogressionprofile");
            data.Execute(profile, shadow);
            Assert.Equal(initial.Count, data.Source.Attributes.Count);
            foreach (var field in initial) Assert.Equal(field.Value, data.Source[field.Key]);
            var writes = output["writes"]!.ToArray();
            if (!shadow)
            {
                Assert.Equal(writes.Length, data.Writes.Count);
                foreach (var write in writes)
                {
                    var actual = Assert.Single(data.Writes.Where(x => x.Id == rows[(string)write["alias"]!].Id));
                    Assert.Equal((string?)write["entity"], actual.LogicalName);
                    var fields = (JObject)write["fields"]!;
                    Assert.Equal(fields.Properties().Select(x => x.Name).OrderBy(x => x),
                        actual.Attributes.Keys.OrderBy(x => x));
                    foreach (var field in fields.Properties())
                        Assert.True(JToken.DeepEquals(field.Value, JToken.FromObject(actual[field.Name])),
                            "Progression reading mismatch: " + (string?)input["name"] + "/" + field.Name);
                }
                continue;
            }
            Assert.Empty(data.Writes);
            var records = data.Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Assert.True((bool)records.Last()["end"]!);
            Assert.DoesNotContain(records, x => (bool?)x["overflow"] == true);
            Assert.Equal(writes.Length, records.Count(x => x["operation"] is not null));
            foreach (var write in writes)
            {
                var record = Assert.Single(records.Where(x => (string?)x["id"] ==
                    rows[(string)write["alias"]!].Id.ToString("D")));
                Assert.Equal((string?)write["operation"], (string?)record["operation"]);
                Assert.Equal((string?)write["entity"], (string?)record["entity"]);
                var fields = (JObject)write["fields"]!;
                var actual = (JObject)record["fields"]!;
                Assert.Equal(fields.Properties().Select(x => x.Name).OrderBy(x => x),
                    actual.Properties().Select(x => x.Name).OrderBy(x => x));
                foreach (var field in fields.Properties())
                {
                    var value = field.Value;
                    if (value.Type == JTokenType.String &&
                        (value.Value<string>()!.Length > 120 || field.Name == "new_area_name"))
                    {
                        var text = value.Value<string>()!;
                        using var sha = System.Security.Cryptography.SHA256.Create();
                        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                        value = new JObject { ["length"] = text.Length,
                            ["sha256"] = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() };
                    }
                    Assert.True(JToken.DeepEquals(value, actual[field.Name]),
                        "Progression shadow mismatch: " + (string?)input["name"] + "/" + field.Name);
                }
            }
        }
    }

    /// <summary>只讀兩種固定嵌入檔，限制1MiB及JSON深度32；串流由本方法using釋放。</summary>
    private static JObject ReadProgressionGolden(string kind)
    {
        if (kind is not ("scenarios" or "expected")) throw new ArgumentException("Unknown progression fixture.", nameof(kind));
        var assembly = typeof(LessonProgressionShellTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x =>
            x.EndsWith(".lessonsprogression." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        if (stream.Length > 1048576) throw new InvalidOperationException("Progression fixture capacity exceeded.");
        using var text = new System.IO.StreamReader(stream, new System.Text.UTF8Encoding(false, true));
        using var reader = new Newtonsoft.Json.JsonTextReader(text)
            { MaxDepth = 32, DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        return JObject.Load(reader);
    }

    /// <summary>
    /// 05A 禁止沙箱產品依賴 ValueTuple；在離線測試讀取已編譯 Publish 的區域型別，
    /// 包含集合內的泛型參數，防止只看 API 而漏掉暫存候選。此反射僅限測試宿主，
    /// 產品不需反射或擴充沙箱權限；既有五值投影測試另保護重構前後的行為。
    /// </summary>
    [Fact]
    public void Progression_SharedPublication_DoesNotUseValueTuple()
    {
        var invocation = typeof(LessonProgressionPlugin).GetNestedType("Invocation", BindingFlags.NonPublic)!;
        var publish = invocation.GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var body = publish.GetMethodBody()!;
        Assert.DoesNotContain(body.LocalVariables, variable => HasValueTuple(variable.LocalType));

        static bool HasValueTuple(Type type) =>
            type.Namespace == "System" && type.Name.StartsWith("ValueTuple", StringComparison.Ordinal) ||
            type.GenericTypeArguments.Any(HasValueTuple);
    }

    /// <summary>實際Plugin合併本人同階歷程，回寫來源與所有下階正式ID，保留原文字。</summary>
    [Fact]
    public void Lessons_Progression_TwoWingsProfile_ActualPluginUsesLegacyFields()
    {
        using var data = new Fixture();
        data.Course["new_class_number_fornext_level"] = 2;
        data.Course["new_complete_classes_number"] = 3;
        data.Course["new_classes_for_next_level"] = "1,A";
        data.Course["new_complete_need_classes"] = "1,2,3,A";
        data.Source["new_1_present"] = true;
        data.Source["new_finishhomework_date1"] = new DateTime(2026, 1, 1);
        var history = data.AddHistory(data.Course);
        history["new_2_present"] = true;
        var nextCourse = data.AddCourse("下一階課程");
        var next = data.AddHistory(nextCourse);
        data.Execute("stage-history-v1");
        var sourcePatch = Assert.Single(data.Writes.Where(x => x.Id == data.Source.Id));
        Assert.True(sourcePatch.GetAttributeValue<bool>("new_current_enter_next_level"));
        Assert.False(sourcePatch.GetAttributeValue<bool>("new_current_complete"));
        Assert.Equal(1, sourcePatch["new_singon_number"]);
        Assert.Contains("上課時數:2\r\n", sourcePatch.GetAttributeValue<string>("new_memo"), StringComparison.Ordinal);
        Assert.Contains("沒有上第 3 課，\r\n無法結業\r\n", sourcePatch.GetAttributeValue<string>("new_memo"), StringComparison.Ordinal);
        var nextPatch = Assert.Single(data.Writes.Where(x => x.Id == next.Id));
        Assert.True(nextPatch.GetAttributeValue<bool>("new_previous_next_level_condition"));
        Assert.False(nextPatch.GetAttributeValue<bool>("new_previous_complete_condition"));
        Assert.DoesNotContain(data.Writes, x => x.Id == history.Id);
        Assert.False(data.Source.Contains("new_current_complete"));
    }

    /// <summary>第二種真設定使用歷史出席與權重，不能錯接generic門檻或階段結果。</summary>
    [Fact]
    public void Lessons_Progression_SecondProfile_ActualPluginUsesLegacyFields()
    {
        using var data = new Fixture();
        data.Source["new_first_date"] = new DateTime(2026, 1, 1);
        data.Source["new_2_date"] = new DateTime(2026, 1, 2);
        data.Source["new_1_present"] = true;
        data.Source["new_credit"] = 3d;
        data.Source["new_present"] = 20;
        data.Source["new_homework"] = 80;
        data.Source["new_a_homework1"] = "作業";
        data.Source["new_a_complete_date"] = new DateTime(2026, 1, 1);
        data.Source["new_a_score"] = 70d;
        data.Execute("credit-score-v1");
        var update = Assert.Single(data.Writes);
        Assert.Equal(3d, update["new_estimated_credit"]);
        Assert.Equal(76d, update["new_score"]);
        Assert.False(update.Contains("new_current_complete"));
    }

    /// <summary>Create複製真課程日期、名稱、權重及費用；後續Update不覆蓋既有歷史設定。</summary>
    [Fact]
    public void Credits_CreateCopiesCourseSettings_AndUpdateKeepsHistoricalValues()
    {
        using var data = new Fixture();
        data.Context.MessageName = "Create";
        data.Context.Depth = 2;
        data.Course["new_l1_date"] = new DateTime(2026, 1, 1);
        data.Course["new_l1_name"] = "第一堂";
        data.Course["new_credit"] = 4d;
        data.Course["new_present"] = 30;
        data.Course["new_lessons_fee"] = new Money(500m);
        data.Source["new_1_present"] = true;
        data.Execute("credit-score-v1");
        var update = Assert.Single(data.Writes);
        Assert.Equal("第一堂", update["new_l1_name"]);
        Assert.Equal(new DateTime(2026, 1, 1), update["new_first_date"]);
        Assert.Equal(4d, update["new_estimated_credit"]);
        Assert.Equal(30d, update["new_score"]);
        Assert.Equal(500m, update.GetAttributeValue<Money>("new_fee").Value);
    }

    /// <summary>課程缺日期或費用時舊setter不清空來源；不得寫1000年哨兵到CRM日期欄位。</summary>
    [Fact]
    public void Credits_CreateMissingCourseDateAndFee_DoesNotOverwriteExistingSource()
    {
        using var data = new Fixture();
        data.Context.MessageName = "Create";
        data.Source["new_2_date"] = new DateTime(2026, 1, 2);
        data.Source["new_fee"] = new Money(200m);
        data.Execute("credit-score-v1");
        var patch = Assert.Single(data.Writes);
        Assert.False(patch.Contains("new_2_date"));
        Assert.False(patch.Contains("new_first_date"));
        Assert.False(patch.Contains("new_fee"));
    }

    /// <summary>其他同階既有通過只提升來源，下階收到raw重新評估結果，不能合併兩者。</summary>
    [Fact]
    public void Stage_StickyOtherRecord_DoesNotChangeRawNextStagePropagation()
    {
        using var data = new Fixture();
        data.Course["new_class_number_fornext_level"] = 99;
        data.Course["new_complete_classes_number"] = 99;
        var older = data.AddHistory(data.Course);
        older["new_current_enter_next_level"] = true;
        older["new_current_complete"] = true;
        var next = data.AddHistory(data.AddCourse("下一階課程"));
        data.Execute("stage-history-v1");
        var source = Assert.Single(data.Writes.Where(x => x.Id == data.Source.Id));
        Assert.True(source.GetAttributeValue<bool>("new_current_complete"));
        var nextPatch = Assert.Single(data.Writes.Where(x => x.Id == next.Id));
        Assert.False(nextPatch.GetAttributeValue<bool>("new_previous_complete_condition"));
        Assert.Contains("本次課程紀錄就可以結業", nextPatch.GetAttributeValue<string>("new_not_yet"), StringComparison.Ordinal);
    }

    /// <summary>Shadow真正走設定適配器但零真Update，結果有完整end標記。</summary>
    [Fact]
    public void Progression_Shadow_RecordsIntentWithoutRealWrites()
    {
        using var data = new Fixture();
        data.Execute("credit-score-v1", true);
        Assert.Empty(data.Writes);
        Assert.Contains(data.Trace.Entries, x => x.Contains("\"end\":true"));
        Assert.Contains(data.Trace.Entries, x => x.Contains("new_estimated_credit"));
    }

    /// <summary>同名不同正式ID完整傳遞；相同ID重複則零候選發布，沒有Distinct掩飾。</summary>
    [Fact]
    public void Stage_SameNameDifferentIdsPreserved_DuplicateIdsRejectBeforeWrite()
    {
        using var data = new Fixture();
        var nextCourse = data.AddCourse("下一階課程");
        var first = data.AddHistory(nextCourse);
        var second = data.AddHistory(nextCourse);
        data.Execute("stage-history-v1");
        Assert.Contains(data.Writes, x => x.Id == first.Id);
        Assert.Contains(data.Writes, x => x.Id == second.Id);
        data.Writes.Clear();
        data.History.Add(first);
        Assert.Throws<InvalidOperationException>(() => data.Execute("stage-history-v1"));
        Assert.Empty(data.Writes);
    }

    /// <summary>查詢在候選建立後故障仍保留原實例且零寫入，下一次呼叫可安全重試。</summary>
    [Fact]
    public void Stage_QueryFault_PreservesOriginalExceptionAndRetryHasNoPartialCandidate()
    {
        using var data = new Fixture();
        var fault = new InvalidOperationException("synthetic-read-fault");
        data.QueryFailure = fault;
        Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => data.Execute("stage-history-v1")));
        Assert.Empty(data.Writes);
        data.QueryFailure = null;
        data.Execute("stage-history-v1");
        Assert.Single(data.Writes);
    }

    /// <summary>來源、歷程及課程歸屬任何一項錯配均必須先於所有寫入拒絕。</summary>
    [Theory]
    [InlineData("member")]
    [InlineData("history")]
    [InlineData("course")]
    public void Progression_CrossChurchScopeRejected_BeforePublication(string boundary)
    {
        using var data = new Fixture();
        var other = new EntityReference("account", Guid.NewGuid());
        if (boundary == "member") data.Member["parentcustomerid"] = other;
        else if (boundary == "course") data.Owner["parentcustomerid"] = other;
        else data.AddHistory(data.Course)["new_account_new_stor_lessons"] = other;
        Assert.Throws<InvalidOperationException>(() => data.Execute("stage-history-v1"));
        Assert.Empty(data.Writes);
    }

    /// <summary>學籍卡正常路徑發布原五值後觸碰本人卡，各層容器隔離且不任意彙總。</summary>
    [Fact]
    public void Credits_RollCard_PublishesLegacyProjectionAndTouchesMemberCard()
    {
        using var data = new Fixture();
        var card = data.AddCard();
        data.Source["new_classification"] = new OptionSetValue(100000002);
        data.Source["new_achieved_credit"] = 2d;
        data.Source["new_credit"] = 3d;
        var parent = new RemoteExecutionContext { OrganizationId = data.Context.OrganizationId };
        var grandparent = new RemoteExecutionContext { OrganizationId = data.Context.OrganizationId };
        data.Context.ParentContext = parent; parent.ParentContext = grandparent;
        data.Execute("credit-score-v1");
        Assert.Equal(new[] { data.Source.Id, card.Id }, data.Writes.Select(x => x.Id));
        var payload = Assert.IsType<ArrayList>(data.Context.SharedVariables["更新學籍卡"]);
        Assert.Equal(new object[] { "楊梅靈糧堂更新學籍卡", data.Source.Id, "MG12", 2d, 3d }, payload.ToArray());
        var parentPayload = Assert.IsType<ArrayList>(parent.SharedVariables["更新學籍卡"]);
        var grandparentPayload = Assert.IsType<ArrayList>(grandparent.SharedVariables["更新學籍卡"]);
        Assert.NotSame(payload, parentPayload);
        Assert.NotSame(parentPayload, grandparentPayload);
        payload[2] = "changed";
        Assert.Equal("MG12", parentPayload[2]);
        Assert.Equal("MG12", grandparentPayload[2]);
        Assert.False(data.Writes.Last().Contains("new_estimated_credit"));
    }

    /// <summary>學籍卡跨本人必須零寫入、零投影；Shadow正常卡亦不得污染context。</summary>
    [Fact]
    public void Credits_RollCard_ScopeAndShadowKeepSharedStateIsolated()
    {
        using var data = new Fixture();
        var card = data.AddCard();
        data.Execute("credit-score-v1", true);
        Assert.Empty(data.Writes);
        Assert.Empty(data.Context.SharedVariables);
        card["new_contact_student_roll_card"] = new EntityReference("contact", Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => data.Execute("credit-score-v1"));
        Assert.Empty(data.Writes);
        Assert.Empty(data.Context.SharedVariables);
    }

    /// <summary>保留舊同鍵payload的數值，只替換標記並隔離容器；缺分類及學分保留原哨兵。</summary>
    [Fact]
    public void Credits_RollCard_ExistingPayloadAndMissingValuesPreserveLegacyContract()
    {
        using var data = new Fixture();
        data.AddCard();
        var old = new ArrayList { "old-marker", data.Source.Id, "existing-category", 2d, 4d };
        data.Context.SharedVariables["更新學籍卡"] = old;
        data.Execute("credit-score-v1");
        var payload = Assert.IsType<ArrayList>(data.Context.SharedVariables["更新學籍卡"]);
        Assert.NotSame(old, payload);
        Assert.Equal("old-marker", old[0]);
        Assert.Equal(new object[] { "楊梅靈糧堂更新學籍卡", data.Source.Id, "existing-category", 2d, 4d }, payload.ToArray());
        data.Context.SharedVariables.Clear(); data.Writes.Clear();
        data.Execute("credit-score-v1");
        payload = Assert.IsType<ArrayList>(data.Context.SharedVariables["更新學籍卡"]);
        Assert.Equal(string.Empty, payload[2]);
        Assert.Equal(-9999d, payload[3]);
    }

    /// <summary>學籍卡及報名表同時存在時，Create沿用神學生與400費用並保留舊學期分類映射。</summary>
    [Fact]
    public void Credits_CreateRollCardAndRegistration_PreservesStudentFeeAndClassification()
    {
        using var data = new Fixture();
        data.Context.MessageName = "Create";
        data.AddCard();
        data.Source["new_registration_form_new_stor_lesson"] = new EntityReference("new_registration_form", Guid.NewGuid());
        data.Course["new_semester"] = new OptionSetValue(100000004);
        data.Course["new_lessons_fee"] = new Money(600m);
        data.Execute("credit-score-v1");
        var patch = data.Writes.First();
        Assert.True(patch.GetAttributeValue<bool>("new_god_student"));
        Assert.Equal(400m, patch.GetAttributeValue<Money>("new_fee").Value);
        Assert.Equal(100000004, patch.GetAttributeValue<OptionSetValue>("new_classification").Value);
        Assert.Equal("讀書會", Assert.IsType<ArrayList>(data.Context.SharedVariables["更新學籍卡"])[2]);
    }

    /// <summary>兩套舊旗標規則在Create及Update皆保留；不擴張為所有非空值皆跳過學分。</summary>
    [Theory]
    [InlineData("stage-history-v1", "other-button", "Update", true)]
    [InlineData("stage-history-v1", "", "Create", true)]
    [InlineData("credit-score-v1", "編輯式表格被按儲存:1", "Update", true)]
    [InlineData("credit-score-v1", "編輯式表格被按儲存", "Create", true)]
    [InlineData("credit-score-v1", "other-button", "Update", false)]
    public void Progression_SavedFlag_PreservesEachLegacyRoute(string profile, string flag, string message, bool skipped)
    {
        using var data = new Fixture();
        data.Context.MessageName = message;
        data.Source["new_saved_flag"] = flag;
        data.Execute(profile);
        Assert.Equal(skipped ? 0 : 1, data.Writes.Count);
        Assert.Empty(data.Context.SharedVariables);
    }

    /// <summary>缺profile不能默認選擇任一舊引擎，必須在查歷程及寫入前拒絕。</summary>
    [Fact]
    public void Progression_MissingAccountProfile_FailsBeforeHistoryAndWrite()
    {
        using var data = new Fixture();
        data.Rows[data.Church].Attributes.Remove("new_lessonprogressionprofile");
        Assert.Throws<InvalidOperationException>(() => new LessonProgressionPlugin(Configuration(), "").Execute(data.Provider));
        Assert.Empty(data.Writes);
        data.Service.Verify(x => x.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
    }

    /// <summary>最新同階課程決定門檻；自列亦匹配下階時，最後來源前階結果不可被raw覆蓋。</summary>
    [Fact]
    public void Stage_NewestSettingsAndOverlappingSource_PreserveFinalSourcePrecedence()
    {
        using var data = new Fixture();
        data.Course["new_class_number_fornext_level"] = 99;
        data.Course["new_complete_classes_number"] = 99;
        data.Course["new_nextlevel_class"] = "本階";
        var newestCourse = data.AddCourse("本階新版");
        newestCourse["new_class_number_fornext_level"] = 1;
        newestCourse["new_complete_classes_number"] = 1;
        var newest = data.AddHistory(newestCourse);
        newest["new_1_present"] = true;
        data.History.Remove(newest); data.History.Insert(0, newest);
        data.Execute("stage-history-v1");
        var patch = Assert.Single(data.Writes.Where(x => x.Id == data.Source.Id));
        Assert.True(patch.GetAttributeValue<bool>("new_current_complete"));
        Assert.Equal("沒有前階段課程名稱\r\n", patch["new_not_yet"]);
        Assert.True(patch.GetAttributeValue<bool>("new_previous_complete_condition"));
    }

    /// <summary>同一Plugin實例並行32次只寫各自來源，排空後各owner清空所有SDK參照。</summary>
    [Fact]
    public async Task Progression_ReusedPlugin_ConcurrentOwnersRemainIsolatedAndDrain()
    {
        var church = Guid.NewGuid();
        var plugin = new LessonProgressionPlugin(Configuration(), "");
        var owners = Enumerable.Range(0, 32).Select(_ => new Fixture(church)).ToArray();
        try
        {
            await Task.WhenAll(owners.Select(owner => Task.Run(() => plugin.Execute(owner.Provider))));
            foreach (var owner in owners)
            {
                var patch = Assert.Single(owner.Writes);
                Assert.Equal(owner.Source.Id, patch.Id);
                Assert.DoesNotContain(owners.Where(x => x != owner), x => x.Writes.Any(y => y.Id == patch.Id));
            }
        }
        finally
        {
            foreach (var owner in owners) owner.Dispose();
        }
        Assert.All(owners, x => { Assert.Empty(x.Rows); Assert.Empty(x.History); Assert.Empty(x.Writes); });
    }

    /// <summary>Update深度二沿用舊略過政策，不建立SDK或查詢；未知設定立即拒絕。</summary>
    [Fact]
    public void Progression_DepthAndConfiguration_RejectInvalidRoutes()
    {
        using var data = new Fixture();
        data.Context.Depth = 2;
        data.Execute("credit-score-v1");
        Assert.Empty(data.Writes);
        data.Service.Verify(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
        data.Context.Depth = 1;
        Assert.Throws<InvalidOperationException>(() => data.Execute("unknown"));
        Assert.Throws<InvalidPluginExecutionException>(() =>
            new LessonProgressionPlugin("{\"mode\":\"Live\",\"options\":{\"churchId\":\"not-supported\"}}", ""));
    }

    /// <summary>步驟只儲存模式，部署設定不含環境GUID，規則版本從正式account讀取。</summary>
    private static string Configuration(bool shadow = false) =>
        "{\"mode\":\"" + (shadow ? "Shadow" : "Live") + "\"}";

    /// <summary>每案擁有的有界SDK替身；Dispose清空集合，從不處置產品借入的真實SDK。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>本案正式教會ID，僅用於合成資料關聯，不寫入部署設定。</summary>
        internal Guid Church { get; }
        /// <summary>本案來源快照，產品應建立候選而不能原地修改。</summary>
        internal Entity Source { get; }
        /// <summary>本案權威課程設定，可由測試注入不同舊欄位。</summary>
        internal Entity Course { get; }
        /// <summary>來源本人contact，用於正式教會與學籍卡歸屬驗證。</summary>
        internal Entity Member { get; }
        /// <summary>課程主要聯絡人，其教會必須與本人一致。</summary>
        internal Entity Owner { get; }
        /// <summary>本案SDK讀取資料，Dispose確定清空所有Entity參照。</summary>
        internal Dictionary<Guid, Entity> Rows { get; } = new();
        /// <summary>本人歷程的獨立候選集合，保留同名不同正式ID。</summary>
        internal List<Entity> History { get; } = new();
        /// <summary>本案觀察到的Update順序，Dispose排空以免跨案例保留。</summary>
        internal List<Entity> Writes { get; } = new();
        /// <summary>嚴格借用SDK替身，不建立連線，清理時移除呼叫參照。</summary>
        internal Mock<IOrganizationService> Service { get; } = new(MockBehavior.Strict);
        /// <summary>本案影子追蹤替身，無static或跨案例資料。</summary>
        internal FakeTracingService Trace { get; } = new();
        /// <summary>本案同步pipeline輸入，僅由Fixture生命週期擁有。</summary>
        internal RemoteExecutionContext Context { get; }
        /// <summary>當次服務集合，不保存到產品可重用Plugin。</summary>
        internal FakeServiceProvider Provider { get; }
        /// <summary>可選的原例外注入，Dispose清除且不包裝成成功。</summary>
        internal Exception? QueryFailure { get; set; }

        /// <summary>建立獨立教會與會友歸屬，SDK只實作本規則的Retrieve、Query及Update。</summary>
        internal Fixture(Guid? church = null)
        {
            Church = church ?? Guid.NewGuid();
            Rows.Add(Church, new Entity("account", Church) { ["new_lessonprogressionprofile"] = "credit-score-v1" });
            Member = new Entity("contact", Guid.NewGuid()) { ["parentcustomerid"] = new EntityReference("account", Church) };
            Owner = new Entity("contact", Guid.NewGuid()) { ["parentcustomerid"] = new EntityReference("account", Church) };
            Rows.Add(Member.Id, Member); Rows.Add(Owner.Id, Owner);
            Course = AddCourse("本階課程");
            Course["new_now_stage_name"] = "本階";
            Course["new_nextlevel_class"] = "下一階";
            Source = AddHistory(Course);
            Context = new RemoteExecutionContext
            {
                MessageName = "Update", PrimaryEntityName = "new_stor_lessons", PrimaryEntityId = Source.Id,
                Stage = 40, Mode = 0, Depth = 1, UserId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), OrganizationName = "synthetic"
            };
            Service.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns<string, Guid, ColumnSet>((table, id, _) =>
                {
                    var row = Rows[id];
                    Assert.Equal(table, row.LogicalName);
                    return row;
                });
            Service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns<QueryBase>(query =>
            {
                if (QueryFailure is { } failure) throw failure;
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("new_stor_lessons", expression.EntityName);
                Assert.Contains(expression.Criteria.Conditions, condition =>
                    condition.AttributeName == "new_contact_new_stor_lessons" &&
                    condition.Operator == ConditionOperator.Equal && Equals(condition.Values[0], Member.Id));
                var result = new EntityCollection();
                result.Entities.AddRange(History);
                return result;
            });
            Service.Setup(x => x.Update(It.IsAny<Entity>())).Callback<Entity>(Writes.Add);
            var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
            factory.Setup(x => x.CreateOrganizationService(Context.UserId)).Returns(Service.Object);
            Provider = new FakeServiceProvider().Add<IPluginExecutionContext>(Context)
                .Add<IOrganizationServiceFactory>(factory.Object).Add<ITracingService>(Trace);
        }

        /// <summary>建立具權威主要聯絡人lookup的課程，同名課程也使用不同正式ID。</summary>
        internal Entity AddCourse(string name)
        {
            var row = new Entity("new_disciple_lessons", Guid.NewGuid())
            {
                ["new_name"] = name,
                ["new_contact_new_disciple_lessons"] = new EntityReference("contact", Owner.Id)
            };
            Rows.Add(row.Id, row);
            return row;
        }

        /// <summary>建立同一本人與教會的正式歷程，不按課名合併或覆寫其他ID。</summary>
        internal Entity AddHistory(Entity course)
        {
            var row = new Entity("new_stor_lessons", Guid.NewGuid())
            {
                ["new_contact_new_stor_lessons"] = new EntityReference("contact", Member.Id),
                ["new_account_new_stor_lessons"] = new EntityReference("account", Church),
                ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", course.Id)
            };
            Rows.Add(row.Id, row); History.Add(row);
            return row;
        }

        /// <summary>建立本人學籍卡；產品服務仍以context.UserId借用，無擴權存取。</summary>
        internal Entity AddCard()
        {
            var card = new Entity("new_roll_card", Guid.NewGuid())
            {
                ["new_contact_student_roll_card"] = new EntityReference("contact", Member.Id)
            };
            Rows.Add(card.Id, card);
            Source["new_roll_card_new_stor_lessons"] = new EntityReference("new_roll_card", card.Id);
            return card;
        }

        /// <summary>執行真Plugin，不以測試專用入口繞過設定、深度或Shadow生命週期。</summary>
        internal void Execute(string profile, bool shadow = false)
        {
            Rows[Church]["new_lessonprogressionprofile"] = profile;
            new LessonProgressionPlugin(Configuration(shadow), "").Execute(Provider);
        }

        /// <summary>清空每案owned集合及故障參照；沒有外部連線、timer或背景工作待清理。</summary>
        public void Dispose()
        {
            Rows.Clear(); History.Clear(); Writes.Clear(); QueryFailure = null;
            Service.Invocations.Clear();
        }
    }
}
