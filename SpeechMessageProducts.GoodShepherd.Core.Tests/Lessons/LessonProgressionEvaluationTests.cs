/*
 * 檔案整體說明
 * 目的與定位：以逐段閱讀退役來源建立的合成資料，驗證課程進階真正使用既有欄位，
 * 不再以任意門檻宣稱舊版相容。階段契約來自舊第一套來源1134～1326行，學分契約
 * 來自第二套來源340～898行；未執行舊DLL，也不把離線結果描述成正式實機證據。
 * 主要流程：建構獨立SDK純資料快照，斷言跨記錄課堂合併、指定必修、負門檻、
 * 一般課程一次出席、晨禱兩次缺席與末堂恩典、作業分類及平均加權分數。
 * 故障注入包括重複正式ID、非有限分數與來源變動；斷言拒絕或輸出互不影響。
 * 使用邊界與維護：沒有SDK服務、網路、Session、計時器或背景工作；每案擁有自己的
 * Entity與集合，返回值不得保留來源可變參照，沒有需Dispose的外部資源。
 */
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Lessons;

/// <summary>以reading基準保護階段及學分兩種實際欄位計算，非組織實機驗收。</summary>
public sealed class LessonProgressionEvaluationTests
{
    /// <summary>
    /// 檢查實際編譯的報告邊界，不允許以 ValueTuple 將新依賴帶入沙箱。
    /// 此反射只由離線測試宿主執行，不進產品；回歸故障為 Build 回傳具名 tuple，
    /// 決定性斷言要求回傳型別非 System.ValueTuple，且兩段文字只能讀不能改。
    /// </summary>
    [Fact]
    public void Stage_ReportBoundary_UsesImmutableNamedResult()
    {
        var report = typeof(LessonProgressionEvaluation).Assembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Lessons.LessonProgressionStageReport", true)!;
        var build = report.GetMethod("Build",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.NotNull(build);
        var result = build.ReturnType;
        Assert.False(result.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal));
        foreach (var name in new[] { "ClassReport", "HomeworkReport" })
        {
            var property = result.GetProperty(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            Assert.NotNull(property);
            Assert.Equal(typeof(string), property!.PropertyType);
            Assert.Null(property.GetSetMethod(true));
        }
    }

    /// <summary>課堂跨不同正式記錄合併，指定堂次及作業仍須分別通過。</summary>
    [Fact]
    public void Lessons_Progression_TwoWingsProfile_MatchesLegacy()
    {
        var (rows, expected) = ReadingCase("stage-merged-attendance-required-homework");
        var course = rows["course"];
        var first = rows["record"];
        var second = rows["record-2"];
        var result = LessonProgressionEvaluation.Stage(course, new[] { first, second });
        Assert.Equal(expected.Value<bool>("new_current_enter_next_level"), result.EnterNext);
        Assert.Equal(expected.Value<bool>("new_current_complete"), result.Complete);
        Assert.Equal(expected.Value<string>("new_memo"), result.ClassReport + "\r\n" + result.HomeworkReport);
        second["new_3_present"] = true;
        Assert.True(LessonProgressionEvaluation.Stage(course, new[] { first, second }).Complete);
        Assert.False(result.Complete);
        Assert.False(first.Contains("new_current_complete"));
    }

    /// <summary>一般課程只需一次已排課出席，分數按類別平均後乘權重，不改用全部出席。</summary>
    [Fact]
    public void Lessons_Progression_SecondProfile_MatchesLegacy()
    {
        var (rows, expected) = ReadingCase("credit-weighted-homework-average");
        var row = rows["record"];
        var courseName = rows["course"].GetAttributeValue<string>("new_name");
        var result = LessonProgressionEvaluation.Credits(row, courseName);
        Assert.Equal(expected.Value<double>("new_estimated_credit"), result.EstimatedCredit);
        Assert.Equal(expected.Value<double>("new_score"), result.Score);
        row.Attributes.Remove("new_b_complete_date");
        Assert.Equal(0d, LessonProgressionEvaluation.Credits(row, "一般課程").EstimatedCredit);
    }

    /// <summary>
    /// 獨立 reading 第三案：三堂出席已滿兩種時數門檻，但必要作業 A 未交，
    /// 進階與結業仍須同時拒絕；不能讓合併堂次掩蓋缺少作業的條件。
    /// 所有正式合成列僅屬本案，計算不得更改來源欄位。
    /// </summary>
    [Fact]
    public void Stage_RequiredHomeworkMissing_DoesNotPassAttendanceAlone()
    {
        var (rows, expected) = ReadingCase("stage-missing-required-homework");
        var first = rows["record"];
        var result = LessonProgressionEvaluation.Stage(rows["course"], new[] { first, rows["record-2"] });
        Assert.Equal(expected.Value<bool>("new_current_enter_next_level"), result.EnterNext);
        Assert.Equal(expected.Value<bool>("new_current_complete"), result.Complete);
        Assert.False(result.EnterNext);
        Assert.False(result.Complete);
        Assert.Equal(expected.Value<string>("new_memo"), result.ClassReport + "\r\n" + result.HomeworkReport);
        Assert.False(first.Contains("new_current_complete"));
    }

    /// <summary>晨禱缺席兩次仍可取得原學分，更多缺席但最後三堂皆出席只得一學分。</summary>
    [Fact]
    public void Credits_MorningAbsenceAndGrace_PreserveReadingContract()
    {
        var row = Row();
        row["new_credit"] = 4d;
        for (var i = 1; i <= 6; i++)
        {
            row[i == 1 ? "new_first_date" : $"new_{i}_date"] = new DateTime(2026, 1, i);
            row[$"new_{i}_present"] = i >= 3;
        }
        Assert.Equal(4d, LessonProgressionEvaluation.Credits(row, "晨禱課程").EstimatedCredit);
        row["new_3_present"] = false;
        Assert.Equal(1d, LessonProgressionEvaluation.Credits(row, "晨禱課程").EstimatedCredit);
        row["new_6_present"] = false;
        Assert.Equal(0d, LessonProgressionEvaluation.Credits(row, "晨禱課程").EstimatedCredit);
    }

    /// <summary>同名不同ID不能被合併，完全相同ID重複則必須拒絕完整候選。</summary>
    [Fact]
    public void Stage_DuplicateIdentityRejected_AndNegativeThresholdClamped()
    {
        var course = new Entity("new_disciple_lessons", Guid.NewGuid())
        {
            ["new_class_number_fornext_level"] = -1,
            ["new_complete_classes_number"] = -1
        };
        var a = Row();
        var b = Row();
        Assert.True(LessonProgressionEvaluation.Stage(course, new[] { a, b }).Complete);
        Assert.Throws<InvalidOperationException>(() =>
            LessonProgressionEvaluation.Stage(course, new[] { a, a }));
    }

    /// <summary>任何未排課皆依舊視為出席通過；非有限分數不准發布至CRM。</summary>
    [Fact]
    public void Credits_NoScheduleAndNonFiniteValue_AreExplicit()
    {
        var row = Row();
        row["new_credit"] = 2d;
        Assert.Equal(2d, LessonProgressionEvaluation.Credits(row, "一般").EstimatedCredit);
        row["new_a_homework1"] = "作業";
        row["new_a_score"] = double.NaN;
        Assert.Throws<InvalidOperationException>(() => LessonProgressionEvaluation.Credits(row, "一般"));
    }

    /// <summary>建立獨立正式列；沒有全域集合或跨案例可變資源。</summary>
    private static Entity Row() => new("new_stor_lessons", Guid.NewGuid());

    /// <summary>
    /// 從嵌入reading情境建立每案獨立正式ID，日期與Double保留SDK欄位型別。
    /// 讀取兩檔皆以using關閉stream，驗證來源說明存在，不建立或冒充legacy.json。
    /// </summary>
    private static (Dictionary<string, Entity> Rows, JObject Expected) ReadingCase(string name)
    {
        var scenarios = ReadGolden("scenarios");
        var expected = ReadGolden("expected");
        Assert.Equal("reading", expected.Value<string>("basis"));
        var input = scenarios["cases"]!.Single(x => x.Value<string>("name") == name);
        var output = expected["cases"]!.Single(x => x.Value<string>("name") == name);
        var evidence = expected["declaredDifferences"]!.First(x => x.Value<string>("case") == name);
        Assert.Contains(".cs:", evidence.Value<string>("reason"), StringComparison.Ordinal);
        var rows = input["seed"]!.ToDictionary(x => x.Value<string>("alias")!,
            x => new Entity(x.Value<string>("entity"), Guid.NewGuid()), StringComparer.Ordinal);
        foreach (var seed in input["seed"]!)
        {
            var row = rows[seed.Value<string>("alias")!];
            foreach (var field in ((JObject)seed["fields"]!).Properties())
                row[field.Name] = field.Value.Type switch
                {
                    JTokenType.Object => new EntityReference(field.Value.Value<string>("entity"),
                        rows[field.Value.Value<string>("ref")!].Id),
                    JTokenType.Boolean => field.Value.Value<bool>(),
                    JTokenType.Integer => field.Value.Value<int>(),
                    JTokenType.Float => field.Value.Value<double>(),
                    JTokenType.Date => field.Value.Value<DateTime>(),
                    JTokenType.String => field.Value.Value<string>(),
                    _ => throw new InvalidOperationException("Unexpected reading field type.")
                };
        }
        // 與適配器重讀課程後填入顯示名稱相同，只從本案權威種子解析，不猜測名稱。
        foreach (var row in rows.Values)
            if (row.GetAttributeValue<EntityReference>("new_new_disciple_lessons_new_stor_les") is { } reference)
                reference.Name = rows.Values.Single(x => x.Id == reference.Id).GetAttributeValue<string>("new_name");
        return (rows, (JObject)output["writes"]![0]!["fields"]!);
    }

    /// <summary>取得嵌入且有界的本包JSON，讀取器由本方法確定關閉，不依賴工作目錄。</summary>
    private static JObject ReadGolden(string name)
    {
        using var stream = typeof(LessonProgressionEvaluationTests).Assembly.GetManifestResourceStream(
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.Legacy.lessonsprogression." + name + ".json")
            ?? throw new InvalidOperationException("Missing progression reading resource.");
        using var reader = new System.IO.StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }
}
