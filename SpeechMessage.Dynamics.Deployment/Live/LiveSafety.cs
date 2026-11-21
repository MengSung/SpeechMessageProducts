/*
檔案整體說明
目的與定位：部署 Live 檢查的命令區域 D34 與測試 Channel 用量能力，不建立跨命令身分或秘密快取。
主要流程：權威測試會友及教會以既有 ShadowTestIdentity 驗證；全部頻道解密後必須等於
測試 token。用量只讀固定 LINE consumption 端點，不接受 caller 提供 URL 或 redirect。
資源與隔離：每次 D34 的 key byte[] 在 finally 清零，HttpClient／handler 由此 owner Dispose，
每個 request／response／stream 皆同步排空後釋放，單次回應最多4096 bytes，15秒總上限。
失敗處理：任何缺值、分頁、重複ID、錯token或回應格式拒絕；不輸出秘密或原始SDK／HTTP本文。
驗證方式：由離線 CLI／注入邊界測試及測試組織演練驗證，正常業務推播不在本檔發送。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using LineMessagingProcessor.Core.Security;
using LineMessagingProcessor.Core;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>單命令 owner；秘密讀取 delegate 由主程式借用，Dispose 不關閉外層 SDK。</summary>
internal sealed class LiveSafety(IOrganizationService service, Func<string, string> secret) : IDisposable
{
    private readonly LineChannelUsageReader usage = new();
    /// <summary>
    /// 先驗權威測試身分，再逐個 Channel 驗解密後 token；不把空清單當通過。
    /// 失敗摘要附呼叫端目前模組，讓安全子檢查可與該次 Live 準備／執行記錄對應。
    /// </summary>
    /// <param name="token">只取消本次驗證的呼叫端 token。</param>
    /// <param name="module">由部署清單選出的模組識別；沒有模組脈絡的獨立離線呼叫可為null。</param>
    internal ShadowVerifiedIdentity Verify(CancellationToken token, string? module = null)
    {
        var identity = LivePreparationFailure.Capture(LivePreparationReason.Safety, module, null, null, null,
            () => ShadowTestIdentity.Verify(service, secret("TestLineUserId"), secret("ShadowTestChurchId"),
                secret("ShadowTestContactId"), token), token, "D34-TEST-IDENTITY");
        var key = LivePreparationFailure.Capture(LivePreparationReason.Safety, module, null, null, null,
            () => Convert.FromBase64String(secret("TokenKey")), token, "CONFIG-TOKEN-KEY");
        try
        {
            if (key.Length != 64)
                throw new LivePreparationFailure(LivePreparationReason.Safety, module, inner: Invalid(),
                    subCheckId: "CONFIG-TOKEN-KEY");
            var query = new QueryExpression("new_linechannel") { ColumnSet = new ColumnSet("new_accesstokenencrypted"), TopCount = 101 };
            var response = LivePreparationFailure.Capture(LivePreparationReason.Safety, module, null, null, null,
                () => service.RetrieveMultiple(query), token, "D34-CHANNEL-INVENTORY");
            token.ThrowIfCancellationRequested();
            if (response.MoreRecords || response.Entities.Count is < 1 or > 100 ||
                response.Entities.Any(row => row.Id == Guid.Empty || row.LogicalName != "new_linechannel") ||
                response.Entities.Select(row => row.Id).Distinct().Count() != response.Entities.Count)
                throw new LivePreparationFailure(LivePreparationReason.Safety, module, inner: Invalid(),
                    subCheckId: "D34-CHANNEL-INVENTORY");
            foreach (var channel in response.Entities)
            {
                var matches = LivePreparationFailure.Capture(LivePreparationReason.Safety, module, null, null, null,
                    () => ChannelTokenProtector.Unprotect(channel.GetAttributeValue<string>("new_accesstokenencrypted"), key) ==
                        secret("TestChannelToken"), token, "D34-CHANNEL-TOKENS");
                if (!matches)
                    throw new LivePreparationFailure(LivePreparationReason.Safety, module, inner: Invalid(),
                        subCheckId: "D34-CHANNEL-TOKENS");
            }
            return identity;
        }
        finally { Array.Clear(key); }
    }
    /// <summary>Authorization 僅在單次 request 保存，串流回應有硬上限及總 deadline。</summary>
    internal long Usage(CancellationToken token)
    {
        return usage.Read(secret("TestChannelToken"), token);
    }
    /// <summary>關閉本命令 HTTP 連線池，不保留到其他組織命令。</summary>
    public void Dispose() => usage.Dispose();
    /// <summary>固定安全錯誤不包含秘密或外部本文。</summary>
    private static InvalidDataException Invalid() => new("Live safety or usage evidence is unavailable.");
}
