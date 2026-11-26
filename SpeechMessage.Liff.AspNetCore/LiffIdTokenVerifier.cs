/*
 * 檔案整體說明
 * 目的與定位：F6 LIFF 共用驗證服務，借入部署組合根的 HttpClient 及 IChurchClock，
 * 將每次 ID Token 交給固定 LINE 官方端點，再以伺服器預期的 Login Channel 核對身分。
 * 主要功能與流程：操作內建立 POST form、五秒 linked deadline、request 與 response，
 * 使用 ResponseHeadersRead 有界讀取至多 64 KiB；完整 JSON 與 aud／sub／exp 驗證後
 * 才發布不可變 LiffIdentity。重複 JSON 欄位與 exp 等於現在均拒絕，沒有成功降級。
 * 使用邊界與維護：token、姓名及回應只在單次操作存在，不寫快取或日誌、不持有請求狀態；
 * client 與 clock 只借用，不關閉、不改 DefaultRequestHeaders。無 Cookie／redirect
 * 的傳輸政策由 DI handler owner 負責；本服務本身不建立或替換 handler。
 * 每筆操作依序關閉 JSON、本文 stream、response/content、request/form 與 linked source；
 * 本文緩衝在 finally 清除，不建立背景 Task 或殘留取消登錄。明確 caller 取消原樣傳播，
 * 內部逾時與真正服務故障保留原例外供最終 filter 告警，400／401 與 claims 拒絕不告警。
 * 驗證使用離線 transport、精確邊界、故障注入、處置探針及 A/B 各六十四筆真並行，
 * 不讀正式設定、不連線正式 LINE 或任何組織；外部服務實機驗收由主線另行記錄。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Localization;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>
/// 每次獨立驗證 LINE ID Token 的無請求狀態服務；借用的 client 必須由 Host 保證安全傳輸政策。
/// 此服務只證明 token 身分，產品仍須從伺服器資料解析該身分目前可存取的租戶與角色。
/// </summary>
public sealed class LiffIdTokenVerifier
{
    /// <summary>64 KiB 本文硬上限；另讀一 byte 偵測未宣告 Content-Length 的超量回應。</summary>
    private const int MaximumResponseBytes = 65_536;

    /// <summary>借入的無使用者狀態 client，由 DI owner 統一排空及處置。</summary>
    private readonly HttpClient httpClient;

    /// <summary>借入的 UTC 來源；只在本次驗證完成前取值，不保留任何租戶或驗證結果。</summary>
    private readonly IChurchClock clock;

    /// <summary>借用傳輸及 UTC 來源，不更改其共用設定或取得處置所有權。</summary>
    /// <param name="httpClient">Host 管理、無 Cookie 且禁止 redirect 的可並行 client。</param>
    /// <param name="clock">明確的 UTC 來源，供 exp 嚴格邊界判定。</param>
    public LiffIdTokenVerifier(HttpClient httpClient, IChurchClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(clock);
        this.httpClient = httpClient;
        this.clock = clock;
    }

    /// <summary>
    /// 將本次 token 與部署提供的 Login Channel 交給固定官方端點，完整驗證後才發布身分。
    /// 呼叫端須只從伺服器設定提供預期 Channel，不能讓 request 自行選擇 audience。
    /// </summary>
    /// <param name="idToken">單次請求中的敏感 ID Token，不會存入共用欄位或診斷。</param>
    /// <param name="expectedLoginChannelId">伺服器部署設定的 Login Channel，不是 Messaging Channel。</param>
    /// <param name="cancellationToken">呼叫端明確取消；取消時保持 OperationCanceledException。</param>
    /// <returns>完全驗證且與 HTTP 資源分離的不可變身分。</returns>
    /// <exception cref="LiffVerificationException">輸入或 token 拒絕，或已保留真實故障的服務失敗。</exception>
    /// <exception cref="OperationCanceledException">明確呼叫端取消，不能視為需要告警的內部逾時。</exception>
    public async Task<LiffIdentity> VerifyAsync(string idToken, string expectedLoginChannelId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(expectedLoginChannelId))
            throw new LiffVerificationException();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/oauth2/v2.1/verify")
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("id_token", idToken),
                new KeyValuePair<string, string>("client_id", expectedLoginChannelId)
            ])
        };

        try
        {
            using var response = await httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // 預期 token 拒絕不讀錯誤本文；其餘狀態保留真實狀態碼供最終診斷分類，
                // 不使用供應商 ReasonPhrase、本文或請求欄位作為例外訊息。
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                    throw new LiffVerificationException();
                throw new LiffVerificationException(true, new HttpRequestException(
                    "LIFF verification endpoint returned an unexpected status.", null, response.StatusCode));
            }

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw OversizedResponse();

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var payload = new byte[MaximumResponseBytes + 1];
            try
            {
                var length = await ReadBoundedAsync(stream, payload, deadline.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(payload.AsMemory(0, length),
                    new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 16 });
                var identity = ValidateClaims(document.RootElement, expectedLoginChannelId, clock.UtcNow);
                // 非同步 I/O 之後的解析或時鐘讀取期間若 caller／deadline 已取消，
                // 不得發布本來將成功的身分；最終 catch 只以明確 caller token 決定分類。
                deadline.Token.ThrowIfCancellationRequested();
                return identity;
            }
            finally
            {
                // 不使用未清除的共用陣列池；成功、拒絕與取消都清除本次原始個資。
                Array.Clear(payload);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LiffVerificationException(true, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LiffVerificationException(true, exception);
        }
        catch (IOException exception)
        {
            throw new LiffVerificationException(true, exception);
        }
        catch (JsonException exception)
        {
            throw new LiffVerificationException(true, exception);
        }
    }

    /// <summary>
    /// 最多讀到硬上限加一 byte；不信任 Content-Length，且所有實際讀取共用原操作期限。
    /// 不啟動取消後仍會存活的讀取工作，呼叫端待此工作結束後才關閉 stream 與 response。
    /// </summary>
    /// <param name="stream">仍由本次 response 與局部 await using 管理的借入本文。</param>
    /// <param name="payload">本次獨有、固定 64 KiB 加一 byte 的陣列，由外層 finally 清除。</param>
    /// <param name="cancellationToken">涵蓋完整操作的 linked deadline，不能只用 HttpClient 標頭逾時。</param>
    /// <returns>不超過 64 KiB 的實際本文長度。</returns>
    private static async Task<int> ReadBoundedAsync(Stream stream, byte[] payload,
        CancellationToken cancellationToken)
    {
        var length = 0;
        while (true)
        {
            var count = await stream.ReadAsync(payload.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (count == 0) return length;
            length += count;
            if (length > MaximumResponseBytes) throw OversizedResponse();
        }
    }

    /// <summary>
    /// 僅在完整文件已拒絕所有層級的重複欄位後，驗證 aud、sub、整數 exp 及可選 name。
    /// 不修剪身分或 Channel，不用顯示姓名取代 sub；全部通過才建立與 JSON 資源分離的純值。
    /// </summary>
    /// <param name="root">本操作有效期間內的 JSON 根物件，不能被回傳結果保留。</param>
    /// <param name="expectedLoginChannelId">伺服器部署決定的預期 audience。</param>
    /// <param name="now">驗證完成前從借入時鐘取得的 UTC 瞬間。</param>
    /// <returns>完整驗證過的純字串與 UTC 到期值。</returns>
    private static LiffIdentity ValidateClaims(JsonElement root, string expectedLoginChannelId,
        DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("aud", out var audience) || audience.ValueKind != JsonValueKind.String ||
            !string.Equals(audience.GetString(), expectedLoginChannelId, StringComparison.Ordinal) ||
            !root.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(subject.GetString()) ||
            !root.TryGetProperty("exp", out var expiration) || expiration.ValueKind != JsonValueKind.Number ||
            !expiration.TryGetInt64(out var seconds) ||
            seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
            seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            throw new LiffVerificationException();

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (expiresAt <= now) throw new LiffVerificationException();

        string? displayName = null;
        if (root.TryGetProperty("name", out var name))
        {
            if (name.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new LiffVerificationException();
            displayName = name.GetString();
        }
        return new LiffIdentity(subject.GetString()!, displayName, expiresAt);
    }

    /// <summary>建立固定協定故障，不附加長度、本文或身分；超量回覆屬供應商服務故障。</summary>
    /// <returns>供最終 filter 上報的驗證失敗與固定原始原因。</returns>
    private static LiffVerificationException OversizedResponse() =>
        new(true, new InvalidDataException("LIFF verification response exceeded the permitted size."));
}
