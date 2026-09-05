// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/LineUtilityClass.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class LineUtilityClass、class PostTextClass、class TextMessageClass、class PostTemplateClass、class TemplateMessageClass、class TemplateClass、class PostConfirmClass、class ConfirmMessageClass
// 主要成員：GetChannelAccessToken、Dispose、CreateDefaultNotificationWorkflow、CreateDefaultReplyWorkflow、CreateDefaultRichMenuWorkflow、CreateDefaultRichMenuAssignmentWorkflow、SetupChannelAccessToken、RebuildDefaultWorkflowsForCurrentClient、SendBestEffortSdkMessagesAsync、SendBestEffortSdkMessagesToManyAsync
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Text、Microsoft.Xrm.Sdk、System.Threading.Tasks、Line.Messaging、System.IO
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    /// LINE 通訊工具類別。封裝與 LINE Messaging API 往來所需的組態、
    /// Channel Access Token 取得與訊息組裝，並以 IDisposable Pattern 確保資源正確釋放。
    /// </summary>
    public class LineUtilityClass : IDisposable
    {
            #region 系統層級欄位
            //IServiceProvider m_ServiceProvider;
            //ITracingService m_TracingService;
            //IPluginExecutionContext m_Context;
            //
            //IOrganizationServiceFactory m_ServiceFactory;
            IOrganizationService m_CrmService;

            // 目前請求所屬的組織代號，決定要使用哪一組 LINE 頻道設定。
            public String m_OrganizationName = "";

            ReplyUtility m_ReplyUtility;

            #region Channel Access Token 組態

            // 組態建構器與組態實例。宣告為 static readonly，讓整個行程共用一份，
            private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

            // 依組織代號從 appsettings.json 取出對應的 Channel Access Token。
            private static string GetChannelAccessToken(string organization)
            {
                string token = m_Configuration[$"LineMessaging:{organization}:ChannelAccessToken"];
                if (string.IsNullOrEmpty(token))
                {
                    // 找不到該組織的設定時，退回預設組織的設定，
                    // 避免因為新增組織卻漏設定而讓通知整個發不出去。
                    string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                    token = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];
                }
                return token ?? string.Empty;
            }

            #endregion

            String m_ChannelAccessToken = string.Empty;

            LineMessagingClient m_LineMessagingClient;

            /// <summary>
            /// 指示目前 LINE client 是否由本類別以相容字串建構式建立並負責釋放。
            /// 外部注入的 client 由呼叫端擁有，不能在此處 Dispose，避免破壞共享 HttpClient 的生命週期。
            /// </summary>
            private bool m_OwnsLineMessagingClient;

            private ILineNotificationWorkflow m_LineNotificationWorkflow;

            private ILineReplyWorkflow m_LineReplyWorkflow;

            /// <summary>
            /// 舊版 RichMenu 建立/上傳/連結 workflow 的相容欄位。
            /// 目前 legacy-auth 的一般指派改由 <see cref="m_LineRichMenuAssignmentWorkflow"/> 處理，
            /// 此欄位仍保留給既有建構式與測試替換使用。
            /// </summary>
            private ILineRichMenuWorkflow m_LineRichMenuWorkflow;

            /// <summary>
            /// 共用 RichMenu assignment workflow，負責解析 ChurchReport 的 menu key 並呼叫 LINE link/unlink。
            /// </summary>
            private ILineRichMenuAssignmentWorkflow m_LineRichMenuAssignmentWorkflow;

            private readonly bool m_UsesDefaultLineNotificationWorkflow;

            private readonly bool m_UsesDefaultLineReplyWorkflow;

            private readonly bool m_UsesDefaultLineRichMenuWorkflow;

            private readonly bool m_UsesDefaultLineRichMenuAssignmentWorkflow;

            private readonly Action<string, string, string> m_CreatePushLineMessage;

            /// <summary>
            /// ChurchReport 既有認證 RichMenu 的應用程式 menu key。
            /// 實際 LINE richMenuId 由 <see cref="ChurchReportLegacyRichMenuCatalog"/> 與 provisioning/cache 決定。
            /// </summary>
            private const string LegacyAuthRichMenuKey = "legacy-auth";

            private const String WEB_LINK = @"http://www.speechmessage.com.tw";

            private const String DEVELOPER_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

            // LINE 圖文選單圖檔的本機路徑。
            // ⚠️ 下一行的路徑字串在早期編碼轉換中損毀，且此常數在整個方案中沒有任何使用者。
            //    因為是字面值，猜測原文等同修改行為，故保留原樣不動。若要啟用請先確認正確路徑。
            private const String LINE_MENU_PATH = @"D:\Line ?詨\";


            // 預設的訊息縮圖網址，未指定組織時使用。
            private const String m_Default_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 楊梅堂專用的訊息縮圖網址。
            private const String m_Yangmeillc_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 台北堂專用的訊息縮圖網址。
            private const String m_TpeHoc_ThumbnailImageUrl = "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg";

            #endregion

            #region 資源釋放
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 不得釋放注入的 scoped ToolUtility；request scope 負責其 CRM 租約的確定性回收。

                // LineMessagingClient 由這個相容工具類別建立並獨佔；在 request/service owner 結束時必須
                // 釋放其內部 HttpClient，避免 socket handler 與 token 綁定的 client 留在 GC 才回收的路徑。
                if (m_OwnsLineMessagingClient)
                {
                    m_LineMessagingClient?.Dispose();
                }
                m_LineMessagingClient = null;

                // ReplyUtility 不擁有 client 或 workflow，但若未來實作可釋放資源，仍將其釋放責任保留在
                // 建立它的本類別。現行 ReplyUtility 不實作 IDisposable，這個分支為無副作用的相容保護。
                (m_ReplyUtility as IDisposable)?.Dispose();
                m_ReplyUtility = null;
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
            // 解構函式只呼叫 Dispose(false)，避免重複撰寫清理邏輯。
            // 實際資源釋放集中在 Dispose(bool)，可讀性與維護性較高。
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

                // 依目前組織取得對應的 Channel Access Token。
                string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);

                m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);
                m_OwnsLineMessagingClient = true;
                m_UsesDefaultLineNotificationWorkflow = lineNotificationWorkflow == null;
                m_UsesDefaultLineReplyWorkflow = lineReplyWorkflow == null;
                m_UsesDefaultLineRichMenuWorkflow = true;
                m_UsesDefaultLineRichMenuAssignmentWorkflow = true;
                m_LineNotificationWorkflow = lineNotificationWorkflow ?? CreateDefaultNotificationWorkflow(m_LineMessagingClient);
                m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(m_LineMessagingClient);
                m_LineRichMenuWorkflow = CreateDefaultRichMenuWorkflow(m_LineMessagingClient);
                m_LineRichMenuAssignmentWorkflow = CreateDefaultRichMenuAssignmentWorkflow(m_LineMessagingClient);
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
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, lineReplyWorkflow, lineRichMenuWorkflow, null, createPushLineMessage)
            {
            }

            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow,
                ILineRichMenuWorkflow? lineRichMenuWorkflow,
                ILineRichMenuAssignmentWorkflow? lineRichMenuAssignmentWorkflow,
                Action<string, string, string>? createPushLineMessage = null)
            {
                m_ToolUtilityClass = aToolUtilityClass ?? throw new ArgumentNullException(nameof(aToolUtilityClass));
                m_LineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
                m_OwnsLineMessagingClient = false;
                m_UsesDefaultLineNotificationWorkflow = lineNotificationWorkflow == null;
                m_UsesDefaultLineReplyWorkflow = lineReplyWorkflow == null;
                m_UsesDefaultLineRichMenuWorkflow = lineRichMenuWorkflow == null;
                m_UsesDefaultLineRichMenuAssignmentWorkflow = lineRichMenuAssignmentWorkflow == null;
                m_LineNotificationWorkflow = lineNotificationWorkflow ?? CreateDefaultNotificationWorkflow(m_LineMessagingClient);
                m_LineReplyWorkflow = lineReplyWorkflow ?? CreateDefaultReplyWorkflow(m_LineMessagingClient);
                m_LineRichMenuWorkflow = lineRichMenuWorkflow ?? CreateDefaultRichMenuWorkflow(m_LineMessagingClient);
                m_LineRichMenuAssignmentWorkflow = lineRichMenuAssignmentWorkflow ?? CreateDefaultRichMenuAssignmentWorkflow(m_LineMessagingClient);
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
                // 保留 create/upload/link workflow 供舊呼叫端相容；目前一般切換 legacy-auth 選單走 assignment workflow。
                return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));
            }

            private static ILineRichMenuAssignmentWorkflow CreateDefaultRichMenuAssignmentWorkflow(LineMessagingClient lineMessagingClient)
            {
                // 使用產品 catalog 與共用 cache/state store 建立 assignment workflow，
                // 讓 LineUtilityClass 不需要知道 LINE provider richMenuId 或 alias lifecycle。
                var processor = new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient));
                return new LineRichMenuAssignmentWorkflow(
                    processor,
                    new InMemoryLineRichMenuIdCache(),
                    new InMemoryRichMenuStateStore(),
                    new ChurchReportLegacyRichMenuCatalog());
            }

            /// <summary>
            /// 依目前組織切換 LINE token，並以新 client 原子取代此工具類別先前擁有的 client。
            /// </summary>
            /// <param name="aCrmService">舊版呼叫端傳入的 CRM service；此方法不保存、使用或釋放它，資源 owner 仍是呼叫端 scope。</param>
            /// <remarks>
            /// Channel token 是組織隔離邊界，不能將前一組 token 的 client 或預設 workflow 留到下一個組織。
            /// 方法會先在區域變數建構新 client 與相依 workflow；只有全部成功才替換欄位，接著釋放本類別
            /// 建立的舊 client。替換失敗時舊 client 保持可用，新建資源立即釋放，避免半初始化、socket 或 token
            /// retention。此 legacy 類別必須由 request/operation owner Dispose，不可被 singleton 共用。
            /// </remarks>
            public void SetupChannelAccessToken(ref IOrganizationService aCrmService)
            {
                try
                {
                    ThrowIfDisposed();

                    // 依組織代號對應到 appsettings.json 中的設定區段，
                    // 取出該組織專屬的 Channel Access Token。
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
                        // 未指定組織時使用預設組織。
                        string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                        m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);
                    }

                    // 新 client 必須完整建立後才可發佈，否則預設 workflow 建構失敗會讓既有欄位停在
                    // 不可用或已 Dispose 的半初始化狀態。這個向後相容建構式建立內部 HttpClient，故本類別
                    // 在成功替換後是唯一負責釋放舊 client 的 owner。
                    var replacementClient = new LineMessagingClient(m_ChannelAccessToken);
                    try
                    {
                        var replacementNotificationWorkflow = m_UsesDefaultLineNotificationWorkflow
                            ? CreateDefaultNotificationWorkflow(replacementClient)
                            : m_LineNotificationWorkflow;
                        var replacementReplyWorkflow = m_UsesDefaultLineReplyWorkflow
                            ? CreateDefaultReplyWorkflow(replacementClient)
                            : m_LineReplyWorkflow;
                        var replacementRichMenuWorkflow = m_UsesDefaultLineRichMenuWorkflow
                            ? CreateDefaultRichMenuWorkflow(replacementClient)
                            : m_LineRichMenuWorkflow;
                        var replacementRichMenuAssignmentWorkflow = m_UsesDefaultLineRichMenuAssignmentWorkflow
                            ? CreateDefaultRichMenuAssignmentWorkflow(replacementClient)
                            : m_LineRichMenuAssignmentWorkflow;
                        var replacementReplyUtility = new ReplyUtility(replacementClient, replacementReplyWorkflow);

                        var previousClient = m_LineMessagingClient;
                        var previousClientOwned = m_OwnsLineMessagingClient;
                        var previousReplyUtility = m_ReplyUtility;

                        m_LineMessagingClient = replacementClient;
                        m_OwnsLineMessagingClient = true;
                        m_LineNotificationWorkflow = replacementNotificationWorkflow;
                        m_LineReplyWorkflow = replacementReplyWorkflow;
                        m_LineRichMenuWorkflow = replacementRichMenuWorkflow;
                        m_LineRichMenuAssignmentWorkflow = replacementRichMenuAssignmentWorkflow;
                        m_ReplyUtility = replacementReplyUtility;

                        // ReplyUtility 目前只借用 client；仍在這裡釋放未來可能加入的自有資源，但絕不
                        // 釋放外部注入 workflow。client 的唯一 owner 是 LineUtilityClass。
                        (previousReplyUtility as IDisposable)?.Dispose();
                        if (previousClientOwned)
                        {
                            previousClient?.Dispose();
                        }
                    }
                    catch
                    {
                        replacementClient.Dispose();
                        throw;
                    }
                }
                catch
                {
                    // 保留原始堆疊，讓呼叫端能辨識 token 組態或 client 建構的真正失敗位置；絕不記錄 token。
                    throw;
                }
            }

            /// <summary>
            /// 在物件已釋放後拒絕建立或保存新的 token/client 資源。
            /// </summary>
            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LineUtilityClass));
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

                if (m_UsesDefaultLineRichMenuAssignmentWorkflow)
                {
                    m_LineRichMenuAssignmentWorkflow = CreateDefaultRichMenuAssignmentWorkflow(m_LineMessagingClient);
                }
            }

            #region 工具方法
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

                //（已停用的診斷輸出；原本的訊息字串在編碼轉換中損毀，保留供追溯）
                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "<字串已遺失>" + aHttpResponseMessage);

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
                // RichMenu 佈建已由 LineMessagingProcessor.RichMenus 的 catalog/provisioning workflow 負責。
                // 這個 ChurchReport 舊入口只保留「將使用者指派到 legacy-auth 選單」的應用層動作。
                await m_LineRichMenuAssignmentWorkflow.AssignOrThrowAsync(UserId, LegacyAuthRichMenuKey);

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
                // 解除使用者 RichMenu 連結時走共用 assignment workflow，
                // 讓 ChurchReport 不再自行保存或刪除 richMenuId。
                await m_LineRichMenuAssignmentWorkflow.UnassignOrThrowAsync(UserId);

                return "成功";
            }
            #endregion
            #endregion

            #region 設定與組態存取

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

            #region 訊息組裝

            public Entity GetLineSender(Entity aLetterEntity)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 組裝 LINE 訊息內容
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
                        #region 從 LINE 事件物件取出 LINE ID
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

                    // 以下是已停用的舊有姓名比對邏輯，保留供追溯。
                    // 原本的字串常值在早期的編碼轉換中損毀，內容已無法還原，故以本說明取代。
                    //if (aContactName.StartsWith("<字串常值已遺失>"))
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

        #region LINE 訊息資料傳輸類別

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

