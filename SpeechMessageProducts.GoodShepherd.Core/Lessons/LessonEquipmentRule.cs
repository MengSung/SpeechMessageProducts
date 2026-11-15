/*
 * 檔案整體說明
 * 目的與定位：P5-07設備階級純規則，共用於net462活動與net10主機；來源為舊
 * EquipmentUtility.cs:305-334，只取已完成課程對應階段的最高序位，未知名稱保留零階。
 * 主要流程：先完整驗證最多64個非空且唯一的階段，再逐筆驗證最多4096個正式課程
 * 記錄身分；同名不同ID都保留，同ID重複拒絕，不以Distinct或名稱合併隱藏資料衝突。
 * 全部列舉完成才發布純字串結果；中途失敗不寫入任何contact。階段名稱由呼叫端依
 * 產品資源／核定設定供應，不以教會名稱或組織代碼分支，也不從目前文化猜階段名稱。
 * 使用邊界與維護：資料授權由外殼在查詢前完成，GUID語法不是授權。本檔無SDK、
 * Token、時鐘、快取、連線及背景工作；所有集合只屬於同步呼叫、固定上限並在返回
 * 後可回收，foreach確定釋放列舉器，不保留來源集合或跨會友可變狀態。
 */
using System;
using System.Collections.Generic;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>單筆正式課程完成狀態的不可變值；只保留本次計算所需ID、布林及有界階名。</summary>
public sealed class LessonCompletion
{
    /// <summary>建構純值快照；拒絕空正式ID及超過200字的階名，不查資料也不自行授權。</summary>
    /// <param name="id">正式課程記錄ID，同名課程仍以此區分。</param>
    /// <param name="completed">舊new_current_complete的實際布林值。</param>
    /// <param name="stage">關聯課程new_now_stage_name；null依舊空值處理。</param>
    /// <exception cref="ArgumentException">資料身分或字串預算不合法。</exception>
    public LessonCompletion(Guid id, bool completed, string? stage)
    {
        if (id == Guid.Empty || stage?.Length > 200) throw new ArgumentException("Invalid lesson completion value.");
        Id = id; Completed = completed; Stage = stage ?? string.Empty;
    }

    /// <summary>正式課程列身分；同一集合不允許重複，非操作重試鍵。</summary>
    public Guid Id { get; }
    /// <summary>只有true的記錄可提高最高階級，false仍須通過身分檢查。</summary>
    public bool Completed { get; }
    /// <summary>原階名，序位精確比對，不修剪、不改寫文字或保存來源物件。</summary>
    public string Stage { get; }
}

/// <summary>無狀態設備階級運算，不發布部分結果、合併同名列或執行資料庫更新。</summary>
public static class LessonEquipmentRule
{
    /// <summary>驗證完整身分集合後返回最高完成階，沒有完成或未知階名均返回第零階。</summary>
    /// <param name="lessons">已在伺服器授權範圍內查得的正式課程純值，最多4096筆。</param>
    /// <param name="stages">由低至高的1～64個精確階名，每個1～200字且不可重複。</param>
    /// <returns>呼叫提供的不可變階名字串，不持有集合或任何使用者資源。</returns>
    /// <exception cref="ArgumentException">階段缺失、重複、超長或資料值不合法。</exception>
    /// <exception cref="InvalidOperationException">重複正式ID或課程超量，拒絕發布結果。</exception>
    public static string Evaluate(IEnumerable<LessonCompletion> lessons, IReadOnlyList<string> stages)
    {
#if NETFRAMEWORK
        if (lessons is null) throw new ArgumentNullException(nameof(lessons));
#else
        ArgumentNullException.ThrowIfNull(lessons);
#endif
        if (stages is null || stages.Count is < 1 or > 64) throw new ArgumentException("Invalid equipment stage count.", nameof(stages));
        var ordered = new List<string>(stages.Count);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            if (string.IsNullOrEmpty(stage) || stage.Length > 200 || positions.ContainsKey(stage))
                throw new ArgumentException("Invalid equipment stage value.", nameof(stages));
            positions.Add(stage, ordered.Count); ordered.Add(stage);
        }
        var identities = new HashSet<Guid>();
        var highest = 0;
        foreach (var lesson in lessons)
        {
            if (lesson is null) throw new ArgumentException("Missing lesson completion value.", nameof(lessons));
            if (identities.Count >= 4096 || !identities.Add(lesson.Id))
                throw new InvalidOperationException("Lesson identity conflict or capacity exceeded.");
            if (lesson.Completed && positions.TryGetValue(lesson.Stage, out var index) && index > highest) highest = index;
        }
        return ordered[highest];
    }
}
