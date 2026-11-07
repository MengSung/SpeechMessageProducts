/*
 * 檔案整體說明
 *
 * 目的與定位：提供 GoodShepherd 領域的雙目標待辦入列入口，僅處理宮格、QR 圖片、
 * 撥款查詢三種工作；Plugin 與 net10 主機共用，不引入 Hosting 或另一份送訊實作。
 * 主要功能與流程：接收伺服器已授權的教會與來源記錄，建立凍結純值命令，再委派
 * 共用儲存邊界原子建立或讀回正式資料庫 ID。先驗 payload 再 I/O，重送不重設既有列。
 * 使用邊界與維護：Writer 只借入固定授權範圍的 store，不接管其連線所有權；
 * 不保存 request、Session、字典、credential 或操作結果，沒有 queue 或背景 Task。
 * SDK 故障與不確定寫入保留給最終 owner，不在此自行重送或以通知掩蓋主要失敗。
 * 雙宿主測試及 SDK 適配器測試分開，離線結果不代表 D365 實機唯一鍵已驗收。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.Outbound;

/// <summary>只組合已授權參數與純值驗證；資料庫唯一鍵而非程序鎖負責跨程序防重。</summary>
public sealed class OutboundTaskWriter
{
    private readonly IOutboundEnqueueStore store;

    /// <summary>借入生命期由呼叫端掌握的 store，不保存或建立任意組織連線。</summary>
    /// <param name="store">固定已授權組織／產品範圍的入列儲存介面。</param>
    public OutboundTaskWriter(IOutboundEnqueueStore store) =>
        this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>先驗證並凍結 payload，再入列；相同來源記錄的重送維持同一穩定鍵。</summary>
    /// <param name="type">只允許三種對外待辦。</param>
    /// <param name="churchId">伺服器核准的教會資料庫 ID，不是 caller 路由授權。</param>
    /// <param name="contactId">選填且由伺服器核對教會歸屬的會友 ID。</param>
    /// <param name="sourceId">來源事件的正式 ID；retry 不得換新值。</param>
    /// <param name="payload">只含 ID 與數值的有界字典；呼叫後修改不影響命令。</param>
    /// <param name="cancellationToken">此操作 owner 的取消，入場前即取消時零 I/O。</param>
    /// <returns>正式待辦資料庫 ID；不把來源事件 ID 當成儲存結果。</returns>
    public Guid Enqueue(OutboundTaskType type, Guid churchId, Guid? contactId, Guid sourceId,
        IReadOnlyDictionary<string, object> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var command = new OutboundTaskCommand(type, churchId, contactId, sourceId, payload);
        var id = store.CreateOrGet(command, cancellationToken);
        // 不確定寫入後不自動重送；真正的正式 ID 是儲存層成功契約，不能用來源 ID 補洞。
        if (id == Guid.Empty) throw new InvalidOperationException("Outbound store did not return a database identity.");
        return id;
    }
}
