/*
 * 檔案整體說明
 * 目的與定位：P5-07學籍卡通知的不可變純值投影，保留退役課程入口可證實的
 * 五值契約。由Records在學籍卡本人關聯驗證後建立，Plugin同步轉成各context
 * 專屬ArrayList；本檔不取得context、不讀SDK、不猜測下游學分彙總公式。
 * 主要功能與流程：輸入正式來源ID、分類選項、實得與預估學分，依原映射產生
 * 分類文字，保留舊缺實得學分的-9999值與未知分類的空字串。固定標記雖含歷史
 * 名稱，但只屬序列化契約，絕非教會名稱分支或授權來源。
 * 使用邊界與維護：僅接受非空ID與有限學分，失敗在任何CRM寫入前回報；
 * 呼叫返回後不持有Entity、service、使用者或credential。資料僅活於當次同步
 * pipeline，不建立cache、背景工作或外部資源，無Dispose責任。reading測試
 * 應比較五個固定值與各層容器隔離，不把投影成功描述為下游彙總驗收。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>學籍卡觸碰前的五值通知，不包含可變SDK或context參照。</summary>
public sealed class LessonProgressionRollCardProjection
{
    /// <summary>舊入口固定相容標記，只作通知值，不用於規則選取。</summary>
    public static string Marker => LessonText.Get("Lessons.Progression.RollCardMarker");
    /// <summary>舊同步pipeline的固定通知鍵，不能拼接身分或機密資料。</summary>
    public static string SharedVariableKey => LessonText.Get("Lessons.Progression.RollCardKey");

    /// <summary>驗證來源與有限數值，保留原分類映射及缺值哨兵，不擅自修正舊業務。</summary>
    internal LessonProgressionRollCardProjection(Guid sourceId, int? category, double achieved, double estimated)
    {
        if (sourceId == Guid.Empty || double.IsNaN(achieved) || double.IsInfinity(achieved) ||
            double.IsNaN(estimated) || double.IsInfinity(estimated))
            throw new InvalidOperationException("Lesson progression roll-card projection is invalid.");
        SourceId = sourceId;
        Category = category switch
        {
            100000000 => LessonText.Get("Lessons.Progression.Morning"),
            100000001 => LessonText.Get("Lessons.Progression.InnerHealing"),
            100000002 => "MG12",
            100000003 => LessonText.Get("Lessons.Progression.Pregnancy"),
            100000004 => LessonText.Get("Lessons.Progression.ReadingGroup"),
            100000005 => LessonText.Get("Lessons.Progression.Conference"),
            100000006 => LessonText.Get("Lessons.Progression.Foundation"),
            _ => string.Empty
        };
        AchievedCredit = achieved;
        EstimatedCredit = estimated;
    }

    /// <summary>已驗證的正式學員課程ID，不是姓名、課名或重試token。</summary>
    public Guid SourceId { get; }
    /// <summary>原分類選項的固定繁中文字；未知分類依原契約為空字串。</summary>
    public string Category { get; }
    /// <summary>來源保存的實得學分，缺值仍為原工具-9999哨兵。</summary>
    public double AchievedCredit { get; }
    /// <summary>此次重新計算的預估學分，不能替代實得學分。</summary>
    public double EstimatedCredit { get; }
}
