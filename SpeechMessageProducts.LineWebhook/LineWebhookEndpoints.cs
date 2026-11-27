/*
檔案整體說明
目的與定位：既有 LineWebhook 主機的路由組合入口，將部署固定的 POST 路徑委派至 ASP.NET Core 共用接收器，避免各產品複製驗簽、去重與入列流程。
主要功能與流程：映射 /api/line/webhook/{channelId}；只有啟動快照明確啟用 legacy 時才加 /api/callback，並以部署指定 Channel 委派。實際原始本文限制、簽章驗證、完整事件身分檢查、claim／queue 與安全失敗狀態均由 WebhookHttpReceiver 執行。
使用邊界與維護：此檔只持有 Host 與不可變選項的路由委派，不建立 client、連線、timer 或背景工作，不保存 request／Session／token；Host 繼續唯一擁有 DI 與啟停。路由 Channel 仍不具授權，必須由共用 receiver 的 verifier 驗證。原有真 HTTP 邊界測試保護路由相容性及資源清理；離線回歸不代表 D365 或 LINE 實機結果。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>保留既有主機公開路由契約；所有接收處理由共用 HTTP adapter 執行。</summary>
public static class LineWebhookEndpoints
{
    /// <summary>只接受 POST；legacy 開關是啟動快照，query／body 不能改寫其指定頻道。</summary>
    /// <param name="app">已完成共用註冊的主機，呼叫端仍擁有其啟動與關閉。</param>
    /// <returns>同一主機，未完成三項正式依賴時仍由 startup 拒絕。</returns>
    public static WebApplication MapLineWebhook(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/api/line/webhook/{channelId}", (Func<HttpContext, Task<IResult>>)(context =>
            WebhookHttpReceiver.ReceiveAsync(context, context.Request.RouteValues["channelId"] as string)));
        var options = app.Services.GetRequiredService<WebhookOptions>();
        if (options.EnableLegacyCallback)
            app.MapPost("/api/callback", (Func<HttpContext, Task<IResult>>)(context => WebhookHttpReceiver.ReceiveAsync(context, options.LegacyChannelId)));
        return app;
    }
}
