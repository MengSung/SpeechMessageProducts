/*
 * 檔案整體說明
 * 目的與定位：P5-07課程連結的對外待辦規則，以每張圖一個QrImageGenerate事件
 * 取代舊Plugin／活動在沙箱畫圖及寫UNC；呼叫端先以伺服器資料驗證課程與教會。
 * 主要流程：驗正式ID及內容版本，先建完整31／32個不可變命令，再逐張同步入列。
 * 15堂各有簽到／簽退，另有報名；只有舊QR活動包含問卷。payload只含正式課程ID
 * 與數值，不帶LIFF網址、個資或credential。action固定0簽到、1簽退、2報名、3問卷；
 * classIndex在前兩種為1～15，後兩種為0；schema與contentVersion分別描述格式及內容版。
 * 身分與失敗：事件GUID由課程ID、內容版本、堂次與動作計算，教會另由既有資料庫
 * 替代鍵分區。回傳只含store真正儲存ID；既有同事件不重設狀態。不確定I/O故障原樣
 * 向外傳遞，不回傳部分結果；平台交易或資料庫唯一鍵負責回滾／後續重送，不假裝整組原子。
 * 使用邊界與維護：store及取消token借用，單次最多32命令及32結果，無static可變狀態、
 * cache、背景Task或timer。SDK若已入場必須同步排空；此層沒有Dispose owner。
 * 離線雙宿主驗數量、欄位、分區、重送與故障，不代表圖片已發布或實機影子驗收。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>只建立有界純值QR工作，不在沙箱產生圖片或連外。</summary>
public static class LessonLinkRule
{
    /// <summary>
    /// 在任何儲存呼叫之前建立完整命令；同事件由既有store原子建立或核對。
    /// 呼叫端必須完成教會／課程授權，ID語法驗證不是授權。
    /// </summary>
    /// <param name="store">已授權的單次借用儲存；本規則不Dispose。</param>
    /// <param name="churchId">來源課程經伺服器查證的正式教會ID。</param>
    /// <param name="lessonId">正式課程資料庫ID，不接受顯示名稱。</param>
    /// <param name="contentVersion">正整數內容版本；相同內容重送維持相同版本。</param>
    /// <param name="includePoll">舊活動為true，舊Plugin為false，不依組織名稱分支。</param>
    /// <param name="cancellationToken">每次同步I/O入場前檢查；入場後由store排空。</param>
    /// <returns>不可變結果，依堂次簽到／簽退、報名、選填問卷排列的正式待辦ID。</returns>
    public static IReadOnlyList<Guid> Enqueue(IOutboundEnqueueStore store, Guid churchId, Guid lessonId,
        int contentVersion, bool includePoll, CancellationToken cancellationToken = default)
    {
#if NETFRAMEWORK
        if (store is null) throw new ArgumentNullException(nameof(store));
#else
        ArgumentNullException.ThrowIfNull(store);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        if (churchId == Guid.Empty) throw new ArgumentException("A database church identity is required.", nameof(churchId));
        if (lessonId == Guid.Empty) throw new ArgumentException("A database lesson identity is required.", nameof(lessonId));
#if NETFRAMEWORK
        if (contentVersion <= 0) throw new ArgumentOutOfRangeException(nameof(contentVersion));
#else
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentVersion);
#endif
        var commands = new List<OutboundTaskCommand>(includePoll ? 32 : 31);
        for (var index = 1; index <= 15; index++)
        {
            commands.Add(Command(churchId, lessonId, contentVersion, index, 0));
            commands.Add(Command(churchId, lessonId, contentVersion, index, 1));
        }
        commands.Add(Command(churchId, lessonId, contentVersion, 0, 2));
        if (includePoll) commands.Add(Command(churchId, lessonId, contentVersion, 0, 3));
        var results = new Guid[commands.Count];
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = store.CreateOrGet(commands[index], cancellationToken);
            if (id == Guid.Empty) throw new InvalidOperationException("QR store did not return a database identity.");
            results[index] = id;
        }
        return Array.AsReadOnly(results);
    }

    /// <summary>每張圖片獨立穩定事件與封閉數值payload，建構時即凍結，不保留輸入字典。</summary>
    private static OutboundTaskCommand Command(Guid churchId, Guid lessonId, int version, int classIndex, int action)
    {
        var source = DeterministicGuid.Create("lessons.qr-image", lessonId, version, classIndex, action);
        return new OutboundTaskCommand(OutboundTaskType.QrImageGenerate, churchId, null, source,
            new Dictionary<string, object>
            {
                ["schema"] = 1,
                ["lessonId"] = lessonId,
                ["contentVersion"] = version,
                ["classIndex"] = classIndex,
                ["action"] = action
            });
    }
}
