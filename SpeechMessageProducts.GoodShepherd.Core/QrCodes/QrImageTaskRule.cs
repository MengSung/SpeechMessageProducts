/*
 * 檔案整體說明
 * 目的與定位：GoodShepherd P5-09 的會議／個人 QR 純入列規則，讓活動外殼不在
 * 沙箱繪圖、寫 UNC 或保存使用者資料；來源授權及教會歸屬由 QrImageTaskService 驗證。
 * 主要功能與流程：驗證正式教會／來源 ID，依封閉 personal 旗標組成五欄 schema=2
 * payload；kind 固定為整數 meeting=0／personal=1，維持共用命令只允許 GUID／數值，
 * 不擴張成任意字串。會議一組三十八張、個人一張，整組只產生一個 QrImageGenerate 命令。
 * 來源事件為 qrcodes.meeting／qrcodes.personal、記錄 ID 及內容版本一的確定性 GUID，
 * 教會另由既有資料庫替代鍵分區。個人 ContactId 等於來源 ID；會議不冒充單一會友。
 * 使用邊界與維護：store 及取消 token 只借用，命令凍結純值，不保存 SDK、Entity、
 * token、姓名、Session 或可變輸入；單次只有一個命令與一個唯讀結果，無快取或背景。
 * 唯一性由 store 的原子資料庫鍵保證，不以程序字典或鎖代替；成功只回真待辦 ID。
 * 呼叫前檢查取消，已入場同步 I/O 必須排空；故障原樣交外殼，不重試不確定寫入，
 * 也不因後到取消隱藏已確認成功的 ID。本層不擁有可 Dispose 資源或通知 consumer。
 * 驗證以雙宿主真規則及真 writer 的合成 SDK 測試保護，不等於圖片完成或實機驗收。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.QrCodes;

/// <summary>建立固定版本與固定張數的純值 QR 命令；不讀來源、不授權、不畫圖或保存跨呼叫資料。</summary>
public static class QrImageTaskRule
{
    /// <summary>
    /// 完整建立命令後只呼叫一次原子 store，同事件重送核對既有不可變內容，不重設工作狀態。
    /// 正式 ID 語法驗證不是授權，直接呼叫本規則者必須先完成相同的伺服器來源歸屬核對。
    /// </summary>
    /// <param name="store">當次已授權且具資料庫唯一鍵的借用 store；本規則不 Dispose。</param>
    /// <param name="churchId">權威來源解析出的唯一 account 正式 ID。</param>
    /// <param name="recordId">正式 meeting 或 contact ID，不使用顯示名稱或操作重試 token。</param>
    /// <param name="personal">true 為 contact 的一張圖（kind=1），false 為 meeting 的三十八張圖（kind=0）。</param>
    /// <param name="cancellationToken">只判定呼叫者明確取消；同步入場後由 store 完整排空。</param>
    /// <returns>單元素唯讀結果，內容為真正已建立或核對既有的待辦 ID，非來源或事件 ID。</returns>
    public static IReadOnlyList<Guid> Enqueue(IOutboundEnqueueStore store, Guid churchId, Guid recordId,
        bool personal, CancellationToken cancellationToken = default)
    {
#if NETFRAMEWORK
        if (store is null) throw new ArgumentNullException(nameof(store));
#else
        ArgumentNullException.ThrowIfNull(store);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        if (churchId == Guid.Empty) throw new ArgumentException("A database church identity is required.", nameof(churchId));
        if (recordId == Guid.Empty) throw new ArgumentException("A database QR source identity is required.", nameof(recordId));
        var kind = personal ? "personal" : "meeting";
        var source = DeterministicGuid.Create("qrcodes." + kind, recordId, 1);
        var command = new OutboundTaskCommand(OutboundTaskType.QrImageGenerate, churchId,
            personal ? recordId : null, source, new Dictionary<string, object>
            {
                ["schema"] = 2,
                ["kind"] = personal ? 1 : 0,
                ["recordId"] = recordId,
                ["contentVersion"] = 1,
                ["imageCount"] = personal ? 1 : 38
            });
        cancellationToken.ThrowIfCancellationRequested();
        var id = store.CreateOrGet(command, cancellationToken);
        if (id == Guid.Empty) throw new InvalidOperationException("QR store did not return a database identity.");
        return Array.AsReadOnly(new[] { id });
    }
}
