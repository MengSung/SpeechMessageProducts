/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 舊 reply-token API 的 Obsolete 相容殼，將文字／多則／
 * Confirm／Imagemap 回覆交 LineReplyWorkflow，群組及聊天室 profile 查詢交 processor；
 * 回音文案及事件的產品判斷留在本層，不自行建立 HTTP 或繞過正式會友 Core 授權。
 * 主要功能與流程：只借用事件 reply token 與當次訊息，不快取 token。best-effort reply
 * 保留既有不拋 provider 錯誤契約，但將結果中的真實 Exception 交共用診斷。圖片回音先由
 * processor.UseContentAsync 擁有下載 stream／response，再於同步回呼內以 using 擁有
 * GDI image 及 thumbnail；成功或解碼故障都依 thumbnail→image→stream 順序釋放。
 * 既有 blob 上傳未啟用，本包保留 no-op，不宣稱雲端上傳或額外回覆已實作。
 * 使用邊界與維護：SDK、processor 與 workflow 由同一頻道的外層 owner 建立並提供有限
 * 傳輸 timeout；本殼不取得 client 所有權，不允許同實例重綁其他頻道，也沒有 static
 * 使用者資料、queue、timer 或背景工作。方法完成即不保留事件／回覆 token／媒體。
 * SDK 沒有 caller-token 下載 API，不宣稱可中斷已開始的下載。P206 合成 handler 黃金
 * 及非法圖片故障注入驗證協定與確定性釋放，不能當作實機或 blob 驗收。
 */
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    /// <summary>保留舊呼叫形狀的頻道限定相容殼，僅委派共用傳輸；借入依賴由外層 owner 排空並釋放。</summary>
    [Obsolete("改用 LineReplyWorkflow 與 processor 內容入口；產品回音文案仍留產品，階段6移除相容殼。 ")]
    public class ReplyUtility
    {
        #region 初始化設定
        private LineMessagingClient m_LineMessagingClient { get; }

        private LineMessagingProcessorClass m_LineMessagingProcessor { get; }

        private ILineReplyWorkflow m_LineReplyWorkflow { get; }

        //private PushUtility m_PushUtility { get; }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        public ReplyUtility(LineMessagingClient LineMessagingClient)
            : this(
                  LineMessagingClient,
                  new LineMessagingProcessorClass(LineMessagingClient),
                  CreateDefaultReplyWorkflow(LineMessagingClient))
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            ILineReplyWorkflow? lineReplyWorkflow)
            : this(
                  LineMessagingClient,
                  new LineMessagingProcessorClass(LineMessagingClient),
                  lineReplyWorkflow)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="LineMessagingProcessor">同頻道的借用 processor，與 client 的隔離邊界必須一致。</param>
        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            LineMessagingProcessorClass LineMessagingProcessor)
            : this(LineMessagingClient, LineMessagingProcessor, null)
        {
        }

        /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="LineMessagingProcessor">同頻道的借用 processor，與 client 的隔離邊界必須一致。</param>
        /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
        public ReplyUtility(
            LineMessagingClient LineMessagingClient,
            LineMessagingProcessorClass LineMessagingProcessor,
            ILineReplyWorkflow? lineReplyWorkflow)
        {
            this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
            this.m_LineMessagingProcessor = LineMessagingProcessor ?? throw new ArgumentNullException(nameof(LineMessagingProcessor));
            this.m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(LineMessagingClient);

            //m_PushUtility = new PushUtility(LineMessagingClient);

        }

        private static ILineReplyWorkflow CreateDefaultReplyWorkflow(LineMessagingClient lineMessagingClient)
        {
            return new LineReplyWorkflow(new LineMessagingProcessorClass(lineMessagingClient));
        }
        #endregion
        #region Line Messagin Api Reply SDK傳送
        /// <summary>依已驗證 webhook 的 user／group／room 分支取得顯示名稱並回覆原文案；不快取事件或 profile。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="ev">已由外層驗證的文字 webhook 事件，不留存。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task EchoAsyncProcessor(MessageEvent ev)
        {
            //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
            //String UserName = userProfile?.DisplayName ?? "";

            //String Answer = UserName + " 您剛剛說了: " + ((TextEventMessage)ev.Message).Text + "，我會努力協助您的!";

            //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
            //String UserName = userProfile?.DisplayName ?? "";

            String UserName = "";
            if (ev.Source.Type == EventSourceType.Group)
            {
                var userProfile = await m_LineMessagingProcessor.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
                UserName = userProfile?.DisplayName ?? "";

                //ConfirmMessage(ev.Source.Id);

                //CarouselMessage(ev.Source.Id);

                //ChurchCarouselMessage(ev.Source.Id);
                //SendMessage(ev.Source.Id, "耶和華愛我");

                //IList<UserProfile> aListOfProfile = await m_LineMessagingClient.GetGroupMemberProfilesAsync(ev.Source.Id);

                //foreach( UserProfile aUserProfile  in aListOfProfile )
                //{
                //    String LocalUserName = userProfile?.DisplayName ?? "";
                //    UserName += LocalUserName + "，";
                //}
            }
            else if (ev.Source.Type == EventSourceType.Room)
            {
                var userProfile = await m_LineMessagingProcessor.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
                UserName = userProfile?.DisplayName ?? "";

                //ConfirmMessage(ev.Source.Id);

                //CarouselMessage(ev.Source.Id);

                //ChurchCarouselMessage(ev.Source.Id);

                //SendMessage(ev.Source.Id, "耶和華愛我!");

                //IList<UserProfile> aListOfProfile = await m_LineMessagingClient.GetRoomMemberProfilesAsync(ev.Source.Id);

                //foreach (UserProfile aUserProfile in aListOfProfile)
                //{
                //    String LocalUserName = userProfile?.DisplayName ?? "";
                //    UserName += LocalUserName + "，";
                //}

            }
            else
            {
                //var userProfile = await m_LineMessagingClient.GetUserProfileAsync(ev.Source.UserId);
                //UserName = userProfile?.DisplayName ?? "";

                //m_PushUtility.SendMessage(ev.Source.Id, "耶和華愛我!" + UserName);
            }
            String Answer = UserName + "您剛剛說了: " + ((TextEventMessage)ev.Message).Text + "，願耶穌與您同在!";

            await EchoAsync(ev.ReplyToken, Answer);
        }
        /// <summary>將操作限定的 reply token 與 SDK 訊息清單交共用 reply 流程，保留既有 best-effort 行為。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="MessageToSend">呼叫端組好的當次 SDK 訊息集合，完成前不得並行修改。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task ReplyMessage(string replyToken, List<ISendMessage> MessageToSend)
        {
            try
            {
                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.ReplyMessage");

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
            }
        }

        /// <summary>把文字包成 SDK 回覆訊息交共用 reply 流程，不保存一次性 token 或建立獨立 client。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="TextMessage">本次回覆文字，不用於授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task ReplyMessageAsync(string replyToken, string TextMessage)
        {
            List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(TextMessage)
                };

            await ReplyBestEffortSdkMessagesAsync(
                replyToken,
                MessageToSend,
                "ChurchReport.ReplyUtility.ReplyMessageAsync");

            return;
        }
        /// <summary>以傳入文字回覆同一事件 token，不另查 profile 或改寫產品文案。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="userMessage">已組好的產品回音文字。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public Task EchoAsync(string replyToken, string userMessage)
        {
            return ReplyBestEffortTextAsync(
                replyToken,
                userMessage,
                "ChurchReport.ReplyUtility.EchoAsync");
        }

        /// <summary>保留替代文字、確認本文及 action 順序，透過既有共用流程送出 Confirm template。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="AltText">無法顯示樣板時的替代文字。</param>
        /// <param name="Text">SDK 顯示本文，維持舊格式。</param>
        /// <param name="aITemplateAction">當次 template actions，保留序列及內容。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task PostSerializedConfirm(string replyToken, String AltText, String Text, List<ITemplateAction> aITemplateAction)
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

                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.PostSerializedConfirm");
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
            }
        }
        /// <summary>保留圖片基底 URL、寬高及 action 座標，委派共用流程而不自行序列化 HTTP。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="AltText">無法顯示樣板時的替代文字。</param>
        /// <param name="ImageUrl">Imagemap 基底圖片 URL。</param>
        /// <param name="BaseWidth">Imagemap 基底寬度。</param>
        /// <param name="Basehight">保留舊參數拼字的 Imagemap 基底高度。</param>
        /// <param name="aImagemapAction">當次圖片區塊 action 序列。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task PostSerializedImageMap(string replyToken, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
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

                await ReplyBestEffortSdkMessagesAsync(
                    replyToken,
                    MessageToSend,
                    "ChurchReport.ReplyUtility.PostSerializedImageMap");

            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
            }
        }

        /// <summary>於 processor 擁有的下載 stream 範圍內解碼並建立縮圖；兩個 GDI 物件確定釋放，舊 blob 寫入仍停用。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="messageId">同一已驗證事件的媒體 ID，不記錄或快取。</param>
        /// <param name="blobDirectoryName">舊上傳已停用的保留參數，本次不使用。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task EchoImageAsync(string replyToken, string messageId, string blobDirectoryName)
        {
            try
            {
                await m_LineMessagingProcessor.UseContentAsync(messageId, imageStream =>
                {
                    // stream／response 由 processor 擁有；GDI image 與 thumbnail 只能在回呼內依序釋放。
                    using var image = Image.FromStream(imageStream);
                    using var previewImage = image.GetThumbnailImage(
                        (int)(image.Width * 0.25), (int)(image.Height * 0.25), () => false, IntPtr.Zero);
                    // 舊 blob 上傳從未啟用，不能因收斂下載而建立新的雲端寫入或回覆。
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception, "ChurchReport.ReplyUtility.EchoImageAsync");
                throw;
            }
        }
        /// <summary>保留已停用上傳的 no-op 相容入口；不下載、不寫 blob，也不消費 reply token。</summary>
        /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <param name="messageId">同一已驗證事件的媒體 ID，不記錄或快取。</param>
        /// <param name="blobDirectoryName">舊上傳已停用的保留參數，本次不使用。</param>
        /// <param name="blobName">舊上傳已停用的保留參數，本次不使用。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task UploadMediaContentAsync(string replyToken, string messageId, string blobDirectoryName, string blobName)
        {
            //var stream = await this.m_LineMessagingClient.GetContentStreamAsync(messageId);
            //var ext = GetFileExtension(stream.ContentHeaders.ContentType.MediaType);
            //var uri = await BlobStorage.UploadFromStreamAsync(stream, blobDirectoryName, blobName + ext);
        }
        /// <summary>依舊版範圍選擇 package 1 貼圖後交共用 reply；不改取樣上界以免擴張本包行為。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="replyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task ReplyRandomStickerAsync(string replyToken)
        {
            //Sticker ID of bssic stickers (packge ID =1)
            //see https://devdocs.line.me/files/sticker_list.pdf
            var stickerids = Enumerable.Range(1, 17)
                .Concat(Enumerable.Range(21, 1))
                .Concat(Enumerable.Range(100, 139 - 100 + 1))
                .Concat(Enumerable.Range(401, 430 - 400 + 1)).ToArray();

            var rand = new Random(Guid.NewGuid().GetHashCode());
            var stickerId = stickerids[rand.Next(stickerids.Length - 1)].ToString();
            await ReplyBestEffortSdkMessagesAsync(
                replyToken,
                new ISendMessage[] { new StickerMessage("1", stickerId) },
                "ChurchReport.ReplyUtility.ReplyRandomStickerAsync");
        }

        /// <summary>
        /// ChurchReport 的 reply-token 發送集中點。
        /// 所有 helper 都走共用 LINE reply workflow，讓 product code 不再直接呼叫 LINE reply SDK。
        /// </summary>
        private async Task ReplyBestEffortSdkMessagesAsync(
            string replyToken,
            IReadOnlyList<ISendMessage> messages,
            string source)
        {
            var result = await m_LineReplyWorkflow.ReplyAsync(new LineReplyRequest
            {
                ReplyToken = replyToken,
                Messages = messages,
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

        private Task ReplyBestEffortTextAsync(
            string replyToken,
            string message,
            string source)
        {
            return ReplyBestEffortSdkMessagesAsync(
                replyToken,
                new ISendMessage[] { new TextMessage(message) },
                source);
        }
        #endregion

    }
}
