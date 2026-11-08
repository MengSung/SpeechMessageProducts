/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 舊推播及認證 RichMenu 的 Obsolete 產品相容殼；付款、
 * MVC 與文案仍由產品決定，既有共用通知／assignment 流程是唯一 HTTP 接點，禁止重建
 * 第二套 provider 呼叫。正式會友推播已在 P2-04 Core 承接，本殼只保留 legacy SDK 能力。
 * 主要功能與流程：組裝文字、媒體及樣板後委派 LineNotificationWorkflow；best-effort
 * 方法保留不拋傳輸錯誤的契約，但將真實 Exception 交共用診斷。必要通知使用 SendOrThrow，
 * 既有 retry key 原樣傳遞，不新增 sender/key。RichMenu 用 legacy-auth menu key 交
 * assignment 解析並 link/unlink，不重建圖片、不自行刪除共用 menu。同步範例等同一個
 * await 作業完成，不啟動未觀察的背景工作；它們仍是相容入口而非新的正式業務流程。
 * 使用邊界與維護：client 及注入 workflow 均由 Host／呼叫端擁有，實例不得跨頻道重綁，
 * 本殼不 Dispose 借來的資源、不保存使用者到 static。有限 timeout 及授權由外層 owner
 * 負責，訊息與結果只在單次呼叫保留；SDK 無 caller-token 接點時不宣稱新增取消能力。
 * P206 離線假 handler 黃金鎖定 URL、遮罩標頭、body 及 retry key，非真實 LINE 驗收。
 */
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    /// <summary>保留舊呼叫形狀的頻道限定相容殼，僅委派共用傳輸；借入依賴由外層 owner 排空並釋放。</summary>
    [Obsolete("改用 LineNotificationWorkflow／RichMenu assignment；正式會友推播使用 P2-04 Core，階段6移除相容殼。")]
    public class PushUtility
    {
        #region 欄位與設定
        private LineMessagingClient m_LineMessagingClient { get; }
        private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
        /// <summary>
        /// 舊版 create/upload/link 流程的相容入口；保留欄位是為了不破壞既有建構式注入形狀。
        /// 目前新增/刪除 legacy-auth RichMenu 主要改走 assignment workflow，避免產品層重複佈建圖片。
        /// </summary>
        private readonly ILineRichMenuWorkflow _lineRichMenuWorkflow;
        /// <summary>
        /// 共用 RichMenu 指派流程，負責把 ChurchReport 使用者切到 catalog 中的 legacy-auth menu key，
        /// 並將 provider 例外轉成一致的 RichMenu exception/result 語意。
        /// </summary>
        private readonly ILineRichMenuAssignmentWorkflow _lineRichMenuAssignmentWorkflow;
        /// <summary>
        /// ChurchReport 既有認證選單的產品層 menu key；實際 richMenuId 由 catalog/provisioning/cache 解析。
        /// </summary>
        private const string LegacyAuthRichMenuKey = "legacy-auth";

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        public PushUtility(LineMessagingClient LineMessagingClient)
            : this(
                  LineMessagingClient,
                  CreateDefaultNotificationWorkflow(LineMessagingClient),
                  lineRichMenuWorkflow: null,
                  lineRichMenuAssignmentWorkflow: null)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
        public PushUtility(LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)
            : this(
                  LineMessagingClient,
                  lineNotificationWorkflow,
                  CreateDefaultRichMenuWorkflow(LineMessagingClient),
                  lineRichMenuAssignmentWorkflow: null)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
        /// <param name="lineRichMenuWorkflow">舊佈建流程注入形狀的相容參數；一般指派不在本殼重做佈建。</param>
        public PushUtility(
            LineMessagingClient LineMessagingClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineRichMenuWorkflow? lineRichMenuWorkflow)
            : this(LineMessagingClient, lineNotificationWorkflow, lineRichMenuWorkflow, null)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
        /// <param name="lineRichMenuAssignmentWorkflow">同頻道的產品 menu-key 指派流程，不以 provider ID 跨頻道共用。</param>
        public PushUtility(
            LineMessagingClient LineMessagingClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow)
            : this(LineMessagingClient, lineNotificationWorkflow, null, lineRichMenuAssignmentWorkflow)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
        /// <param name="lineRichMenuWorkflow">舊佈建流程注入形狀的相容參數；一般指派不在本殼重做佈建。</param>
        /// <param name="lineRichMenuAssignmentWorkflow">同頻道的產品 menu-key 指派流程，不以 provider ID 跨頻道共用。</param>
        public PushUtility(
            LineMessagingClient LineMessagingClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineRichMenuWorkflow? lineRichMenuWorkflow,
            ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow)
        {
            this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
            _lineNotificationWorkflow = lineNotificationWorkflow ?? CreateDefaultNotificationWorkflow(LineMessagingClient);
            _lineRichMenuWorkflow = lineRichMenuWorkflow ?? CreateDefaultRichMenuWorkflow(LineMessagingClient);
            _lineRichMenuAssignmentWorkflow = lineRichMenuAssignmentWorkflow ?? CreateDefaultRichMenuAssignmentWorkflow(LineMessagingClient);
        }

        private static ILineNotificationWorkflow CreateDefaultNotificationWorkflow(LineMessagingClient lineMessagingClient)
        {
            return new LineNotificationWorkflow(new LineMessagingProcessorClass(lineMessagingClient));
        }

        private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)
        {
            // 保留舊 workflow factory，讓仍注入 ILineRichMenuWorkflow 的測試或呼叫端可解析；
            // 新的 legacy-auth 指派行為則由 assignment workflow 處理。
            return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));
        }

        private static ILineRichMenuAssignmentWorkflow CreateDefaultRichMenuAssignmentWorkflow(LineMessagingClient lineMessagingClient)
        {
            // 預設 assignment workflow 使用產品 catalog 解析 legacy-auth，讓 ChurchReport 工具類不再直接操作 provider richMenuId。
            var processor = new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient));
            return new LineRichMenuAssignmentWorkflow(
                processor,
                new InMemoryLineRichMenuIdCache(),
                new InMemoryRichMenuStateStore(),
                new ChurchReportLegacyRichMenuCatalog());
        }

        private async Task SendBestEffortSdkMessagesAsync(
            string userId,
            IReadOnlyList<ISendMessage> messages,
            string source)
        {
            var result = await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(userId),
                Content = LineNotificationContent.SdkMessagesList(messages),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = source
                }
            });
            if (result.Exception is { } exception)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception);
            }
        }
        #endregion

        #region LINE 推播相容入口
        /// <summary>將當次收件者與文字或 SDK 多則訊息交共用送訊流程；不更改原 payload、sender 或重試鍵。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="MessageToSend">呼叫端組好的當次 SDK 訊息集合，完成前不得並行修改。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
        {
            try
            {
                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.BestEffortSdkMessages");
                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>將當次收件者與文字或 SDK 多則訊息交共用送訊流程；不更改原 payload、sender 或重試鍵。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="Message">當次文字內容，不存到共享狀態。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendMessage(string UserId, string Message)
        {
            try
            {
                var result = await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(UserId),
                    Content = LineNotificationContent.TextMessage(Message),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "ChurchReport.PushUtility"
                    }
                });
                if (result.Exception is { } exception)
                {
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception);
                }

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }

        }

        /// <summary>驗證收件者後交共用必要文字通知；傳輸失敗保持可見，交最終產品 owner 上報。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="Message">當次文字內容，不存到共享狀態。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendMessageOrThrowAsync(string UserId, string Message)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("LINE user id is required.", nameof(UserId));
            }

            await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(UserId),
                Content = LineNotificationContent.TextMessage(Message),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.PushUtility.RequiredText"
                }
            });
        }

        /// <summary>驗證收件者後交共用必要 SDK 多則通知，不吞失敗或補造 retry key。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="messages">當次 SDK 訊息清單，呼叫期間由產品持有。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendMessagesOrThrowAsync(string UserId, IReadOnlyList<ISendMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("LINE user id is required.", nameof(UserId));
            }

            await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(UserId),
                Content = LineNotificationContent.SdkMessagesList(messages),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.PushUtility.RequiredSdkMessages"
                }
            });
        }

        /// <summary>傳遞既有必要文字通知及呼叫端 retry key；失敗由共用流程拋出，不改 sender。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="Message">當次文字內容，不存到共享狀態。</param>
        /// <param name="retryKey">既有呼叫端提供的 LINE retry key，不另產生或改寫。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("LINE user id is required.", nameof(UserId));
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                throw new ArgumentException("LINE message is required.", nameof(Message));
            }

            await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(UserId),
                Content = LineNotificationContent.TextMessage(Message),
                RetryKey = retryKey,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.PushUtility.ReliableText"
                }
            });
        }

        /// <summary>保留原圖／預覽 URL 組裝 ImageMessage，僅委派共用流程而不下載或保留圖片。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
        /// <param name="PreviewImageUrl">預覽圖片 URL，原樣交 SDK。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.SendImage");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>將影片與預覽 URL 組裝為 SDK 訊息；本層不另開串流或變更傳輸協定。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
        /// <param name="PreviewImageUrl">預覽圖片 URL，原樣交 SDK。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new VideoMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.SendVideo");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>保留舊拼字的音訊相容入口，將 URL 與毫秒長度原樣交 SDK，不額外存取檔案。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
        /// <param name="Duration">音訊長度（毫秒），保留舊 SDK 數值。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new AudioMessage(OriginalContenUrl, Duration)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.SendAudio");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>將產品給定的標題、地址及座標封裝為位置訊息；不把這些顯示值當授權或身分。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="Title">顯示標題，不作身分識別。</param>
        /// <param name="Address">顯示地址，不寫入診斷。</param>
        /// <param name="Latitude">緯度數值，原樣交 SDK。</param>
        /// <param name="Longitude">經度數值，原樣交 SDK。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new LocationMessage(Title, Address, Latitude, Longitude)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.SendLocation");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>保留既有 package／sticker ID 的字串序列化，由共用流程執行一次傳輸。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="PackageId">既有貼圖套件數值。</param>
        /// <param name="StickerId">既有貼圖識別數值。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendSticker(string UserId, int PackageId, int StickerId)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new StickerMessage(PackageId.ToString(), StickerId.ToString())
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.SendSticker");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>把當次 Buttons 內容及 action 清單組為 SDK template，產品 entity 多載不額外改動 wire。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="AltText">無法顯示樣板時的替代文字。</param>
        /// <param name="ThumbnailImageUrl">Buttons 的縮圖 URL，不開本機檔案。</param>
        /// <param name="Title">顯示標題，不作身分識別。</param>
        /// <param name="Text">SDK 顯示本文，維持舊格式。</param>
        /// <param name="aITemplateAction">當次 template actions，保留序列及內容。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
        {
            try
            {
                ISendMessage ButtonsTemplateMessage = new TemplateMessage
                (
                    AltText,
                    new ButtonsTemplate
                    (
                        text: Text,
                        title: Title,
                        thumbnailImageUrl: ThumbnailImageUrl,
                        actions: aITemplateAction

                    )
                 );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ButtonsTemplateMessage,
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.PostSerializedTemplate");

            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>保留替代文字、確認本文及 action 順序，透過既有共用流程送出 Confirm template。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="AltText">無法顯示樣板時的替代文字。</param>
        /// <param name="Text">SDK 顯示本文，維持舊格式。</param>
        /// <param name="aITemplateAction">當次 template actions，保留序列及內容。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)
        {
            try
            {
                ISendMessage ConfirmTemplateMessage = new TemplateMessage
                (
                    AltText,
                    new ConfirmTemplate(Text, actions: aITemplateAction)
                );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ConfirmTemplateMessage,
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.PostSerializedConfirm");
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>保留圖片基底 URL、寬高及 action 座標，委派共用流程而不自行序列化 HTTP。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="AltText">無法顯示樣板時的替代文字。</param>
        /// <param name="ImageUrl">Imagemap 基底圖片 URL。</param>
        /// <param name="BaseWidth">Imagemap 基底寬度。</param>
        /// <param name="Basehight">保留舊參數拼字的 Imagemap 基底高度。</param>
        /// <param name="aImagemapAction">當次圖片區塊 action 序列。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
        {
            try
            {
                ISendMessage ImageMapTemplateMessage = new ImagemapMessage
                    (
                        ImageUrl, AltText,
                        new ImagemapSize(BaseWidth, Basehight),
                        aImagemapAction
                    );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ImageMapTemplateMessage,
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.PostSerializedImageMap");

            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>保留 ChurchReport 指派既有 legacy-auth 並送成功通知的相容入口；佈建與資源版本不在本殼重做。</summary>
        /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task<String> AddRichMenuMessage(string UserId)
        {
            try
            {
                // RichMenu 的建立、上傳、alias 與版本比對已抽到 LineMessagingProcessor.RichMenus。
                // ChurchReport 這個舊入口只保留「把使用者切到 legacy-auth 選單」的產品動作，
                // 避免每次呼叫都在產品工具類中重新建立或上傳 LINE RichMenu。
                await _lineRichMenuAssignmentWorkflow.AssignOrThrowAsync(UserId, LegacyAuthRichMenuKey);

                var messageToSend = new List<ISendMessage>
                {
                    new TextMessage("Rich menu added"),
                    new StickerMessage("1", "5")
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    messageToSend,
                    "ChurchReport.PushUtility.AddRichMenuMessage");

                return "成功";
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);
                throw;
            }
        }

        /// <summary>只透過既有 assignment 解除使用者選單；不刪除可能被其他使用者共用的 provider menu。</summary>
        /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task<String> DeleteRichMenuMessage(string UserId)
        {
            try
            {
                // 刪除舊使用者連結時也只走共用 assignment workflow。
                // 共用層負責呼叫 LINE unlink API；ChurchReport 不再處理 RichMenu id 或生命週期。
                await _lineRichMenuAssignmentWorkflow.UnassignOrThrowAsync(UserId);

                return "成功";
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);
                throw;
            }
        }

        #endregion

        #region 工具方法
        private string GetFileExtension(string mediaType)
        {
            switch (mediaType)
            {
                case "image/jpeg":
                    return ".jpeg";
                case "audio/x-m4a":
                    return ".m4a";
                case "video/mp4":
                    return ".mp4";
                default:
                    return "";
            }
        }
        #endregion
        #region 舊樣板同步相容入口

        /// <summary>保留確認按鈕範例的同步別名，等待同一非同步流程結束；新呼叫端應使用明確工作流程。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        public void ConfirmMessage(string UserId)
        {
            ISendMessage replyMessage = new TemplateMessage("確認按鈕",
                    new ConfirmTemplate("耶和華", new List<ITemplateAction> {
                        new MessageTemplateAction("同意", "火熱跟隨"),
                        new MessageTemplateAction("反對", "我愛耶和華")
                    }));

            List<ISendMessage> actions1 = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "2")
            };

            SendBestEffortSdkMessagesAsync(
                    UserId,
                    actions1,
                    "ChurchReport.PushUtility.ConfirmMessage.Sync")
                .GetAwaiter()
                .GetResult();

            return;

        }
        /// <summary>保留既有輪播範例的同步別名及原本文，等待傳輸完成，不建立未觀察的背景工作。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        public void CarouselMessage(string UserId)
        {
            List<ITemplateAction> actions1 = new List<ITemplateAction>();
            List<ITemplateAction> actions2 = new List<ITemplateAction>();

            // Add actions.
            actions1.Add(new MessageTemplateAction("Message Label", "sample data"));
            actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data"));
            actions1.Add(new UriTemplateAction("Uri Label", "https://github.com/kenakamu"));

            // Add datetime picker actions
            actions2.Add(new DateTimePickerTemplateAction("DateTime Picker", "DateTime",
                DateTimePickerMode.Datetime, "2017-07-21T13:00", null, null));
            actions2.Add(new DateTimePickerTemplateAction("Date Picker", "Date",
                DateTimePickerMode.Date, "2017-07-21", null, null));
            actions2.Add(new DateTimePickerTemplateAction("Time Picker", "Time",
                DateTimePickerMode.Time, "13:00", null, null));

            ISendMessage replyMessage = new TemplateMessage("Button Template",
                new CarouselTemplate(new List<CarouselColumn> {
                        new CarouselColumn("Casousel 1 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-1 Title", actions1),
                        new CarouselColumn("Casousel 2 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-2 Title", actions1),
                        new CarouselColumn("Casousel 3 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-3 Title", actions1),
                        new CarouselColumn("Casousel 4 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-4 Title", actions1),
                        new CarouselColumn("Casousel 5 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-5 Title", actions1),
                        new CarouselColumn("Casousel 6 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 1-6 Title", actions1),
                        //new CarouselColumn("Casousel 6 Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 1-6 Title", actions1),


                        new CarouselColumn("Casousel 7 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-1 Title", actions2),
                        new CarouselColumn("Casousel 8 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-2 Title", actions2),
                        new CarouselColumn("Casousel 9 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-3 Title", actions2),
                        new CarouselColumn("Casousel 10 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-4 Title", actions2),
                        //new CarouselColumn("Casousel A Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-4 Title", actions2),
                        //new CarouselColumn("Casousel B Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-5 Title", actions2)
                }));

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "14")
            };

            SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.CarouselMessage.Sync")
                .GetAwaiter()
                .GetResult();

            return;

        }
        /// <summary>保留教會輪播範例的同步別名；產品文案不搬入共用層，呼叫結束前完成傳輸。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        public void ChurchCarouselMessage(string UserId)
        {
            List<ITemplateAction> actions1 = new List<ITemplateAction>();
            // Add actions.
            actions1.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data"));
            actions1.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));

            List<ITemplateAction> actions2 = new List<ITemplateAction>();
            actions2.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data"));
            actions2.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/09/2018512%E9%9D%92%E5%B4%87"));

            List<ITemplateAction> actions3 = new List<ITemplateAction>();
            actions3.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data"));
            actions3.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));

            // Add datetime picker actions
            //actions2.Add(new DateTimePickerTemplateAction("DateTime Picker", "DateTime",
            //    DateTimePickerMode.Datetime, "2017-07-21T13:00", null, null));
            //actions2.Add(new DateTimePickerTemplateAction("Date Picker", "Date",
            //    DateTimePickerMode.Date, "2017-07-21", null, null));
            //actions2.Add(new DateTimePickerTemplateAction("Time Picker", "Time",
            //    DateTimePickerMode.Time, "13:00", null, null));



            ISendMessage replyMessage = new TemplateMessage("Button Template",
                new CarouselTemplate(new List<CarouselColumn>
                {
                        new CarouselColumn("講員：魏外楊老師", "https://od.lk/s/ODdfOTA4MTYyMV8/%E9%AD%8F%E5%A4%96%E6%A5%8A%E8%80%81%E5%B8%AB.jpg",
                        "主題：從頭一天直到如今", actions1),

                        new CarouselColumn("講員：湯簡如牧師", "https://od.lk/s/ODdfOTA4MTYyMl8/%E7%B0%A1%E5%A6%82%E7%89%A7%E5%B8%AB.jpg",
                        "你的品格力系列-不可論斷1", actions1),

                        new CarouselColumn("時間：每週二至週五，早上7：40～9：20", "https://od.lk/s/ODdfOTA4MTYyM18/%E6%99%A8%E7%A6%B1.jpg",
                        "晨禱", actions3),


                        //new CarouselColumn("Casousel 7 Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-1 Title", actions2),
                        //new CarouselColumn("Casousel 8 Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-2 Title", actions2),
                        //new CarouselColumn("Casousel 9 Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-3 Title", actions2),
                        //new CarouselColumn("Casousel 10 Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-4 Title", actions2),
                        //new CarouselColumn("Casousel A Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-4 Title", actions2),
                        //new CarouselColumn("Casousel B Text", "https://github.com/apple-touch-icon.png",
                        //"Casousel 2-5 Title", actions2)
                }));

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "14")
            };

            SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.PushUtility.ChurchCarouselMessage.Sync")
                .GetAwaiter()
                .GetResult();

            return;

        }
        #endregion
    }
}
