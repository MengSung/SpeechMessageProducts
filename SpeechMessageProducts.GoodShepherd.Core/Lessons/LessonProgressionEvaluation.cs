/*
 * 檔案整體說明
 * 目的與定位：P5-07進階計算器，將既有課程時數／必修條件及學分／權重欄位
 * 轉為不可變結果，替代僅含任意門檻的示範模型。呼叫端負責教會與會友授權、
 * 依建立時間排序並提供最新課程設定；本檔只同步讀取SDK純資料，不查詢服務。
 * 主要流程：階段模式驗證最多256個正式記錄ID，再合併堂次及五項作業，分別
 * 評估進階與結業；學分模式保留一次出席、晨禱缺席容許及末三堂恩典，以及
 * 作業／考試／實習平均加權。日期只判斷舊1000年缺值，不推論時區或今日。
 * 使用邊界與維護：欄位型別不符、非有限浮點、重複ID及超量立即拒絕；文字最多
 * 2000字，條件最多64項。合併的是堂次事實，絕不按姓名合併正式資料列。
 * 所有集合由單次方法擁有，結果只含布林及數值；無共用可變狀態、Session、
 * credential、快取、連線、背景工作或可清理資源，不保留任何來源Entity。
 * 驗證：雙目標reading測試保護上述計算；不代表附屬學籍卡處理器已重建。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>分開保留進階與結業評估，避免較低進階門檻冒充結業。</summary>
public sealed class LessonStageAssessment
{
    /// <summary>凍結本次計算的兩個獨立結果，不保留課程或會友物件。</summary>
    internal LessonStageAssessment(bool enterNext, bool complete, string classReport, string homeworkReport)
    {
        EnterNext = enterNext;
        Complete = complete;
        ClassReport = classReport;
        HomeworkReport = homeworkReport;
    }

    /// <summary>同階累積堂次與進階必修條件是否同時通過。</summary>
    public bool EnterNext { get; }
    /// <summary>同階累積堂次與結業必修條件是否同時通過。</summary>
    public bool Complete { get; }
    /// <summary>保留舊版繁中時數稽核文字；只屬本次結果，不包含SDK物件。</summary>
    public string ClassReport { get; }
    /// <summary>保留舊版繁中必修稽核文字，供來源與下階記錄使用。</summary>
    public string HomeworkReport { get; }
}

/// <summary>預估學分與總分的單次不可變結果，不包含已取得學分或學籍卡狀態。</summary>
public sealed class LessonCreditAssessment
{
    /// <summary>保存已驗證的有限數值；數值來源及授權由計算入口負責。</summary>
    internal LessonCreditAssessment(double estimatedCredit, double score)
    {
        EstimatedCredit = estimatedCredit;
        Score = score;
    }

    /// <summary>依出席與作業資格計算的預估學分，不能當作實得學分。</summary>
    public double EstimatedCredit { get; }
    /// <summary>出席比例分數加三種類別平均加權的總分，保持舊Double算術。</summary>
    public double Score { get; }
}

/// <summary>無服務與無狀態的既有欄位運算；不保存或修改來源快照。</summary>
public static class LessonProgressionEvaluation
{
    /// <summary>
    /// 使用呼叫端已驗證的最新同階課程設定評估完整歷程。負時數依舊歸零，
    /// 空必修條件直接通過；數字條件保留舊抽取ASCII數字行為，字母精確比對。
    /// 正式列身分先完整驗證，不能以堂次合併掩飾重複資料列。
    /// </summary>
    public static LessonStageAssessment Stage(Entity course, IReadOnlyList<Entity> rows)
    {
        Identity(course, "new_disciple_lessons");
        if (rows is null || rows.Count is < 1 or > 256)
            throw new InvalidOperationException("Lesson progression collection is invalid.");
        var ids = new HashSet<Guid>();
        var classes = new List<int>(30);
        var homework = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            Identity(row, "new_stor_lessons");
            if (!ids.Add(row.Id)) throw new InvalidOperationException("Lesson progression identity conflict.");
            for (var i = 1; i <= 30; i++)
                if (Bool(row, "new_" + Number(i) + "_present")) AddClass(classes, i);
            for (var i = 1; i <= 5; i++)
                if (DatePresent(row, "new_finishhomework_date" + Number(i)))
                    homework.Add(((char)('A' + i - 1)).ToString());
        }
        var report = LessonProgressionStageReport.Build(course, rows, classes, homework);
        return new LessonStageAssessment(
            classes.Count >= Math.Max(0, Integer(course, "new_class_number_fornext_level")) &&
                Conditions(Text(course, "new_classes_for_next_level"), classes, homework),
            classes.Count >= Math.Max(0, Integer(course, "new_complete_classes_number")) &&
                Conditions(Text(course, "new_complete_need_classes"), classes, homework),
            report.ClassReport, report.HomeworkReport);
    }

    /// <summary>
    /// 以來源列已保存的排課／作業／權重評估學分，不用課程目前權重覆蓋歷史。
    /// 晨禱辨識保留舊課名包含詞，屬課程內容而非教會名稱分支；最後三堂迴圈
    /// 保留舊版不檢查索引零的行為。沒有排課仍通過出席，沒有作業不阻擋學分。
    /// </summary>
    public static LessonCreditAssessment Credits(Entity row, string courseName)
    {
        Identity(row, "new_stor_lessons");
        if (courseName is null || courseName.Length > 2000)
            throw new InvalidOperationException("Lesson progression course name is invalid.");
        var scheduled = 0;
        var attended = 0;
        var attendedScheduled = 0;
        var graceScheduled = 0;
        var graceAttended = 0;
        for (var i = 1; i <= 15; i++)
        {
            var hasDate = DatePresent(row, DateField(i));
            var present = Bool(row, "new_" + Number(i) + "_present");
            if (hasDate) scheduled++;
            if (present) attended++;
            if (hasDate && present) attendedScheduled++;
        }
        for (var i = 15; i > 1 && graceScheduled < 3; i--)
        {
            if (!DatePresent(row, DateField(i))) continue;
            graceScheduled++;
            if (Bool(row, "new_" + Number(i) + "_present")) graceAttended++;
        }
        var homeworkValid = true;
        var totals = new double[3];
        var counts = new int[3];
        var kinds = new[] { LessonText.Get("Lessons.Progression.HomeworkKind"), LessonText.Get("Lessons.Progression.ExamKind"), LessonText.Get("Lessons.Progression.PracticeKind") };
        for (var i = 0; i < 8; i++)
        {
            var prefix = "new_" + (char)('a' + i);
            var name = Text(row, prefix + "_homework" + Number(i + 1));
            for (var kind = 0; kind < kinds.Length; kind++)
            {
                if (!Contains(name, kinds[kind])) continue;
                if (!DatePresent(row, prefix + "_complete_date")) homeworkValid = false;
                var score = Double(row, prefix + "_score");
                if (score < 0) continue;
                totals[kind] += score;
                counts[kind]++;
            }
        }
        var regularPresent = scheduled == 0 || attendedScheduled > 0;
        var morning = Contains(courseName, LessonText.Get("Lessons.Progression.MorningCourse"));
        var pass = morning ? scheduled - attended <= 2 : regularPresent;
        var credit = homeworkValid && pass ? Math.Max(0d, Double(row, "new_credit")) :
            homeworkValid && morning && graceScheduled == graceAttended ? 1d : 0d;
        var total = regularPresent ? Math.Max(0, Integer(row, "new_present")) : 0d;
        var weights = new[] { "new_homework", "new_exam", "new_practice" };
        for (var i = 0; i < counts.Length; i++)
            if (counts[i] > 0) total += totals[i] / counts[i] * (Integer(row, weights[i]) / 100d);
        if (double.IsNaN(total) || double.IsInfinity(total))
            throw new InvalidOperationException("Lesson progression score is not finite.");
        return new LessonCreditAssessment(credit, total);
    }

    /// <summary>保留舊插入演算法的30項上限，包括較小堂次插入造成重複的歷史計數。</summary>
    private static void AddClass(List<int> classes, int value)
    {
        if (classes.Count == 0) { classes.Add(value); return; }
        for (var i = 0; i < classes.Count && classes.Count < 30; i++)
        {
            if (value < classes[i]) classes.Insert(i, value);
            else if (value == classes[i]) break;
            else if (i == classes.Count - 1) { classes.Add(value); break; }
        }
    }

    /// <summary>有界逗號條件，保留原字母大小寫及空白；不引入任意運算式執行器。</summary>
    private static bool Conditions(string expression, List<int> classes, HashSet<string> homework)
    {
        if (expression.Length == 0) return true;
        var conditions = expression.Split(',');
        if (conditions.Length > 64) throw new InvalidOperationException("Lesson progression condition capacity exceeded.");
        foreach (var condition in conditions)
        {
            var digits = string.Empty;
            foreach (var value in condition)
                if (value is >= '0' and <= '9') digits += value;
            if (digits.Length == 0)
            {
                if (!homework.Contains(condition)) return false;
            }
            else if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                !classes.Contains(index)) return false;
        }
        return true;
    }

    /// <summary>驗證正式資料庫ID與表名；替代鍵不得冒充已存在的資料列身分。</summary>
    internal static void Identity(Entity row, string table)
    {
        if (row is null || row.LogicalName != table || row.Id == Guid.Empty || row.KeyAttributes.Count != 0)
            throw new InvalidOperationException("Lesson progression identity is invalid.");
    }

    /// <summary>缺少文字遵循舊空字串，存在時必須是有界字串，不回顯資料。</summary>
    internal static string Text(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? string.Empty :
        value is string text && text.Length <= 2000 ? text :
        throw new InvalidOperationException("Lesson progression text is invalid.");

    /// <summary>缺少布林等同false，型別錯誤拒絕而不是隱藏資料衝突。</summary>
    internal static bool Bool(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? false :
        value is bool result ? result : throw new InvalidOperationException("Lesson progression boolean is invalid.");

    /// <summary>缺整數保留舊工具-9999哨兵；門檻與權重各自在使用處套原語意。</summary>
    internal static int Integer(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? -9999 :
        value is int result ? result : throw new InvalidOperationException("Lesson progression integer is invalid.");

    /// <summary>只接受SDK Double及有限值；缺值保留舊負哨兵而非當成已完成。</summary>
    internal static double Double(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? -9999d :
        value is double result && !double.IsNaN(result) && !double.IsInfinity(result) ? result :
        throw new InvalidOperationException("Lesson progression numeric value is invalid.");

    /// <summary>只判斷舊1000年缺值，不調整時區、建立時鐘或保留DateTime參照。</summary>
    internal static bool DatePresent(Entity row, string field) =>
        row.Attributes.TryGetValue(field, out var value) && value is not null ?
        value is DateTime date ? date.Year != 1000 :
        throw new InvalidOperationException("Lesson progression date is invalid.") : false;

    /// <summary>既有15堂日期欄位的固定不規則命名，不向資料庫猜測新schema。</summary>
    internal static string DateField(int index) => index == 1 ? "new_first_date" :
        index <= 7 ? "new_" + Number(index) + "_date" : "new_l" + Number(index) + "_date";

    /// <summary>schema序號固定使用ASCII，不受執行緒文化及使用者語系影響。</summary>
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>兩目標使用相同Ordinal內容比對，避免Framework缺少Contains多載造成差異。</summary>
    internal static bool Contains(string text, string value)
    {
#if NETFRAMEWORK
        return text.IndexOf(value, StringComparison.Ordinal) >= 0;
#else
        return text.Contains(value, StringComparison.Ordinal);
#endif
    }
}
