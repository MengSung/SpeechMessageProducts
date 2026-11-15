/*
 * 檔案整體說明
 * 目的與定位：保留退役階段課程程式的繁中診斷文字，與進階布林共用同一批
 * 已驗證的資料及堂次集合。此為Core內部純格式器，不依組織名稱分支。
 * 主要功能與流程：依來源記錄順序產生逐筆出席／作業歷程、時數評估及必修
 * 缺項文字，回傳兩段不可變文字供設定適配器寫入new_memo／new_not_yet。
 * 舊文字的錯字、空白、逗號及Windows換行刻意保留，不以美化之名改變契約。
 * 使用邊界與維護：呼叫端先驗證正式身分、排序及教會；課名只用於顯示與
 * 舊課程匹配，不當作資料列ID。每次最多256列，輸出最多100000字，超量拒絕
 * 且不發布部分診斷；不讀credential、不查SDK、不建立背景工作或外部資源。
 * 結果以具名不可變型別傳遞，避免沙箱依賴System.ValueTuple；不改舊文字。
 * 暫存字串及集合只活於同步呼叫，不快取或保留來源Entity。reading文字測試
 * 必須使用固定期望，不得以呼叫本格式器的結果作為自身期望。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using static SpeechMessageProducts.GoodShepherd.Core.Lessons.LessonProgressionEvaluation;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>從完整候選產生舊版文字，不進行寫入或授權決策。</summary>
internal static class LessonProgressionStageReport
{
    /// <summary>
    /// 單次格式化的兩段完整文字；僅保留不可變字串，不保留來源列或建構器。
    /// 呼叫端借用至建立評估結果為止，不需要Dispose、快取或跨呼叫共享。
    /// </summary>
    internal sealed class ReportResult
    {
        /// <summary>接收已通過容量檢查的完整文字；不截斷或改寫舊報告。</summary>
        internal ReportResult(string classReport, string homeworkReport)
        {
            ClassReport = classReport;
            HomeworkReport = homeworkReport;
        }

        /// <summary>本次出席歷程與時數稽核文字，供評估結果唯讀轉交。</summary>
        public string ClassReport { get; }

        /// <summary>本次必修與作業稽核文字，供評估結果唯讀轉交。</summary>
        public string HomeworkReport { get; }
    }

    /// <summary>報告固定Windows換行，與舊資料文字逐字相容，不採主機環境換行。</summary>
    private const string NewLine = "\r\n";

    /// <summary>在有限預算內組合兩段完整報告；所有字串均為本次owned值。</summary>
    internal static ReportResult Build(
        Entity course, IReadOnlyList<Entity> rows, List<int> classes, HashSet<string> homework)
    {
        var classText = new StringBuilder();
        foreach (var row in rows)
        {
            classText.Append(CourseName(row)).Append(", ");
            var line = LessonText.Get("Lessons.Progression.AttendedPrefix");
            for (var i = 1; i <= 30; i++)
                if (Bool(row, "new_" + Number(i) + "_present")) line += Number(i) + "，";
            classText.Append(TrimComma(line)).Append(LessonText.Get("Lessons.Progression.HomeworkPrefix"));
            for (var i = 1; i <= 5; i++)
                if (DatePresent(row, "new_finishhomework_date" + Number(i)))
                    classText.Append(Text(row, "new_homework" + Number(i))).Append('，');
            classText.Append(NewLine);
            Budget(classText);
        }
        classText.Append(LessonText.Get("Lessons.Progression.HoursHeading")).Append(NewLine)
            .Append(LessonText.Get("Lessons.Progression.HoursPrefix")).Append(Number(classes.Count)).Append(NewLine).Append(LessonText.Get("Lessons.Progression.HistoryPrefix"));
        foreach (var index in classes) classText.Append(Number(index)).Append('，');
        classText.Append(NewLine).Append("_________________________________").Append(NewLine)
            .Append(LessonText.Get("Lessons.Progression.CurrentHours")).Append(Number(classes.Count)).Append(NewLine);
        var next = Math.Max(0, Integer(course, "new_class_number_fornext_level"));
        var complete = Math.Max(0, Integer(course, "new_complete_classes_number"));
        classText.Append(LessonText.Get("Lessons.Progression.NextHours")).Append(Number(next)).Append(NewLine)
            .Append(classes.Count >= next ? LessonText.Get("Lessons.Progression.NextHoursPass") :
                LessonText.Get("Lessons.Progression.NextHoursFail") + Number(classes.Count)).Append(NewLine)
            .Append(LessonText.Get("Lessons.Progression.CompleteHours")).Append(Number(complete)).Append(NewLine)
            .Append(classes.Count >= complete ? LessonText.Get("Lessons.Progression.CompleteHoursPass") :
                LessonText.Get("Lessons.Progression.CompleteHoursFail") + Number(classes.Count)).Append(NewLine);
        var requirements = new StringBuilder(
            LessonText.Get("Lessons.Progression.RequiredHeading") + NewLine +
            LessonText.Get("Lessons.Progression.NextHeading") + NewLine);
        var nextPass = AppendConditions(requirements, course, "new_classes_for_next_level", classes, homework);
        requirements.Append(nextPass ? LessonText.Get("Lessons.Progression.NextPass") : NewLine + LessonText.Get("Lessons.Progression.NextFail"))
            .Append(NewLine).Append(NewLine).Append(LessonText.Get("Lessons.Progression.CompleteHeading")).Append(NewLine);
        var completePass = AppendConditions(requirements, course, "new_complete_need_classes", classes, homework);
        requirements.Append(completePass ? LessonText.Get("Lessons.Progression.CompletePass") : NewLine + LessonText.Get("Lessons.Progression.CompleteFail")).Append(NewLine);
        Budget(classText);
        Budget(requirements);
        return new ReportResult(classText.ToString(), requirements.ToString());
    }

    /// <summary>逐項診斷必修條件，與舊版相同抽取數字及精確字母比較，不修剪輸入。</summary>
    private static bool AppendConditions(StringBuilder report, Entity course, string field,
        List<int> classes, HashSet<string> homework)
    {
        var expression = Text(course, field);
        if (expression.Length == 0) { report.Append(LessonText.Get("Lessons.Progression.NoRequirements")); return true; }
        var conditions = expression.Split(',');
        if (conditions.Length > 64) throw new InvalidOperationException("Lesson progression condition capacity exceeded.");
        var pass = true;
        foreach (var condition in conditions)
        {
            var digits = new StringBuilder();
            foreach (var ch in condition)
                if (ch is >= '0' and <= '9') digits.Append(ch);
            if (digits.Length != 0)
            {
                if (!int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    throw new InvalidOperationException("Lesson progression condition index is invalid.");
                if (classes.Contains(index)) continue;
                report.Append(LessonText.Get("Lessons.Progression.MissingClassPrefix")).Append(Number(index)).Append(LessonText.Get("Lessons.Progression.ClassSuffix"));
            }
            else
            {
                if (homework.Contains(condition)) continue;
                var name = condition.Length == 1 && condition[0] is >= 'A' and <= 'E'
                    ? Text(course, "new_homework" + Number(condition[0] - 'A' + 1)) : string.Empty;
                report.Append(condition).Append(' ').Append(name).Append(LessonText.Get("Lessons.Progression.MissingHomework"));
            }
            pass = false;
        }
        return pass;
    }

    /// <summary>課程顯示名稱由適配器重讀權威課程後提供，不用名稱當正式身分。</summary>
    private static string CourseName(Entity row) =>
        row.GetAttributeValue<EntityReference>("new_new_disciple_lessons_new_stor_les")?.Name ?? string.Empty;

    /// <summary>保留舊工具最後一個全形逗號的截斷語意，沒有逗號則保留原文。</summary>
    private static string TrimComma(string value)
    {
        var index = value.LastIndexOf('，');
        return index > 0 ? value.Substring(0, index) : value;
    }

    /// <summary>限制單次報告保留量，超量時整次失敗，避免診斷無界配置。</summary>
    private static void Budget(StringBuilder value)
    {
        if (value.Length > 100000) throw new InvalidOperationException("Lesson progression report capacity exceeded.");
    }
}
