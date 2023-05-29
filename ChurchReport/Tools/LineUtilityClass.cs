using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#region CRM 2011 reference
using Microsoft.Xrm.Sdk;
using ToolUtility;
using System.Threading.Tasks;
#endregion

using Line.Messaging;
using System.IO;
using ToolUtilityNameSpace;

namespace ChurchReport.Tools
{
        public class LineUtilityClass
        {
            #region 系統參數
            //IServiceProvider m_ServiceProvider;
            //ITracingService m_TracingService;
            //IPluginExecutionContext m_Context;
            //
            //IOrganizationServiceFactory m_ServiceFactory;
            IOrganizationService m_CrmService;

            // 系統傳來的組織名稱
            public String m_OrganizationName = "";

            ReplyUtility m_ReplyUtility;

            #region Channel Access Token 設定

            // 客製化
            // 天母豐盛靈糧堂 Line 2.0
            private const String FRENCHHORN_CHANNEL_ACCESS_TOKEN = "MW7xRUVOMqzX651Akvg2cI8Z8oaX61lPAyL3QdSA94/pD61/FmU0wxj8rJ3CBp6Kle1qoDGIPXnMQuV5fhtYLELP+3nfPPiTdvvud9wrDp0uB204ovkDM3CE6wKpcpS2RUILadDWc4FXX6e8lyr+HQdB04t89/1O/w1cDnyilFU=";
            private const String FRENCHHORN_BACK_CHANNEL_ACCESS_TOKEN = "MW7xRUVOMqzX651Akvg2cI8Z8oaX61lPAyL3QdSA94/pD61/FmU0wxj8rJ3CBp6Kle1qoDGIPXnMQuV5fhtYLELP+3nfPPiTdvvud9wrDp0uB204ovkDM3CE6wKpcpS2RUILadDWc4FXX6e8lyr+HQdB04t89/1O/w1cDnyilFU=";
        #endregion

        String m_ChannelAccessToken = FRENCHHORN_CHANNEL_ACCESS_TOKEN;

            LineMessagingClient m_LineMessagingClient;

            private const String WEB_LINK = @"http://www.speechmessage.com.tw";

            private const String DEVELOPER_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

            // Line 選單圖形檔案位置
            private const String LINE_MENU_PATH = @"D:\Line 選單\";


            // 模板預設的圖片
            private const String m_Default_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 楊梅靈糧堂模板預設的圖片
            private const String m_Yangmeillc_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 天母豐盛靈糧堂模板預設的圖片
            private const String m_TpeHoc_ThumbnailImageUrl = "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg";

            #endregion

            #region 釋放記憶體
            private bool _disposed = false;

            protected virtual void Dispose(bool disposing)
            {
                if (_disposed) return;

                if (disposing)
                {
                    m_ToolUtilityClass.Dispose();
                }

                _disposed = true;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~LineUtilityClass()
            {
                // Do not re-create Dispose clean-up code here.
                // Calling Dispose(false) is optimal in terms of
                // readability and maintainability.
                Dispose(false);
            }
            #endregion

            ToolUtilityClass m_ToolUtilityClass;

            public LineUtilityClass( ToolUtilityClass aToolUtilityClass)
            {
                m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);

                m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);
            }

            public void SetupChannelAccessToken(ref IOrganizationService aCrmService)
            {
                try
                {
                    // 客製化，請選擇
                    // 先取得組織名稱
                    if (this.m_OrganizationName == "frenchhorn")
                    {
                        m_ChannelAccessToken = FRENCHHORN_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "frenchhornback")
                    {
                        m_ChannelAccessToken = FRENCHHORN_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else
                    {
                        m_ChannelAccessToken = FRENCHHORN_CHANNEL_ACCESS_TOKEN;
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            #region 工具區
            #region Line Messagin Api SDK傳送
            public async Task ReplyMessage(string ReplyToken, List<ISendMessage> MessageToSend)
            {
                try
                {
                    await this.m_ReplyUtility.ReplyMessage(ReplyToken, MessageToSend);

                    return;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }
            }
            public async Task ReplyTextMessage(string ReplyToken, string Message)
            {
                await this.m_ReplyUtility.ReplyMessageAsync(ReplyToken, Message);

                return;
            }
            public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
            {
                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendMessageAsync(string UserId, string Message)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "傳送結果=" + aHttpResponseMessage);

                return;
            }
            public async Task MultiCastTextMessageAsync(IList<string> To, string Message)
            {
                try
                {
                    if (To.Count > 0)
                    {
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
            public void SendMessage(string UserId, string Message)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:圖片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.ReplyMessageAsync(ReplyToken, MessageToSend);

                return;
            }
            public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:影片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new VideoMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:聲音", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new AudioMessage(OriginalContenUrl, Duration)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:座標", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new LocationMessage(Title, Address, Latitude, Longitude)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendSticker(string UserId, int PackageId, int StickerId)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:貼圖", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new StickerMessage(PackageId.ToString(), StickerId.ToString())
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task PostSerializedTemplate(Entity aLetterEntity, string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
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

                    throw e;
                }
            }

            public async Task PostSerializedFlex(string UserId, FlexMessage aFlexMessage)
            {
            this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Flex", "");
                await this.m_LineMessagingClient.PushMessageAsync(UserId, new List<ISendMessage> { aFlexMessage });
            }
            public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)
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
            public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
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

            public async Task<String> AddRichMenuMessage(string UserId)
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

                byte[] readText = File.ReadAllBytes(path);
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
            public async Task<String> DeleteRichMenuMessage(string UserId)
            {
                // Get Rich Menu for the user
                var richMenuId = await this.m_LineMessagingClient.GetRichMenuIdOfUserAsync(UserId);
                await m_LineMessagingClient.UnLinkRichMenuFromUserAsync(UserId);
                await m_LineMessagingClient.DeleteRichMenuAsync(richMenuId);

                return "成功";

            }

            #endregion

            #endregion

            #region 設定通知格式

            public void SetupActionList(Entity aLetterEntity, ref TemplateMessageClass aTemplateMessageClass)
            {
                try
                {
                    String ActionLabel_1 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_label_1");
                    if (ActionLabel_1 != "")
                    {
                        ActionClass aActionClass = new ActionClass()
                        {
                            type = ConvertActionType(aLetterEntity, "new_action_category_1"),
                            label = ActionLabel_1,
                            text = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_text_1"),
                            data = "動作=" + ActionLabel_1 + "& EntityId=" + aLetterEntity.Id,
                            uri = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_uri_1"),

                            //type = "postback",
                            //label = "購買",
                            //data = "action=購買&itemid=001",
                            //uri = "http://www.speechmessage.com.tw",
                        };
                        aTemplateMessageClass.template.actions.Add(aActionClass);
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public String ConvertActionType(Entity aLetterEntity, String FieldName)
            {
                try
                {
                    int ActionType = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aLetterEntity, FieldName);

                    switch (ActionType)
                    {
                        case 100000000:
                            {
                                return "postback";
                            }
                        case 100000001:
                            {
                                return "message";
                            }
                        case 100000002:
                            {
                                return "uri";
                            }
                        default:
                            {
                                return "";
                            }
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }
            #endregion

            #region 處理寄送者

            public Entity GetLineSender(Entity aLetterEntity)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得 LINE 訊息寄送者
                        EntityReference aContactEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                        Guid aContactId = aContactEntityReference.Id;

                        Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                        return aRetrievedContact;

                        #endregion
                    }

                    return null;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public void GetLineIdAndContactFullNameOfSender(Entity aLetterEntity, ref String DisplayedLineId, ref String LineId, ref String ReplyToken, ref String ContactFullName)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得 LINE 訊息收件者的全名及其LINE ID
                        LineId = "";
                        ContactFullName = GetContactPartyFullName(aFromEntityCollection.Entities[i], ref LineId);
                        #endregion
                    }

                    DisplayedLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_displayed_lineid");

                    ReplyToken = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_linereplytoken");

                    return;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public string GetContactPartyFullName(Entity aContactParty, ref String LineId)
            {
                try
                {
                    EntityReference aContactEntityReference = (EntityReference)aContactParty["partyid"];

                    Guid aContactId = aContactEntityReference.Id;

                    String aContactName = aContactEntityReference.Name;

                    Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                    //if (aContactName.StartsWith("Line新加入者"))
                    //if (aContactName.EndsWith("(Line)"))
                    //{
                    //    aContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "new_line_displayname");
                    //}

                    LineId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "new_lineid");

                    return aContactName;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            #endregion


        }

        #region POST 區塊

        public class PostTextClass
        {
            public string to { get; set; }

            public List<TextMessageClass> messages { get; set; }
        }

        public class TextMessageClass
        {
            public string type { get; set; }
            public string text { get; set; }
        }







        public class PostTemplateClass
        {
            public string to { get; set; }

            public List<TemplateMessageClass> messages { get; set; }
        }
        public class TemplateMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public TemplateClass template { get; set; }
        }
        public class TemplateClass
        {
            public string type { get; set; }
            public string thumbnailImageUrl { get; set; }
            public string title { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }




        public class PostConfirmClass
        {
            public string to { get; set; }

            public List<ConfirmMessageClass> messages { get; set; }
        }
        public class ConfirmMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public ConfirmClass template { get; set; }
        }
        public class ConfirmClass
        {
            public string type { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }




        public class PostCarouselClass
        {
            public string to { get; set; }

            public List<CarouselMessageClass> messages { get; set; }
        }
        public class CarouselMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public CarouselClass template { get; set; }
        }

        public class CarouselClass
        {
            public string type { get; set; }
            public List<CarouselColumeClass> columns { get; set; }
        }

        public class CarouselColumeClass
        {
            public string thumbnailImageUrl { get; set; }
            public string title { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }



        public class PostImageMapClass
        {
            public string to { get; set; }

            public List<ImageMapMessageClass> messages { get; set; }
        }
        public class ImageMapMessageClass
        {
            public string type { get; set; }
            public string baseUrl { get; set; }
            public string altText { get; set; }

            public BaseSizeClass baseSize { get; set; }

            public List<ActionClass> actions { get; set; }
        }

        public class BaseSizeClass
        {
            public int height { get; set; }
            public int width { get; set; }
        }






        public class ActionClass
        {
            public string type { get; set; }
            public string label { get; set; }
            public string data { get; set; }
            public string text { get; set; }
            public string uri { get; set; }
            public string linkUri { get; set; }


            public AreaClass area { get; set; }

        }

        public class AreaClass
        {
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
        }



        #endregion

        #region 寄發LINE所需的 Class

        public class MessageContent
        {
            public int ContentLength { get; set; }
            public string ContentType { get; set; }
            public List<byte> RawBytes { get; set; }
        }


        public class UserProfile
        {
            //"displayName":"LINE taro",
            //"userId":"Uxxxxxxxxxxxxxx...",
            //"pictureUrl":"http://obs.line-apps.com/...",
            //"statusMessage":"Hello, LINE!"
            public string DisplayName { get; set; }
            public string UserId { get; set; }
            public string PictureUrl { get; set; }
            public string StatusMessage { get; set; }
        }
        #endregion
}
