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
    public class PushUtility
    {
        #region 初始化設定
        private LineMessagingClient m_LineMessagingClient { get; }

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        public PushUtility(LineMessagingClient LineMessagingClient)
        {
            this.m_LineMessagingClient = LineMessagingClient;
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

                throw e;
            }
        }
        public async Task SendMessage(string UserId, string Message)
        {
            try
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
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

                throw e;
            }

        }
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

                    await this.m_LineMessagingClient.MultiCastMessageAsync(To, MessageToSend);
                }
                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
        {
            try
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:圖片", "");
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:影片", ""); List<ISendMessage> MessageToSend = new List<ISendMessage>
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:聲音", ""); List<ISendMessage> MessageToSend = new List<ISendMessage>
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:座標", "");
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
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:貼圖", "");
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

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //throw e;
            }
        }
        /// <summary>
        /// 舊版直接建立 RichMenu、上傳圖片並綁定到指定使用者的流程。
        ///
        /// 這段程式保留在 ToolUtility 舊工具中，描述早期產品直接操作 LINE provider 的生命週期：
        /// 先建立 RichMenu 取得 provider 產生的 richMenuId，再從本機固定路徑讀取圖片，
        /// 上傳為 RichMenu 圖片，最後把該 richMenuId link 到使用者並推送成功訊息。
        ///
        /// ChurchReport 目前已改由共用 RichMenu workflow / assignment workflow 管理 menu key、
        /// richMenuId 快取、線上選單同步與解除綁定，避免每次呼叫都建立新 RichMenu 或依賴硬編碼圖片路徑。
        /// </summary>
        public async Task<String> AddRichMenuMessage(string UserId)
        {
            try
            {
                // 建立 LINE provider 需要的 RichMenu 定義；這裡只有一個全版面 postback 區塊，
                // 屬於舊版示範式選單，不具備目前共用 catalog 的版本化命名與 fingerprint 機制。
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

                // 舊版流程直接依賴伺服器本機固定路徑；部署環境若沒有這個檔案，
                // RichMenu 建立後會在圖片讀取或上傳階段失敗，且 provider 端可能留下未使用的 richMenuId。
                String path = @"D:\暫存區\richmenu.PNG";

                byte[] readText = System.IO.File.ReadAllBytes(path);
                var image = new MemoryStream(readText);


                //var image = new MemoryStream(byDataValue);

                // 將本機 PNG 圖片上傳到剛建立的 provider richMenuId。
                await this.m_LineMessagingClient.UploadRichMenuPngImageAsync(image, richMenuId);
                // 將 provider richMenuId 綁定到單一使用者；這裡沒有 menu key 抽象，也沒有快取或重試策略。
                await this.m_LineMessagingClient.LinkRichMenuToUserAsync(UserId, richMenuId);

                // 舊版方法會額外推送文字與貼圖通知，讓使用者知道選單已被建立並綁定。
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
        /// <summary>
        /// 舊版直接解除使用者 RichMenu 並刪除 provider richMenuId 的流程。
        ///
        /// 此方法先向 LINE 查詢使用者目前綁定的 richMenuId，接著 unlink 使用者，
        /// 最後直接刪除該 provider RichMenu。這個做法假設該 richMenuId 只屬於單一使用者；
        /// 若同一選單被多位使用者或多個流程共用，直接刪除 provider 資源會影響其他人。
        ///
        /// 新版 ChurchReport 透過共用 assignment workflow 只處理使用者 unlink，
        /// provider RichMenu 的建立、版本同步與刪除策略交由共用 provisioning / sweep 流程集中管理。
        /// </summary>
        public async Task<String> DeleteRichMenuMessage(string UserId)
        {
            try
            {
                // 取得使用者目前在 LINE provider 端實際綁定的 richMenuId。
                var richMenuId = await this.m_LineMessagingClient.GetRichMenuIdOfUserAsync(UserId);
                // 先解除使用者與 RichMenu 的連結，避免刪除 provider 資源時仍有使用者指向它。
                await m_LineMessagingClient.UnLinkRichMenuFromUserAsync(UserId);
                // 舊版流程會直接刪除 provider RichMenu；新版共用流程避免在產品工具類中做這件事。
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

        /// <summary>
        /// 確認訊息範例 (已改為非同步)
        /// ✅ Phase 7: 移除 .Wait() 阻塞，改為 async/await
        /// </summary>
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

            await this.m_LineMessagingClient.PushMessageAsync(UserId, actions1);
        }

        /// <summary>
        /// 輪播訊息範例 (已改為非同步)
        /// ✅ Phase 7: 移除 .Wait() 阻塞，改為 async/await
        /// </summary>
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

            await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
        }

        /// <summary>
        /// 教會輪播訊息範例 (已改為非同步)
        /// ✅ Phase 7: 移除 .Wait() 阻塞，改為 async/await
        /// </summary>
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

            await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
        }

        #region 向後相容 - 已標記過時的同步方法
        /// <summary>
        /// 確認訊息範例 (已過時)
        /// </summary>
        [Obsolete("請使用 ConfirmMessageAsync 非同步方法")]
        public void ConfirmMessage(string UserId)
        {
            ConfirmMessageAsync(UserId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 輪播訊息範例 (已過時)
        /// </summary>
        [Obsolete("請使用 CarouselMessageAsync 非同步方法")]
        public void CarouselMessage(string UserId)
        {
            CarouselMessageAsync(UserId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 教會輪播訊息範例 (已過時)
        /// </summary>
        [Obsolete("請使用 ChurchCarouselMessageAsync 非同步方法")]
        public void ChurchCarouselMessage(string UserId)
        {
            ChurchCarouselMessageAsync(UserId).GetAwaiter().GetResult();
        }
        #endregion

        #endregion
    }
}
