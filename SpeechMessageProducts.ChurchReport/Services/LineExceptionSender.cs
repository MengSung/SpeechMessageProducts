/*
檔案整體說明

目的與定位：保留 ChurchReport 舊公開 sender 的來源與二進位呼叫入口；實際 LINE HTTP
傳輸已搬至 SpeechMessage.Hosting，本檔不再解析 token、不建立第二套 client 或通知管道。

主要功能與流程：
建構時只把 Host 已驗證的設定及原產品管理者後備值交給共用 sender；SendAsync、
Dispose 均直接委派。部署設定優先、舊管理者後備與原五秒逾時行為由共用層維持。

資源與隔離：
本 facade 唯一擁有一個共用 sender，Program 必須先停止並 drain diagnostics，再釋放
facade。實際 request／response／client 清理由 Hosting 負責；本檔不保存 IConfiguration、
Session、request、token 或跨教會狀態，不紀錄自身失敗，也不啟動背景工作。
變更須同時驗證 Hosting sender 測試與 ChurchReport 集中 token 解析契約。
*/
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Services;

/// <summary>
/// 舊產品 sender 的薄 facade；只提供 ChurchReport 核定的管理者後備值並委派共用實作，
/// 不重複任何憑證解析、HTTP 傳輸或通知生命週期。
/// </summary>
public sealed class LineExceptionSender : IDisposable
{
    /// <summary>本 facade 唯一擁有的共用 sender，其 client 不與其他產品或使用者共用。</summary>
    private readonly SpeechMessage.Hosting.LineExceptionSender sender;

    /// <summary>沿用原產品公開建構式，將部署安全驗證與組態 owner 責任留在 Program。</summary>
    /// <param name="configuration">已通過 Development guard 的受信任部署設定。</param>
    public LineExceptionSender(IConfiguration configuration) =>
        sender = new SpeechMessage.Hosting.LineExceptionSender(
            configuration, ChurchReportLineAdminNotificationService.DefaultAdminLineUserId);

    /// <summary>委派共用傳輸並保留原取消／失敗工作，不攔截或遞迴通知。</summary>
    /// <param name="message">已去識別化的診斷摘要。</param>
    /// <param name="cancellationToken">本筆通知的取消訊號，不保存於 facade。</param>
    /// <returns>共用 sender 的原始發送工作。</returns>
    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        sender.SendAsync(message, cancellationToken);

    /// <summary>在 Host 排空通知後委派釋放唯一 sender，不處理任何其他 Host 的資源。</summary>
    public void Dispose() => sender.Dispose();
}
