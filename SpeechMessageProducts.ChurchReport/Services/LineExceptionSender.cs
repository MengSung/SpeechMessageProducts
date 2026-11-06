/*
 * 檔案整體說明
 *
 * 目的與定位：管理者診斷用 LINE push adapter，僅接收已落檔的安全文字。現有 workflow 沒有 CancellationToken 契約，此專用 adapter 使用官方 push endpoint，讓 
 * Host 能確實取消並 drain。 擁有一個無使用者 cookie 的 HttpClient；部署 token 只存續至 Program 關閉，不讀 request 設定。
 *
 * 主要功能與流程：
 * 本檔宣告 LineExceptionSender。
 * - 只由組合根建立。環境 token 優先，其次目前 CRM 組織，再其次既有 LINE 預設組織； 收件人沿用既有管理者 ID，可由受信任部署設定覆寫。禁止把此 client 借給業務使用者。
 * - 每次 request／response 都 using 釋放；不讀 provider response body、不寫 ILogger， 避免 token、回應資料被保留或形成通知遞迴。非 2xx 回傳固定型別失敗由 
 * owner 本地記錄。
 * - Program 必須先停止並 drain 診斷 consumer，再釋放 client／pool 並清除 token 參照。
 * 主要操作入口包含 SendAsync、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Contracts/TenancyCallSiteTests.cs、
 * SpeechMessageProducts.ChurchReport/Program.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Services;

/// <summary>
/// 管理者診斷用 LINE push adapter，僅接收已落檔的安全文字。現有 workflow 沒有
/// CancellationToken 契約，此專用 adapter 使用官方 push endpoint，讓 Host 能確實取消並 drain。
/// 擁有一個無使用者 cookie 的 HttpClient；部署 token 只存續至 Program 關閉，不讀 request 設定。
/// </summary>
public sealed class LineExceptionSender : IDisposable
{
    private readonly HttpClient _client;
    private string _token;
    private readonly string _recipient;

    /// <summary>
    /// 只由組合根建立。環境 token 優先，其次目前 CRM 組織，再其次既有 LINE 預設組織；
    /// 收件人沿用既有管理者 ID，可由受信任部署設定覆寫。禁止把此 client 借給業務使用者。
    /// </summary>
    public LineExceptionSender(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var organization = configuration["CrmConnection:Organization"];
        if (!string.IsNullOrWhiteSpace(organization))
            organization = char.ToUpperInvariant(organization[0]) + organization.Substring(1).ToLowerInvariant();
        // 只委派唯一解析器，保留告警專用環境→組織→預設順序；此 sender 不通知自身解析故障。
        _token = global::SpeechMessage.Tenancy.LegacyLineChannelAccess.GetOptionalAccessToken(configuration,
            string.IsNullOrWhiteSpace(organization) ? null : organization,
            global::SpeechMessage.Tenancy.LineChannelSelection.EnvironmentThenOrganizationThenDefault);
        _recipient = configuration["ExceptionNotifications:AdminLineUserId"]
            ?? ChurchReportLineAdminNotificationService.DefaultAdminLineUserId;
        _client = new HttpClient(new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>
    /// 每次 request／response 都 using 釋放；不讀 provider response body、不寫 ILogger，
    /// 避免 token、回應資料被保留或形成通知遞迴。非 2xx 回傳固定型別失敗由 owner 本地記錄。
    /// </summary>
    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(_recipient))
            throw new InvalidOperationException("Exception LINE notification configuration is missing.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Trim());
        request.Content = JsonContent.Create(new { to = _recipient, messages = new[] { new { type = "text", text = message } } });
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("LINE exception delivery failed.", null, response.StatusCode);
    }

    /// <summary>Program 必須先停止並 drain 診斷 consumer，再釋放 client／pool 並清除 token 參照。</summary>
    public void Dispose()
    {
        _client.Dispose();
        _token = null;
    }
}
