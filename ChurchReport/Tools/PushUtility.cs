using Line.Messaging;
using LineMessagingProcessor.Workflows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    public class PushUtility
    {
        #region 初始化設定
        private LineMessagingClient m_LineMessagingClient { get; }
        private readonly ILineNotificationWorkflow? _lineNotificationWorkflow;

        public PushUtility(LineMessagingClient LineMessagingClient)
            : this(LineMessagingClient, null)
        {
        }

        public PushUtility(LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)
        {
            this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
            _lineNotificationWorkflow = lineNotificationWorkflow;
        }
        #endregion

        #region Line Messagin Api Push SDK傳送
        public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
        {
            try
            {
                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
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
                if (_lineNotificationWorkflow != null)
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

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

            if (_lineNotificationWorkflow != null)
            {
                await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(UserId),
                    Content = LineNotificationContent.TextMessage(Message),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "ChurchReport.PushUtility.RequiredText"
                    }
                });
                return;
            }

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                new TextMessage(Message)
            };

            await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
        }

        public async Task SendMessagesOrThrowAsync(string UserId, IReadOnlyList<ISendMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("LINE user id is required.", nameof(UserId));
            }

            if (_lineNotificationWorkflow != null)
            {
                await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(UserId),
                    Content = LineNotificationContent.SdkMessagesList(messages),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "ChurchReport.PushUtility.RequiredSdkMessages"
                    }
                });
                return;
            }

            await this.m_LineMessagingClient.PushMessageAsync(UserId, new List<ISendMessage>(messages));
        }

        /// <summary>
        /// Sends a required text notification with LINE retry-key semantics.
        /// This method is intentionally different from <see cref="SendMessage(string, string)"/>:
        /// SendMessage is the legacy best-effort path and still swallows failures; this method is
        /// for payment or required notifications where failure must remain visible to the caller.
        ///
        /// When an ILineNotificationWorkflow is injected, the request is routed through the shared
        /// product-agnostic LINE workflow with the retry key preserved. ChurchReport-specific CRM,
        /// payment, donation, and MVC decisions stay in ChurchReport.
        ///
        /// When no workflow is injected, the method falls back to the SDK retry-key push overload
        /// so existing new PushUtility(client) call sites remain compatible.
        /// </summary>
        /// <param name="UserId">LINE user id. Required notifications must have an explicit recipient.</param>
        /// <param name="Message">Text content to send.</param>
        /// <param name="retryKey">
        /// LINE retry key used to identify retried sends and reduce duplicate payment notifications.
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

            if (_lineNotificationWorkflow != null)
            {
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
                return;
            }

            List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                new TextMessage(Message)
            };

            await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend, retryKey);
        }

        public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
        {
            try
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

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
                RichMenu richMenu = new RichMenu()
                {
                    Size = ImagemapSize.RichMenuLong,
                    Selected = false,
                    Name = "nice richmenu",
                    ChatBarText = "touch me",
                    Areas = new List<ActionArea>()
                        {
                            new ActionArea()
                            {
                                Bounds = new ImagemapArea(0,0 ,ImagemapSize.RichMenuLong.Width,ImagemapSize.RichMenuLong.Height),
                                Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A")
                            }
                        }
                };

                String richMenuId = await this.m_LineMessagingClient.CreateRichMenuAsync(richMenu);
                //var image = new MemoryStream(File.ReadAllBytes(HttpContext.Current.Server.MapPath(@"~\Images\richmenu.PNG")));
                //var image = new MemoryStream(File.ReadAllBytes(@"D:\\LINE 佈署\\Logo\\音訊科技\\SpeechMessage.png"));

                String path = @"D:\暫存區\richmenu.PNG";

                byte[] readText = System.IO.File.ReadAllBytes(path);
                var image = new MemoryStream(readText);


                //var image = new MemoryStream(byDataValue);

                // Upload Image
                await this.m_LineMessagingClient.UploadRichMenuPngImageAsync(image, richMenuId);
                // Link to user
                await this.m_LineMessagingClient.LinkRichMenuToUserAsync(UserId, richMenuId);

                ISendMessage replyMessage = new TextMessage("Rich menu added");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    replyMessage,
                    new StickerMessage("1", "5")
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return "成功";

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public async Task<String> DeleteRichMenuMessage(string UserId)
        {
            try
            {
                // Get Rich Menu for the user
                var richMenuId = await this.m_LineMessagingClient.GetRichMenuIdOfUserAsync(UserId);
                await m_LineMessagingClient.UnLinkRichMenuFromUserAsync(UserId);
                await m_LineMessagingClient.DeleteRichMenuAsync(richMenuId);

                return "成功";
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
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

            this.m_LineMessagingClient.PushMessageAsync(UserId, actions1).Wait();

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

            this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend).Wait();

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

            this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend).Wait();

            return;

        }
        #endregion
    }
}
