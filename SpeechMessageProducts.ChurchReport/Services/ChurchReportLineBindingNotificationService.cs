// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/ChurchReportLineBindingNotificationService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IChurchReportLineProfileProvider、class ChurchReportLineProfileProvider、class ChurchReportLineBindingNotificationService
// 主要成員：GetUserProfileAsync、NotifyLineBindingAsync、SendBindingPromptAsync、BuildBindingUrl、BuildBindingMessage
// 引用命名空間：Line.Messaging、LineMessagingProcessor、LineMessagingProcessor.Workflows、System、System.Collections.Generic、System.Net、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Services;

using LineUserProfile = Line.Messaging.UserProfile;

/// <summary>
/// 取得 ChurchReport 綁定流程需要的 LINE 使用者資料。
/// 這個介面留在 ChurchReport，是因為「綁定流程需要哪些 profile 欄位」
/// 屬於產品流程需求；實際 LINE API 呼叫則交給共用 processor 封裝。
/// </summary>
public interface IChurchReportLineProfileProvider
{
    /// <summary>
    /// 依 LINE user id 取得 LINE 使用者 profile。
    /// </summary>
    /// <param name="lineUserId">LINE user id。</param>
    /// <param name="cancellationToken">ASP.NET request 取消權杖。</param>
    Task<LineUserProfile?> GetUserProfileAsync(string lineUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// ChurchReport 的預設 LINE profile provider。
/// 它只是一層薄 adapter，負責把 ChurchReport 服務介面接到已抽離的 LineMessagingProcessor。
/// </summary>
public sealed class ChurchReportLineProfileProvider : IChurchReportLineProfileProvider
{
    private readonly LineMessagingProcessorClass _lineMessagingProcessor;

    public ChurchReportLineProfileProvider(LineMessagingProcessorClass lineMessagingProcessor)
    {
        _lineMessagingProcessor = lineMessagingProcessor ?? throw new ArgumentNullException(nameof(lineMessagingProcessor));
    }

    /// <inheritdoc />
    public async Task<LineUserProfile?> GetUserProfileAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _lineMessagingProcessor.GetUserProfileAsync(lineUserId).ConfigureAwait(false);
    }
}

/// <summary>
/// ChurchReport 專用的 LINE 綁定通知流程。
/// 這個類別刻意只做三件事：
/// 1. 取得 LINE 使用者顯示名稱。
/// 2. 依 ChurchReport 既有路由格式組出綁定網址與訊息。
/// 3. 把訊息交給共用的 ILineNotificationWorkflow 發送。
///
/// 注意：這裡保留 ChurchReport 專用網址與文案，因為未來產品會有自己的綁定頁、
/// 會員資料來源與顯示文字；共用 LINE 專案不應該引用這些產品相依。
/// </summary>
public sealed class ChurchReportLineBindingNotificationService : IChurchReportLineBindingNotificationService
{
    private const string BindingViewBaseUrl = "https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/";
    private const string BindingPromptPrefix = "請點擊以下網址進行牧養系統與Line的註冊:";

    private readonly IChurchReportLineProfileProvider _profileProvider;
    private readonly ILineNotificationWorkflow _lineNotificationWorkflow;

    public ChurchReportLineBindingNotificationService(
        IChurchReportLineProfileProvider profileProvider,
        ILineNotificationWorkflow lineNotificationWorkflow)
    {
        _profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
        _lineNotificationWorkflow = lineNotificationWorkflow ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
    }

    /// <inheritdoc />
    public async Task NotifyLineBindingAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var profile = await _profileProvider
            .GetUserProfileAsync(lineUserId, cancellationToken)
            .ConfigureAwait(false);

        await SendBindingPromptAsync(
                lineUserId,
                profile?.DisplayName ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 依已知的 LINE user id 與 display name 發送綁定提示。
    /// 這個方法獨立出來是為了讓單元測試可以直接驗證 ChurchReport 的 URL 與訊息格式，
    /// 不需要真的呼叫 LINE profile API。
    /// </summary>
    /// <param name="lineUserId">LINE user id。</param>
    /// <param name="displayName">LINE profile 顯示名稱，可為空字串。</param>
    /// <param name="cancellationToken">ASP.NET request 取消權杖。</param>
    public async Task SendBindingPromptAsync(
        string lineUserId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bindingUrl = BuildBindingUrl(displayName, lineUserId);
        var message = BuildBindingMessage(bindingUrl);

        await _lineNotificationWorkflow
            .SendOrThrowAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(lineUserId),
                Content = LineNotificationContent.TextMessage(message),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.LineBindingNotification",
                    ["bindingUrl"] = bindingUrl
                }
            })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 建立 ChurchReport 既有 LINE 綁定頁 URL。
    /// displayName 與 userId 必須 URL encode，避免中文、空白或特殊字元破壞路由。
    /// </summary>
    public static string BuildBindingUrl(string displayName, string lineUserId)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        var encodedDisplayName = WebUtility.UrlEncode(displayName ?? string.Empty);
        var encodedUserId = WebUtility.UrlEncode(lineUserId);
        return BindingViewBaseUrl + encodedDisplayName + "," + encodedUserId;
    }

    /// <summary>
    /// 建立 ChurchReport 的 LINE 綁定提示訊息。
    /// 文案保留在 ChurchReport，因為這是產品語意，不是 LINE SDK 的責任。
    /// </summary>
    public static string BuildBindingMessage(string bindingUrl)
    {
        if (string.IsNullOrWhiteSpace(bindingUrl))
        {
            throw new ArgumentException("Binding URL is required.", nameof(bindingUrl));
        }

        return BindingPromptPrefix + Environment.NewLine + bindingUrl;
    }
}
