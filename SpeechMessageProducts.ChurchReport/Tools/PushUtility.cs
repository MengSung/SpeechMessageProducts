// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/PushUtility.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class PushUtility
// 主要成員：CreateDefaultNotificationWorkflow、CreateDefaultRichMenuWorkflow、CreateDefaultRichMenuAssignmentWorkflow、SendBestEffortSdkMessagesAsync、SendMessage、SendMessageOrThrowAsync、SendMessagesOrThrowAsync、SendReliableMessageAsync、SendImage、SendVideo
// 引用命名空間：Line.Messaging、LineMessagingProcessor、LineMessagingProcessor.RichMenus、LineMessagingProcessor.Workflows、System、System.Collections.Generic、System.IO、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

        public PushUtility(LineMessagingClient LineMessagingClient)
            : this(
                  LineMessagingClient,
                  CreateDefaultNotificationWorkflow(LineMessagingClient),
                  lineRichMenuWorkflow: null,
                  lineRichMenuAssignmentWorkflow: null)
        {
        }

        public PushUtility(LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)
            : this(
                  LineMessagingClient,
                  lineNotificationWorkflow,
                  CreateDefaultRichMenuWorkflow(LineMessagingClient),
                  lineRichMenuAssignmentWorkflow: null)
        {
        }

        public PushUtility(
            LineMessagingClient LineMessagingClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineRichMenuWorkflow? lineRichMenuWorkflow)
            : this(LineMessagingClient, lineNotificationWorkflow, lineRichMenuWorkflow, null)
        {
        }

        public PushUtility(
            LineMessagingClient LineMessagingClient,
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow)
            : this(LineMessagingClient, lineNotificationWorkflow, null, lineRichMenuAssignmentWorkflow)
        {
        }

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
            await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(userId),
                Content = LineNotificationContent.SdkMessagesList(messages),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = source
                }
            });
        }
        #endregion

        #region Line Messagin Api Push SDK?喲?
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
        public async Task SendMessage(string UserId, string Message)
        {
            try
            {
                await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(UserId),
                    Content = LineNotificationContent.TextMessage(Message),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "ChurchReport.PushUtility"
                    }
                });

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }

        }

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

        /// <summary>
        /// 送出需要保留 LINE retry-key 語意的必要文字通知。
        /// 這個方法刻意不同於 <see cref="SendMessage(string, string)"/>：
        /// SendMessage 是舊版 best-effort 路徑，仍會吞掉失敗；此方法則用於付款或必要通知，
        /// 讓傳送失敗必須對呼叫端保持可見。
        ///
        /// 注入 ILineNotificationWorkflow 時，請求會走共用且不綁定產品的 LINE workflow，
        /// 並保留 retry key。ChurchReport 專屬的 CRM、付款、奉獻與 MVC 決策仍留在 ChurchReport。
        ///
        /// 舊版 <c>new PushUtility(client)</c> 建構式現在會自動建立這個共用 workflow，
        /// 因此舊呼叫端也會使用同一條 processor-backed 路徑。
        /// </summary>
        /// <param name="UserId">LINE 使用者 ID。必要通知必須有明確收件者。</param>
        /// <param name="Message">要送出的文字內容。</param>
        /// <param name="retryKey">
        /// LINE retry key，用來識別重試送出並降低付款通知重複送達。
        /// </param>
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw;
            }
        }

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
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
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
        #region 蝺渡??

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

