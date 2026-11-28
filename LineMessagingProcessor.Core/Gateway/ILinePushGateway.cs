/*
檔案整體說明
目的與定位：F5 雙目標推播唯一出口契約，供產品業務呼叫而不公開 broadcast 能力。
主要功能與流程：Send 完成租戶、Channel、全名單核對，套用私有 sender／Flex 快照，再以
資料庫原子批次 claim 配合同鍵 HTTP；不確定結果持久待對帳，不換鍵重新發送。
使用邊界與維護：Gateway 借用固定產品依賴，單次參數不得跨操作保留；correlationId 必須
由伺服器業務事件建立，不可採 request 任意 ID。平台接受不等於手機送達，結果不得混稱。
*/
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>不包含廣播或跨產品後備值的唯一租戶推播入口。</summary>
public interface ILinePushGateway
{
    /// <summary>
    /// 以最多 5000 位、五則 SDK 訊息進行整案核對與分批；caller 取消只在尚未接受的邊界生效。
    /// 已接受後的紀錄使用獨立有界期限，不因後到取消隱藏平台接受事實。
    /// </summary>
    Task<LinePushGatewayResult> SendAsync(PushScope scope, IReadOnlyList<string> recipientUserIds,
        IReadOnlyList<ISendMessage> messages, MessageCategory category, string correlationId, CancellationToken ct);
}
