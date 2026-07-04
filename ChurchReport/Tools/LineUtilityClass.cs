using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#region CRM 2011 reference
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;
#endregion

using Line.Messaging;
using System.IO;
using ToolUtilityNameSpace;
using Microsoft.Extensions.Configuration;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;

namespace ChurchReport.Tools
{
    /// <summary>
    /// LINE 閮撌亙憿
    /// ??Phase 5: 甇?Ⅱ撖衣 IDisposable Pattern 隞仿甇Ｚ??園?瘣拇?
    /// </summary>
    public class LineUtilityClass : IDisposable
    {
            #region 蝟餌絞?
            //IServiceProvider m_ServiceProvider;
            //ITracingService m_TracingService;
            //IPluginExecutionContext m_Context;
            //
            //IOrganizationServiceFactory m_ServiceFactory;
            IOrganizationService m_CrmService;

            // 蝟餌絞?喃???蝜?蝔?
            public String m_OrganizationName = "";

            ReplyUtility m_ReplyUtility;

            #region Channel Access Token 閮剖?

            // ?蔭撱箸??刻?撖虫?
            private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

            // 敺?蝵株???Channel Access Token
            private static string GetChannelAccessToken(string organization)
            {
                string token = m_Configuration[$"LineMessaging:{organization}:ChannelAccessToken"];
                if (string.IsNullOrEmpty(token))
                {
                    // 憒??曆??唳?摰?蝜?閮剖?嚗蝙?券?閮剔?蝜?
                    string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                    token = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];
                }
                return token ?? string.Empty;
            }

            #endregion

            String m_ChannelAccessToken = string.Empty;

            LineMessagingClient m_LineMessagingClient;

            private ILineNotificationWorkflow m_LineNotificationWorkflow;

            private ILineReplyWorkflow m_LineReplyWorkflow;

            private ILineRichMenuWorkflow m_LineRichMenuWorkflow;

            private readonly bool m_UsesDefaultLineNotificationWorkflow;

            private readonly bool m_UsesDefaultLineReplyWorkflow;

            private readonly bool m_UsesDefaultLineRichMenuWorkflow;

            private readonly Action<string, string, string> m_CreatePushLineMessage;

            private const String WEB_LINK = @"http://www.speechmessage.com.tw";

            private const String DEVELOPER_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

            // Line ?詨?耦瑼?雿蔭
            private const String LINE_MENU_PATH = @"D:\Line ?詨\";


            // 璅⊥?身????
            private const String m_Default_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 璆??釦?芋?輸?閮剔???
            private const String m_Yangmeillc_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 憟賜鈭箸芋?輸?閮剔???
            private const String m_TpeHoc_ThumbnailImageUrl = "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg";

            #endregion

            #region ?閮擃?
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // ??? ToolUtilityClass
                m_ToolUtilityClass?.Dispose();
                
                // ??? LineMessagingClient
                m_LineMessagingClient?.Dispose();
                
                // ??? ReplyUtility
                (m_ReplyUtility as IDisposable)?.Dispose();
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
                : this(aToolUtilityClass, null)
            {
            }

            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                ILineNotificationWorkflow? lineNotificationWorkflow)
                : this(aToolUtilityClass, lineNotificationWorkflow, null)
            {
            }

            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow)
            {
                m_ToolUtilityClass = aToolUtilityClass ?? throw new ArgumentNullException(nameof(aToolUtilityClass));

                // ????雿輻?身蝯???Token
                string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);
                
                m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);
                m_UsesDefaultLineNotificationWorkflow = lineNotificationWorkflow == null;
                m_UsesDefaultLineReplyWorkflow = lineReplyWorkflow == null;
                m_UsesDefaultLineRichMenuWorkflow = true;
                m_LineNotificationWorkflow = lineNotificationWorkflow ?? CreateDefaultNotificationWorkflow(m_LineMessagingClient);
                m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(m_LineMessagingClient);
                m_LineRichMenuWorkflow = CreateDefaultRichMenuWorkflow(m_LineMessagingClient);
                m_CreatePushLineMessage = m_ToolUtilityClass.CreatePushLineMessage;

                m_ReplyUtility = new ReplyUtility(
                    m_LineMessagingClient,
                    new LineMessagingProcessor.LineMessagingProcessorClass(m_LineMessagingClient),
                    m_LineReplyWorkflow);
            }

            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, null, null)
            {
            }

            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, lineReplyWorkflow, null, null)
            {
            }

            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                Action<string, string, string>? createPushLineMessage)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, null, null, createPushLineMessage)
            {
            }

            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow,
                Action<string, string, string>? createPushLineMessage)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, lineReplyWorkflow, null, createPushLineMessage)
            {
            }

            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow,
                ILineRichMenuWorkflow? lineRichMenuWorkflow,
                Action<string, string, string>? createPushLineMessage = null)
            {
                m_ToolUtilityClass = aToolUtilityClass ?? throw new ArgumentNullException(nameof(aToolUtilityClass));
                m_LineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
                m_UsesDefaultLineNotificationWorkflow = lineNotificationWorkflow == null;
                m_UsesDefaultLineReplyWorkflow = lineReplyWorkflow == null;
                m_UsesDefaultLineRichMenuWorkflow = lineRichMenuWorkflow == null;
                m_LineNotificationWorkflow = lineNotificationWorkflow ?? CreateDefaultNotificationWorkflow(m_LineMessagingClient);
                m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(m_LineMessagingClient);
                m_LineRichMenuWorkflow = lineRichMenuWorkflow ?? CreateDefaultRichMenuWorkflow(m_LineMessagingClient);
                m_CreatePushLineMessage = createPushLineMessage ?? m_ToolUtilityClass.CreatePushLineMessage;
                m_ReplyUtility = new ReplyUtility(
                    m_LineMessagingClient,
                    new LineMessagingProcessor.LineMessagingProcessorClass(m_LineMessagingClient),
                    m_LineReplyWorkflow);
            }

            private static ILineNotificationWorkflow CreateDefaultNotificationWorkflow(LineMessagingClient lineMessagingClient)
            {
                return new LineNotificationWorkflow(new LineMessagingProcessorClass(lineMessagingClient));
            }

            private static ILineReplyWorkflow CreateDefaultReplyWorkflow(LineMessagingClient lineMessagingClient)
            {
                return new LineReplyWorkflow(new LineMessagingProcessorClass(lineMessagingClient));
            }

            private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)
            {
                return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));
            }

            public void SetupChannelAccessToken(ref IOrganizationService aCrmService)
            {
                try
                {
                    // ?寞?蝯??迂敺?蝵格?霈???? Channel Access Token
                    if (this.m_OrganizationName == "jesus")
                    {
                        m_ChannelAccessToken = GetChannelAccessToken("Jesus");
                    }
                    else if (this.m_OrganizationName == "jesusback")
                    {
                        m_ChannelAccessToken = GetChannelAccessToken("JesusBack");
                    }
                    else
                    {
                        // 雿輻?身蝯?
                        string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                        m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);
                    }

                    // ?????LineMessagingClient
                    // ?ㄐ?芣??遣?祇??交??? LineMessagingClient??
                    // 憒??芯???ILineNotificationWorkflow 瘜典?唬?靘陷憭?蝜????澆蝡荔?
                    // 撖阡???粥 workflow ????processor/client嚗??舫ㄐ?遣??client??
                    // ?迨甇???亦???workflow 撅支?敹??瑕??詨???蝜?token 頝舐?賢???
                    m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);
                    RebuildDefaultWorkflowsForCurrentClient();
                    m_ReplyUtility = new ReplyUtility(m_LineMessagingClient, m_LineReplyWorkflow);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            private void RebuildDefaultWorkflowsForCurrentClient()
            {
                if (m_UsesDefaultLineNotificationWorkflow)
                {
                    m_LineNotificationWorkflow = CreateDefaultNotificationWorkflow(m_LineMessagingClient);
                }

                if (m_UsesDefaultLineReplyWorkflow)
                {
                    m_LineReplyWorkflow = CreateDefaultReplyWorkflow(m_LineMessagingClient);
                }

                if (m_UsesDefaultLineRichMenuWorkflow)
                {
                    m_LineRichMenuWorkflow = CreateDefaultRichMenuWorkflow(m_LineMessagingClient);
                }
            }

            #region 撌亙?
            #region Line Messagin Api SDK?喲?
            private async Task SendBestEffortSdkMessagesAsync(
                string userId,
                IReadOnlyList<ISendMessage> messages,
                string source)
            {
                await m_LineNotificationWorkflow.SendAsync(new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(userId),
                    Content = LineNotificationContent.SdkMessagesList(messages),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = source
                    }
                });
            }

            private async Task SendBestEffortSdkMessagesToManyAsync(
                IList<string> userIds,
                IReadOnlyList<ISendMessage> messages,
                string source)
            {
                if (userIds == null || userIds.Count == 0)
                {
                    return;
                }

                foreach (var userId in userIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                {
                    await m_LineNotificationWorkflow.SendAsync(new LineNotificationRequest
                    {
                        Recipient = LineNotificationRecipient.User(userId),
                        Content = LineNotificationContent.SdkMessagesList(messages),
                        Metadata = new Dictionary<string, string>
                        {
                            ["source"] = source,
                            ["deliveryMode"] = "multicast-split"
                        }
                    });
                }
            }

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
                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.BestEffortSdkMessages");

                return;
            }
            public async Task SendMessageAsync(string UserId, string Message)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendMessageAsync");

                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "?喲???" + aHttpResponseMessage);

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

                        await SendBestEffortSdkMessagesToManyAsync(
                            To,
                            MessageToSend,
                            "ChurchReport.LineUtilityClass.MultiCastTextMessageAsync");
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
                m_CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                SendBestEffortSdkMessagesAsync(
                        UserId,
                        MessageToSend,
                        "ChurchReport.LineUtilityClass.SendMessage.Sync")
                    .GetAwaiter()
                    .GetResult();

                return;
            }
            public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:圖片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendImage");

                return;
            }
            public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_ReplyUtility.ReplyMessage(ReplyToken, MessageToSend);

                return;
            }
            public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:影片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new VideoMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendVideo");

                return;
            }
            public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:聲音", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new AudioMessage(OriginalContenUrl, Duration)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendAudio");

                return;
            }
            public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:座標", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new LocationMessage(Title, Address, Latitude, Longitude)
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendLocation");

                return;
            }
            public async Task SendSticker(string UserId, int PackageId, int StickerId)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:貼圖", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new StickerMessage(PackageId.ToString(), StickerId.ToString())
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.SendSticker");

                return;
            }
            public async Task PostSerializedTemplate(Entity aLetterEntity, string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:Template", "");
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
                    "ChurchReport.LineUtilityClass.PostSerializedTemplate.Entity");

            }

            public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
            {
                try
                {
                    m_CreatePushLineMessage(UserId, "Line推播統計:Template", "");
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
                        "ChurchReport.LineUtilityClass.PostSerializedTemplate");

                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public async Task PostSerializedFlex(string UserId, FlexMessage aFlexMessage)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:Flex", "");
                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    new List<ISendMessage> { aFlexMessage },
                    "ChurchReport.LineUtilityClass.PostSerializedFlex");
            }
            public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:Confirm", "");
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
                    "ChurchReport.LineUtilityClass.PostSerializedConfirm");
            }
            public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:ImageMap", "");
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
                    "ChurchReport.LineUtilityClass.PostSerializedImageMap");

            }

            public async Task<String> AddRichMenuMessage(string UserId)
            {
                var richMenu = CreateLegacySingleButtonRichMenu();
                var imagePath = @"D:\暫存區\richmenu.PNG";
                await m_LineRichMenuWorkflow.CreateUploadAndLinkOrThrowAsync(new LineRichMenuCreateUploadAndLinkRequest
                {
                        UserId = UserId,
                        RichMenu = richMenu,
                        PngImageStreamFactory = () => File.OpenRead(imagePath),
                        Metadata = new Dictionary<string, string>
                        {
                            ["source"] = "ChurchReport.LineUtilityClass.AddRichMenuMessage",
                            ["legacyImagePath"] = imagePath
                        }
                });

                var messageToSend = new List<ISendMessage>
                {
                    new TextMessage("Rich menu added"),
                    new StickerMessage("1", "5")
                };

                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    messageToSend,
                    "ChurchReport.LineUtilityClass.AddRichMenuMessage");

                return "成功";
            }

            public async Task<String> DeleteRichMenuMessage(string UserId)
            {
                await m_LineRichMenuWorkflow.DeleteLinkedRichMenuOrThrowAsync(new LineRichMenuDeleteLinkedRequest
                {
                        UserId = UserId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["source"] = "ChurchReport.LineUtilityClass.DeleteRichMenuMessage"
                        }
                });

                return "成功";
            }

            private static RichMenu CreateLegacySingleButtonRichMenu()
            {
                return new RichMenu()
                {
                    Size = ImagemapSize.RichMenuLong,
                    Selected = false,
                    Name = "nice richmenu",
                    ChatBarText = "touch me",
                    Areas = new List<ActionArea>()
                    {
                        new ActionArea()
                        {
                            Bounds = new ImagemapArea(0, 0, ImagemapSize.RichMenuLong.Width, ImagemapSize.RichMenuLong.Height),
                            Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A")
                        }
                    }
                };
            }
            #endregion
            #endregion

            #region 閮剖???澆?

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
                            data = "??=" + ActionLabel_1 + "& EntityId=" + aLetterEntity.Id,
                            uri = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_uri_1"),

                            //type = "postback",
                            //label = "鞈潸眺",
                            //data = "action=鞈潸眺&itemid=001",
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

            #region ??撖?

            public Entity GetLineSender(Entity aLetterEntity)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region ?? LINE 閮撖?
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
                        #region ?? LINE 閮?嗡辣???典??LINE ID
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

                    //if (aContactName.StartsWith("Line?啣??亥?))
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

        #region POST ?憛?

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

        #region 撖LINE????Class

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

