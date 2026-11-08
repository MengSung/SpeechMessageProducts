/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtility 舊推播 API 的 Obsolete 相容殼；產品統計與既有樣板仍留本層，
 * 傳輸統一委派 LineLegacySendWorkflow → net10 processor → 原 SDK。正式會友推播由
 * P2-04 Core 核對 contact／church／channel，本殼不捏造授權 ID，也不讀 SDK 私有 token。
 * 主要功能與流程：保留文字、多則、圖片、影片、音訊、位置、貼圖、Buttons／Confirm／
 * Imagemap 及同步別名的本文；multicast 保留單次 HTTP，不改為逐人 push。只有原文字／
 * 多則／多播入口失敗重拋，其餘媒體與樣板沿舊 best-effort；真正失敗交共用診斷入口，
 * 不組含個資的錯誤訊息。主方案已有 RichMenu 的 create／upload／link 及 query／unlink／delete 路徑保持，
 * 只委派 net10，不擅自退役公開方法。圖片預設仍是固定本機路徑，另可注入有限長度串流。
 * 舊刪除入口仍假設選單獨佔，不可用於 ChurchReport assignment 管理的共用 menu。
 * 使用邊界與維護：SDK client、CRM 工具與其有限 timeout 由呼叫端／Factory scope 擁有，
 * 本殼只借用，不能被用來跨頻道重綁或跨租戶共用。每次訊息及收件人只在 await 期間保留；
 * 不建立 HTTP、重試、queue、timer 或背景工作。沒有 caller-token API，不宣稱可取消 SDK
 * 已開始的傳輸；owner 必須等操作完成才釋放 client。P206 假 handler 黃金保護 URL、
 * 遮罩標頭及 body；本檔不自行執行產品授權，保留至階段6移除。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Line.Messaging;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ToolUtility
{
    /// <summary>保留舊呼叫形狀的頻道限定相容殼，僅委派共用傳輸；借入依賴由外層 owner 排空並釋放。</summary>
    [Obsolete("改用 LineLegacySendWorkflow；正式會友推播使用 P2-04 Core，舊相容殼於階段6移除。 ")]
    public class PushUtility
    {
        #region 初始化設定
        private readonly LineMessagingProcessor.Workflows.LineLegacySendWorkflow workflow;
        private readonly ToolUtilityClass m_ToolUtilityClass;
        private readonly Func<Stream> richMenuImageSource;

        /// <summary>保留舊頻道限定建構入口；借用 SDK 及 CRM 工具，傳輸只建立輕量 workflow adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        public PushUtility(LineMessagingClient LineMessagingClient)
            : this(LineMessagingClient, ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")) { }

        /// <summary>保留舊頻道限定建構入口；借用 SDK 及 CRM 工具，傳輸只建立輕量 workflow adapter。</summary>
        /// <param name="LineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
        /// <param name="toolUtility">借入的 CRM 統計工具，不觸發隱藏 Factory。</param>
        public PushUtility(LineMessagingClient LineMessagingClient, ToolUtilityClass toolUtility)
            : this(LineMessagingClient, toolUtility, () => File.OpenRead(@"D:\暫存區\richmenu.PNG")) { }

        /// <summary>
        /// 借用 SDK／CRM 並注入有限長度圖片來源；預設來源仍是舊固定路徑，不改 HTTP 次序。
        /// 本殼只保存來源委派，workflow 在每次 upload 結束關閉開啟的串流。
        /// </summary>
        /// <param name="LineMessagingClient">同頻道借用 SDK，由外層 owner 設 timeout 並於排空後釋放。</param>
        /// <param name="toolUtility">借用的操作限定 CRM 統計工具，不轉移 scope 所有權。</param>
        /// <param name="richMenuImageSource">每次產生操作限定可讀串流的來源，不得捕捉 Session／HttpContext 或回傳共享串流。</param>
        public PushUtility(LineMessagingClient LineMessagingClient, ToolUtilityClass toolUtility, Func<Stream> richMenuImageSource)
        {
            if (LineMessagingClient == null) throw new ArgumentNullException(nameof(LineMessagingClient));
            m_ToolUtilityClass = toolUtility ?? throw new ArgumentNullException(nameof(toolUtility));
            this.richMenuImageSource = richMenuImageSource ?? throw new ArgumentNullException(nameof(richMenuImageSource));
            workflow = new LineMessagingProcessor.Workflows.LineLegacySendWorkflow(
                new LineMessagingProcessor.LineMessagingProcessorClass(LineMessagingClient));
        }

        /// <summary>只轉交共用 net10 流程；保持原始 SDK 失敗型別，不建立第二份 HTTP 或跨操作資料。</summary>
        private Task SendSdkMessagesAsync(string userId, IList<ISendMessage> messages) => workflow.SendAsync(userId, messages);
        #endregion
        #region Line Messagin Api Push SDK傳送
        /// <summary>將當次收件者與文字或 SDK 多則訊息交共用送訊流程；不更改原 payload、sender 或重試鍵。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <param name="MessageToSend">呼叫端組好的當次 SDK 訊息集合，完成前不得並行修改。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
        {
            try
            {
                await SendSdkMessagesAsync(UserId, MessageToSend);
                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
            }

        }
        /// <summary>保留原收件順序及空集合不發送契約；本工具維持單次 multicast HTTP，不改為 ChurchReport 的逐人 push。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="To">產品先驗證的當次收件集合，不以內容去重。</param>
        /// <param name="Message">當次文字內容，不存到共享狀態。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task MultiCastTextMessageAsync(IList<string> To, string Message)
        {
            try
            {
                if (To.Count > 0)
                {
                    this.m_ToolUtilityClass.CreatePushLineMessage(To, "Line推播統計:文字", Message);

                    List<ISendMessage> MessageToSend = new List<ISendMessage>
                    {
                        new TextMessage(Message)
                    };

                    await workflow.MultiCastAsync(To, MessageToSend);
                }
                return;
            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                throw;
            }

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:圖片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:影片", ""); List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new VideoMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:聲音", ""); List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new AudioMessage(OriginalContenUrl, Duration)
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:座標", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new LocationMessage(Title, Address, Latitude, Longitude)
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:貼圖", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new StickerMessage(PackageId.ToString(), StickerId.ToString())
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Template", "");
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

                await SendSdkMessagesAsync(UserId, MessageToSend);

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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Confirm", "");
                ISendMessage ConfirmTemplateMessage = new TemplateMessage
                (
                    AltText,
                    new ConfirmTemplate(Text, actions: aITemplateAction)
                );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ConfirmTemplateMessage,
                };

                await SendSdkMessagesAsync(UserId, MessageToSend);
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:ImageMap", "");
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

                await SendSdkMessagesAsync(UserId, MessageToSend);

            }
            catch (System.Exception e)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                // 保留原 best-effort：已交共用診斷，不改為拋出。
            }
        }
        /// <summary>
        /// 保留主方案既有選單定義、圖片預設及成功通知，四步傳輸委派 net10。
        /// 建立後讀檔／上傳失敗仍保留舊例外及 provider 狀態，不擅加 rollback 或 retry。
        /// </summary>
        /// <param name="UserId">產品已授權的當次選單使用者。</param>
        /// <returns>四步成功後的既有「成功」文字，否則保留原失敗。</returns>
        public async Task<String> AddRichMenuMessage(string UserId)
        {
            try
            {
                var menu = new RichMenu
                {
                    Size = ImagemapSize.RichMenuLong,
                    Selected = false,
                    Name = "nice richmenu",
                    ChatBarText = "touch me",
                    Areas = new List<ActionArea>
                    {
                        new ActionArea
                        {
                            Bounds = new ImagemapArea(0, 0, ImagemapSize.RichMenuLong.Width, ImagemapSize.RichMenuLong.Height),
                            Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A")
                        }
                    }
                };
                await workflow.CreateAndAssignLegacyRichMenuAsync(UserId, menu, richMenuImageSource,
                    new ISendMessage[] { new TextMessage("Rich menu added"), new StickerMessage("1", "5") });
                return "成功";
            }
            catch (Exception exception)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception);
                throw;
            }
        }

        /// <summary>
        /// 保留主方案 query／unlink／delete 的公開成功路徑，不改為 throw 或僅 unlink。
        /// 此 legacy 動作假設選單獨佔，不得用來刪除 assignment 管理的共用選單。
        /// </summary>
        /// <param name="UserId">原產品已確認擁有獨佔選單的使用者。</param>
        /// <returns>三步成功後的既有「成功」文字，否則保留原失敗。</returns>
        public async Task<String> DeleteRichMenuMessage(string UserId)
        {
            try
            {
                await workflow.DeleteAssignedLegacyRichMenuAsync(UserId);
                return "成功";
            }
            catch (Exception exception)
            {
                global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception);
                throw;
            }
        }

        #endregion
        #region 工具區
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
        #region 練習區

        /// <summary>保留確認按鈕及貼圖範例的既有 payload，交同一 legacy 工作流程等待完成。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task ConfirmMessageAsync(string UserId)
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

            await SendSdkMessagesAsync(UserId, actions1);
        }

        /// <summary>保留輪播範例、日期 action 及貼圖的原內容，交同一 legacy 工作流程等待完成。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task CarouselMessageAsync(string UserId)
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


                        new CarouselColumn("Casousel 7 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-1 Title", actions2),
                        new CarouselColumn("Casousel 8 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-2 Title", actions2),
                        new CarouselColumn("Casousel 9 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-3 Title", actions2),
                        new CarouselColumn("Casousel 10 Text", "https://github.com/apple-touch-icon.png",
                        "Casousel 2-4 Title", actions2),
                }));

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "14")
            };

            await SendSdkMessagesAsync(UserId, MessageToSend);
        }

        /// <summary>保留教會輪播文案及貼圖範例，僅把傳輸交共用 legacy 流程。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
        public async Task ChurchCarouselMessageAsync(string UserId)
        {
            List<ITemplateAction> actions1 = new List<ITemplateAction>();
            // Add actions.
            actions1.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            actions1.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));

            List<ITemplateAction> actions2 = new List<ITemplateAction>();
            actions2.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            actions2.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/09/2018512%E9%9D%92%E5%B4%87"));

            List<ITemplateAction> actions3 = new List<ITemplateAction>();
            actions3.Add(new MessageTemplateAction("報名", "簡如牧師邀請您"));
            actions3.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));

            ISendMessage replyMessage = new TemplateMessage("Button Template",
                new CarouselTemplate(new List<CarouselColumn>
                {
                        new CarouselColumn("講員：魏外楊老師", "https://od.lk/s/ODdfOTA4MTYyMV8/%E9%AD%8F%E5%A4%96%E6%A5%8A%E8%80%81%E5%B8%AB.jpg",
                        "主題：從頭一天直到如今", actions1),

                        new CarouselColumn("講員：湯簡如牧師", "https://od.lk/s/ODdfOTA4MTYyMl8/%E7%B0%A1%E5%A6%82%E7%89%A7%E5%B8%AB.jpg",
                        "你的品格力系列-不可論斷1", actions1),

                        new CarouselColumn("時間：每週二至週五，早上7：40～9：20", "https://od.lk/s/ODdfOTA4MTYyM18/%E6%99%A8%E7%A6%B1.jpg",
                        "晨禱", actions3),
                }));

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "14")
            };

            await SendSdkMessagesAsync(UserId, MessageToSend);
        }

        #region 向後相容 - 已標記過時的同步方法
        /// <summary>保留確認按鈕範例的同步別名，等待同一非同步流程結束；新呼叫端應使用明確工作流程。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        [Obsolete("請使用 ConfirmMessageAsync 非同步方法")]
        public void ConfirmMessage(string UserId)
        {
            ConfirmMessageAsync(UserId).GetAwaiter().GetResult();
        }

        /// <summary>保留既有輪播範例的同步別名及原本文，等待傳輸完成，不建立未觀察的背景工作。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        [Obsolete("請使用 CarouselMessageAsync 非同步方法")]
        public void CarouselMessage(string UserId)
        {
            CarouselMessageAsync(UserId).GetAwaiter().GetResult();
        }

        /// <summary>保留教會輪播範例的同步別名；產品文案不搬入共用層，呼叫結束前完成傳輸。</summary>
        /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
        /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
        [Obsolete("請使用 ChurchCarouselMessageAsync 非同步方法")]
        public void ChurchCarouselMessage(string UserId)
        {
            ChurchCarouselMessageAsync(UserId).GetAwaiter().GetResult();
        }
        #endregion

        #endregion
    }
}
