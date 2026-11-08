/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 舊 LINE 工具的 Obsolete 相容外觀；送訊只組裝 SDK 訊息
 * 並委派共用 notification／reply／RichMenu assignment，CRM action／party 組裝及舊
 * JSON DTO 仍屬產品，不移入 Core。正式會友授權推播由 P2-04 Core 負責，本檔不補造身分。
 * 主要功能與流程：保留統計、同步別名、媒體、Flex、template 與 reply 入口；既有多播
 * 刻意逐人 push，與 ToolUtility multicast 不同。認證選單僅以 menu key 指派／解除，不
 * 重新建立 HTTP、圖片或刪除共用 provider 資源。best-effort 的真實失敗交共用診斷，
 * 不以 raw Exception 字串通知使用者；必要產品決策及 CRM 授權仍由呼叫端完成。
 * 資源與隔離：部署組態為不監聽變更的 static 設定，沒有 request／user 快取；每個工具
 * 實例必須限定一個 request／operation 並在操作排空後 Dispose，不可 singleton 或與
 * 切換組織並行。自行建立的 token-bound client 由本實例釋放；注入 client／workflow／
 * CRM Tool 僅借用，由外層 scope 釋放。SetupChannelAccessToken 保持原先成功建構才替換
 * 及失敗釋放新 client 的順序；注入的 workflow 必須符合 owner 的既有頻道隔離約定。
 * 使用邊界與維護：訊息、party、DTO 都是操作內可變資料，不能快取為跨使用者狀態；
 * DTO 不執行驗證或授權。SDK timeout 由 client owner 設定，本包未新增 caller-token API。
 * P206 假 HTTP 黃金驗證送訊；CRM-only 方法另作 no-HTTP／產品組裝對照，不冒稱實機。
 */
using ChurchReport.Configuration;
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
    [Obsolete("改用共用 notification/reply/RichMenu workflow；CRM 組裝留產品，階段6移除送訊相容殼。")]
    public class LineUtilityClass : IDisposable
    {
            #region 系統層級欄位
            //IServiceProvider m_ServiceProvider;
            //ITracingService m_TracingService;
            //IPluginExecutionContext m_Context;
            //
            //IOrganizationServiceFactory m_ServiceFactory;
            IOrganizationService m_CrmService;

            /// <summary>由產品 owner 設定的組織別名；不可把使用者輸入視為授權，也不可與送訊並行修改。</summary>
            public String m_OrganizationName = "";

            ReplyUtility m_ReplyUtility;

            #region Channel Access Token 組態

            // 組態建構器與組態實例。宣告為 static readonly，讓整個行程共用一份，
            /// <summary>
            /// LineUtilityClass 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
            /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
            /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
            /// </summary>
            private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
            private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();

            // 依組織代號從 appsettings.json 取出對應的 Channel Access Token。
            /// <summary>由 Tenancy 唯一入口解析，null 表示部署預設；保留既有產品組織選擇與 client owner，不快取解析結果。</summary>
            /// <param name="organization">已驗證產品組織別名；null 時僅讀部署預設。</param>
            /// <returns>只供本次工具 client 使用的 raw token；未配置為空，來源故障由外層上報。</returns>
            private static string GetChannelAccessToken(string? organization)
            {
                return global::SpeechMessage.Tenancy.LegacyLineChannelAccess.GetOptionalAccessToken(m_Configuration, organization);
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

        /// <summary>在呼叫端已等待作業排空後釋放本實例擁有的 client；借入的 SDK／CRM／workflow 不在此釋放。</summary>
        /// <param name="disposing">true 時只關閉本實例擁有的 managed client；false 不碰借用資源。</param>
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

        /// <summary>在呼叫端已等待作業排空後釋放本實例擁有的 client；借入的 SDK／CRM／workflow 不在此釋放。</summary>
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

            /// <summary>保留部署設定建構式；由本實例建立 token-bound client，呼叫端必須等待操作結束後 Dispose。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            public LineUtilityClass( ToolUtilityClass aToolUtilityClass)
                : this(aToolUtilityClass, null)
            {
            }

            /// <summary>保留部署設定建構式；由本實例建立 token-bound client，呼叫端必須等待操作結束後 Dispose。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                ILineNotificationWorkflow? lineNotificationWorkflow)
                : this(aToolUtilityClass, lineNotificationWorkflow, null)
            {
            }

            /// <summary>保留部署設定建構式；由本實例建立 token-bound client，呼叫端必須等待操作結束後 Dispose。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow)
            {
                m_ToolUtilityClass = aToolUtilityClass ?? throw new ArgumentNullException(nameof(aToolUtilityClass));

                // 依目前組織取得對應的 Channel Access Token。
                m_ChannelAccessToken = GetChannelAccessToken(null);

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

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, null, null)
            {
            }

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
            public LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, lineReplyWorkflow, null, null)
            {
            }

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="createPushLineMessage">當次產品統計接點；替身可用於離線驗證，不是授權接點。</param>
            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                Action<string, string, string>? createPushLineMessage)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, null, null, createPushLineMessage)
            {
            }

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
            /// <param name="createPushLineMessage">當次產品統計接點；替身可用於離線驗證，不是授權接點。</param>
            protected LineUtilityClass(
                ToolUtilityClass aToolUtilityClass,
                LineMessagingClient lineMessagingClient,
                ILineNotificationWorkflow? lineNotificationWorkflow,
                ILineReplyWorkflow? lineReplyWorkflow,
                Action<string, string, string>? createPushLineMessage)
                : this(aToolUtilityClass, lineMessagingClient, lineNotificationWorkflow, lineReplyWorkflow, null, createPushLineMessage)
            {
            }

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
            /// <param name="lineRichMenuWorkflow">舊佈建流程注入形狀的相容參數；一般指派不在本殼重做佈建。</param>
            /// <param name="createPushLineMessage">當次產品統計接點；替身可用於離線驗證，不是授權接點。</param>
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

            /// <summary>保留舊建構式的頻道限定相容外觀；借用注入資源，null workflow 只以同一 SDK 建 adapter。</summary>
            /// <param name="aToolUtilityClass">借入的產品 CRM 工具，由呼叫端 scope 擁有及釋放。</param>
            /// <param name="lineMessagingClient">同頻道 SDK client；由呼叫端擁有、設定有限 timeout 並於操作排空後釋放。</param>
            /// <param name="lineNotificationWorkflow">可選的同頻道通知流程；null 才以現有 client 建立 adapter，不取得 owner。</param>
            /// <param name="lineReplyWorkflow">可選的同頻道 reply 流程；null 使用現有 client，不保存事件 token。</param>
            /// <param name="lineRichMenuWorkflow">舊佈建流程注入形狀的相容參數；一般指派不在本殼重做佈建。</param>
            /// <param name="lineRichMenuAssignmentWorkflow">同頻道的產品 menu-key 指派流程，不以 provider ID 跨頻道共用。</param>
            /// <param name="createPushLineMessage">當次產品統計接點；替身可用於離線驗證，不是授權接點。</param>
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
                        m_ChannelAccessToken = GetChannelAccessToken(null);
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
            #region LINE SDK 相容入口
            private async Task SendBestEffortSdkMessagesAsync(
                string userId,
                IReadOnlyList<ISendMessage> messages,
                string source)
            {
                var result = await m_LineNotificationWorkflow.SendAsync(new LineNotificationRequest
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
                    var result = await m_LineNotificationWorkflow.SendAsync(new LineNotificationRequest
                    {
                        Recipient = LineNotificationRecipient.User(userId),
                        Content = LineNotificationContent.SdkMessagesList(messages),
                        Metadata = new Dictionary<string, string>
                        {
                            ["source"] = source,
                            ["deliveryMode"] = "multicast-split"
                        }
                    });
                    if (result.Exception is { } exception)
                    {
                        global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception);
                    }
                }
            }

            /// <summary>將操作限定的 reply token 與 SDK 訊息清單交共用 reply 流程，保留既有 best-effort 行為。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="ReplyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
            /// <param name="MessageToSend">呼叫端組好的當次 SDK 訊息集合，完成前不得並行修改。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
            public async Task ReplyMessage(string ReplyToken, List<ISendMessage> MessageToSend)
            {
                try
                {
                    await this.m_ReplyUtility.ReplyMessage(ReplyToken, MessageToSend);

                    return;
                }
                catch (System.Exception e)
                {
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);
                }
            }
            /// <summary>將當次文字與 reply token 交產品 reply 相容殼，再由共用 workflow 傳輸。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="ReplyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
            /// <param name="Message">當次文字內容，不存到共享狀態。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
            public async Task ReplyTextMessage(string ReplyToken, string Message)
            {
                await this.m_ReplyUtility.ReplyMessageAsync(ReplyToken, Message);

                return;
            }
            /// <summary>將當次收件者與文字或 SDK 多則訊息交共用送訊流程；不更改原 payload、sender 或重試鍵。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="MessageToSend">呼叫端組好的當次 SDK 訊息集合，完成前不得並行修改。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
            public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
            {
                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    MessageToSend,
                    "ChurchReport.LineUtilityClass.BestEffortSdkMessages");

                return;
            }
            /// <summary>先執行既有產品推播統計，再將文字交共用通知流程；統計不是授權證明。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="Message">當次文字內容，不存到共享狀態。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
            /// <summary>保留原收件順序及空集合不發送契約；本產品沿既有 workflow 逐人 push，不改為 ToolUtility 的 multicast HTTP。</summary>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }

            }
            /// <summary>將當次收件者與文字或 SDK 多則訊息交共用送訊流程；不更改原 payload、sender 或重試鍵。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="Message">當次文字內容，不存到共享狀態。</param>
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
            /// <summary>保留原圖／預覽 URL 組裝 ImageMessage，僅委派共用流程而不下載或保留圖片。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
            /// <param name="PreviewImageUrl">預覽圖片 URL，原樣交 SDK。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
            /// <summary>把當次原圖／預覽 URL 組成圖片回覆後交既有 reply 流程，不取得媒體串流所有權。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="ReplyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
            /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
            /// <param name="PreviewImageUrl">預覽圖片 URL，原樣交 SDK。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
            public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_ReplyUtility.ReplyMessage(ReplyToken, MessageToSend);

                return;
            }
            /// <summary>將影片與預覽 URL 組裝為 SDK 訊息；本層不另開串流或變更傳輸協定。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
            /// <param name="PreviewImageUrl">預覽圖片 URL，原樣交 SDK。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
            /// <summary>保留舊拼字的音訊相容入口，將 URL 與毫秒長度原樣交 SDK，不額外存取檔案。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="OriginalContenUrl">原始媒體 URL，只交 SDK，不由本殼下載。</param>
            /// <param name="Duration">音訊長度（毫秒），保留舊 SDK 數值。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
            /// <summary>保留既有 package／sticker ID 的字串序列化，由共用流程執行一次傳輸。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="PackageId">既有貼圖套件數值。</param>
            /// <param name="StickerId">既有貼圖識別數值。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
            /// <summary>把當次 Buttons 內容及 action 清單組為 SDK template，產品 entity 多載不額外改動 wire。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="aLetterEntity">當次產品 letter 資料，由呼叫端完成 CRM scope 驗證。</param>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="AltText">無法顯示樣板時的替代文字。</param>
            /// <param name="ThumbnailImageUrl">Buttons 的縮圖 URL，不開本機檔案。</param>
            /// <param name="Title">顯示標題，不作身分識別。</param>
            /// <param name="Text">SDK 顯示本文，維持舊格式。</param>
            /// <param name="aITemplateAction">當次 template actions，保留序列及內容。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }

            /// <summary>保留呼叫端已組好的 Flex 訊息及產品統計；本層不改寫 sender 或進行 CRM 授權。</summary>
            /// <remarks>產品參數不是授權證明；維持舊版失敗語意，不新增 HTTP、重試或跨操作狀態。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <param name="aFlexMessage">呼叫端已組裝的當次 Flex，不改寫內容。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
            public async Task PostSerializedFlex(string UserId, FlexMessage aFlexMessage)
            {
                m_CreatePushLineMessage(UserId, "Line推播統計:Flex", "");
                await SendBestEffortSdkMessagesAsync(
                    UserId,
                    new List<ISendMessage> { aFlexMessage },
                    "ChurchReport.LineUtilityClass.PostSerializedFlex");
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

            /// <summary>保留 ChurchReport 指派既有 legacy-auth 並送成功通知的相容入口；佈建與資源版本不在本殼重做。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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

            /// <summary>只透過既有 assignment 解除使用者選單；不刪除可能被其他使用者共用的 provider menu。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="UserId">本次產品已授權的 LINE 收件 ID；相容入口也可能為群組，不代表正式會友授權。</param>
            /// <returns>等待同一流程結束的作業；依既有入口契約保留 best-effort 或必要失敗。</returns>
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

            /// <summary>從 letter 欄位追加舊產品 action DTO，保留既有格式與欄位值；不發 HTTP 或執行授權。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="aLetterEntity">當次產品 letter 資料，由呼叫端完成 CRM scope 驗證。</param>
            /// <param name="aTemplateMessageClass">當次可變 template DTO，僅追加到此操作候選，不應跨使用者共用。</param>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }

            /// <summary>把 CRM option 值轉成舊樣板 action 型別，未知值保留空字串；不送訊或猜測資料。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="aLetterEntity">當次產品 letter 資料，由呼叫端完成 CRM scope 驗證。</param>
            /// <param name="FieldName">產品內定的 option 欄位名稱。</param>
            /// <returns>既有產品欄位對應結果；未附加授權或跨操作狀態。</returns>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }
            #endregion

            #region 訊息組裝

            /// <summary>依 letter 第一個 from party 查詢 contact；查詢與授權責任屬產品 Tool，回傳值不跨請求快取。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="aLetterEntity">當次產品 letter 資料，由呼叫端完成 CRM scope 驗證。</param>
            /// <returns>既有產品欄位對應結果；未附加授權或跨操作狀態。</returns>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }

            /// <summary>沿既有 from party 順序更新輸出及 letter reply 欄位；僅產品 CRM 組裝，不改成授權流程。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="aLetterEntity">當次產品 letter 資料，由呼叫端完成 CRM scope 驗證。</param>
            /// <param name="DisplayedLineId">輸出產品顯示帳號，不是授權證明。</param>
            /// <param name="LineId">輸出當次查得的 LINE ID，不跨操作保留。</param>
            /// <param name="ReplyToken">已驗證 webhook 的一次性 token，僅在本次呼叫借用，不快取。</param>
            /// <param name="ContactFullName">輸出既有 contact 顯示名，不作穩定身分。</param>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }

            /// <summary>透過 partyid 查 contact 的 LINE ID，回傳原參照顯示名；同名不合併、不以名稱代替身分。</summary>
            /// <remarks>只在當次產品操作使用輸入與輸出；不建立跨使用者快取，真正失敗交原 owner 處理。</remarks>
            /// <param name="aContactParty">當次包含伺服器 partyid 的 CRM party。</param>
            /// <param name="LineId">輸出當次查得的 LINE ID，不跨操作保留。</param>
            /// <returns>既有產品欄位對應結果；未附加授權或跨操作狀態。</returns>
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
                    global::ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(e);

                    throw;
                }
            }

            #endregion


        }

        #region 舊 JSON 資料傳輸模型

        /// <summary>文字推播收件者與訊息序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class PostTextClass
        {
            /// <summary>當次收件 LINE ID，不是授權證明；不含額外資源所有權。</summary>
            public string to { get; set; }

            /// <summary>當次有序訊息集合，不可共享可變清單；不含額外資源所有權。</summary>
            public List<TextMessageClass> messages { get; set; }
        }

        /// <summary>文字訊息型別及本文的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class TextMessageClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>顯示本文，可能含產品資料，不應寫入診斷；不含額外資源所有權。</summary>
            public string text { get; set; }
        }







        /// <summary>Buttons 推播收件者與訊息序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class PostTemplateClass
        {
            /// <summary>當次收件 LINE ID，不是授權證明；不含額外資源所有權。</summary>
            public string to { get; set; }

            /// <summary>當次有序訊息集合，不可共享可變清單；不含額外資源所有權。</summary>
            public List<TemplateMessageClass> messages { get; set; }
        }
        /// <summary>Buttons 替代文字及樣板的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class TemplateMessageClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>樣板替代文字；不含額外資源所有權。</summary>
            public string altText { get; set; }
            /// <summary>操作內可變樣板內容；不含額外資源所有權。</summary>
            public TemplateClass template { get; set; }
        }
        /// <summary>Buttons 圖片、標題、本文及 action 清單的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class TemplateClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>縮圖 URL 字串，本 DTO 不下載；不含額外資源所有權。</summary>
            public string thumbnailImageUrl { get; set; }
            /// <summary>顯示標題，不作穩定身分；不含額外資源所有權。</summary>
            public string title { get; set; }
            /// <summary>顯示本文，可能含產品資料，不應寫入診斷；不含額外資源所有權。</summary>
            public string text { get; set; }
            /// <summary>當次 action 有序集合；不含額外資源所有權。</summary>
            public List<ActionClass> actions { get; set; }
        }




        /// <summary>Confirm 推播收件者與訊息序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class PostConfirmClass
        {
            /// <summary>當次收件 LINE ID，不是授權證明；不含額外資源所有權。</summary>
            public string to { get; set; }

            /// <summary>當次有序訊息集合，不可共享可變清單；不含額外資源所有權。</summary>
            public List<ConfirmMessageClass> messages { get; set; }
        }
        /// <summary>Confirm 替代文字及樣板的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class ConfirmMessageClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>樣板替代文字；不含額外資源所有權。</summary>
            public string altText { get; set; }
            /// <summary>操作內可變樣板內容；不含額外資源所有權。</summary>
            public ConfirmClass template { get; set; }
        }
        /// <summary>Confirm 本文與 action 清單的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class ConfirmClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>顯示本文，可能含產品資料，不應寫入診斷；不含額外資源所有權。</summary>
            public string text { get; set; }
            /// <summary>當次 action 有序集合；不含額外資源所有權。</summary>
            public List<ActionClass> actions { get; set; }
        }




        /// <summary>Carousel 推播收件者與訊息序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class PostCarouselClass
        {
            /// <summary>當次收件 LINE ID，不是授權證明；不含額外資源所有權。</summary>
            public string to { get; set; }

            /// <summary>當次有序訊息集合，不可共享可變清單；不含額外資源所有權。</summary>
            public List<CarouselMessageClass> messages { get; set; }
        }
        /// <summary>Carousel 替代文字及樣板的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class CarouselMessageClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>樣板替代文字；不含額外資源所有權。</summary>
            public string altText { get; set; }
            /// <summary>操作內可變樣板內容；不含額外資源所有權。</summary>
            public CarouselClass template { get; set; }
        }

        /// <summary>Carousel 欄位序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class CarouselClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>當次 Carousel 欄位有序集合；不含額外資源所有權。</summary>
            public List<CarouselColumeClass> columns { get; set; }
        }

        /// <summary>單一 Carousel 圖片、文字及 action的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class CarouselColumeClass
        {
            /// <summary>縮圖 URL 字串，本 DTO 不下載；不含額外資源所有權。</summary>
            public string thumbnailImageUrl { get; set; }
            /// <summary>顯示標題，不作穩定身分；不含額外資源所有權。</summary>
            public string title { get; set; }
            /// <summary>顯示本文，可能含產品資料，不應寫入診斷；不含額外資源所有權。</summary>
            public string text { get; set; }
            /// <summary>當次 action 有序集合；不含額外資源所有權。</summary>
            public List<ActionClass> actions { get; set; }
        }



        /// <summary>Imagemap 推播收件者與訊息序列的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class PostImageMapClass
        {
            /// <summary>當次收件 LINE ID，不是授權證明；不含額外資源所有權。</summary>
            public string to { get; set; }

            /// <summary>當次有序訊息集合，不可共享可變清單；不含額外資源所有權。</summary>
            public List<ImageMapMessageClass> messages { get; set; }
        }
        /// <summary>Imagemap 圖片、尺寸及 action的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class ImageMapMessageClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>Imagemap 圖片基底 URL；不含額外資源所有權。</summary>
            public string baseUrl { get; set; }
            /// <summary>樣板替代文字；不含額外資源所有權。</summary>
            public string altText { get; set; }

            /// <summary>Imagemap 的基底尺寸；不含額外資源所有權。</summary>
            public BaseSizeClass baseSize { get; set; }

            /// <summary>當次 action 有序集合；不含額外資源所有權。</summary>
            public List<ActionClass> actions { get; set; }
        }

        /// <summary>Imagemap 基底寬高的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class BaseSizeClass
        {
            /// <summary>以像素表示的高度；不含額外資源所有權。</summary>
            public int height { get; set; }
            /// <summary>以像素表示的寬度；不含額外資源所有權。</summary>
            public int width { get; set; }
        }






        /// <summary>舊 action 型別、顯示文字、資料及範圍的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class ActionClass
        {
            /// <summary>舊 JSON 型別代碼，保留大小寫；不含額外資源所有權。</summary>
            public string type { get; set; }
            /// <summary>action 顯示標籤，不作身分；不含額外資源所有權。</summary>
            public string label { get; set; }
            /// <summary>action 原始產品資料，只供當次組裝；不含額外資源所有權。</summary>
            public string data { get; set; }
            /// <summary>顯示本文，可能含產品資料，不應寫入診斷；不含額外資源所有權。</summary>
            public string text { get; set; }
            /// <summary>action 目標 URI，本 DTO 不驗證或開啟；不含額外資源所有權。</summary>
            public string uri { get; set; }
            /// <summary>舊 action 連結 URI，本 DTO 不開啟；不含額外資源所有權。</summary>
            public string linkUri { get; set; }


            /// <summary>action 的圖片矩形範圍；不含額外資源所有權。</summary>
            public AreaClass area { get; set; }

        }

        /// <summary>Imagemap 可點擊矩形的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class AreaClass
        {
            /// <summary>矩形左上角 X 像素；不含額外資源所有權。</summary>
            public int x { get; set; }
            /// <summary>矩形左上角 Y 像素；不含額外資源所有權。</summary>
            public int y { get; set; }
            /// <summary>以像素表示的寬度；不含額外資源所有權。</summary>
            public int width { get; set; }
            /// <summary>以像素表示的高度；不含額外資源所有權。</summary>
            public int height { get; set; }
        }



        #endregion

        #region LINE 訊息資料傳輸類別

        /// <summary>舊媒體長度、類型及操作內位元組的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class MessageContent
        {
            /// <summary>媒體位元組長度的舊欄位；不含額外資源所有權。</summary>
            public int ContentLength { get; set; }
            /// <summary>媒體類型字串；不含額外資源所有權。</summary>
            public string ContentType { get; set; }
            /// <summary>當次媒體位元組清單，不可長期快取；不含額外資源所有權。</summary>
            public List<byte> RawBytes { get; set; }
        }


        /// <summary>舊產品 profile 顯示資料的可變資料容器；只供產品操作內組裝，不執行 HTTP／授權，不可跨使用者快取。</summary>
        public class UserProfile
        {
            //"displayName":"LINE taro",
            //"userId":"Uxxxxxxxxxxxxxx...",
            //"pictureUrl":"http://obs.line-apps.com/...",
            //"statusMessage":"Hello, LINE!"
            /// <summary>顯示名，不作穩定身分；不含額外資源所有權。</summary>
            public string DisplayName { get; set; }
            /// <summary>LINE ID；須由外層核對實際產品授權；不含額外資源所有權。</summary>
            public string UserId { get; set; }
            /// <summary>個人圖片 URL，不在 DTO 下載；不含額外資源所有權。</summary>
            public string PictureUrl { get; set; }
            /// <summary>profile 顯示狀態文字，僅限當次產品用途；不含額外資源所有權。</summary>
            public string StatusMessage { get; set; }
        }

    #endregion
}
