// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/BaseChurchController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class BaseChurchController
// 主要成員：HandleError、SendLineErrorNotification、SetMultiGroupLayoutParameter、ResolveDonationManagementAccessFlag、IsIntegrateDataLoaded、SetupBasicViewBag、SetupMemberInfoViewBag、SetupFeeDataListCount、EnsureCorrectUserData、GetStableHash
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、ChurchReport.Services.Donation、ChurchReport.Services.MemberInfo、ChurchReport.Tools、ChurchReport.Services、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Services.Donation;
using ChurchReport.Services.MemberInfo;
using ChurchReport.Tools;
using ChurchReport.Services;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// ???梯”?箏??批??(Base Controller for Church Reports)
    ///
    /// ?飛隤芣?嚗?
    /// ?銝?鞊∪摨??伐????????批?券?匱?輯???乓?
    /// ?箔?暻潮?閬摨?嗅嚗?
    /// - ?踹???隞?Ⅳ嚗??梁???踝?憒隤方??ession 撽?嚗?券ㄐ??
    /// - 蝯曹?銵嚗Ⅱ靽???嗅?賣??詨??隤方???摰瑼Ｘ??
    /// - 靘陷瘜典嚗?銝剔恣???冽???瘜典??
    ///
    /// 閮剛?璅∪?嚗?
    /// - Template Method Pattern嚗?靘瘚?嚗?摮??亥?撖怎摰郊撽?
    /// - Dependency Injection嚗??湔?萄遣靘陷嚗敺??冽釣?乓?
    /// - Singleton Pattern嚗?鈭???憒?ToolUtility嚗?桐???
    ///
    /// 雿輻?孵?嚗?
    /// public class MyController : BaseChurchController
    /// {
    ///     public MyController(...) : base(...) { }
    ///
    ///     public IActionResult MyAction()
    ///     {
    ///         // ?臭誑?湔雿輻?箏?憿?惇?改?憒?ToolUtility, InMemoryContext 蝑?
    ///     }
    /// }
    /// </summary>
    public abstract class BaseChurchController : Controller, IDisposable
    {
        #region 撣豢摰儔 (Constants)

        /// <summary>
        /// ?亥?閮??蜇撅斤? (Total logging level)
        ///
        /// ?飛隤芣?嚗?
        /// ?其?璆剜??其葉嚗隤?撅斤?蝞∠?嚗?
        /// - Level 1: ?箸鞈?
        /// - Level 2: 閰喟敦鞈?
        /// - Level 3: ?日鞈?
        /// - Level 4: 霅血?
        /// - Level 5: ?航炊
        ///
        /// ?ㄐ摰儔鈭虜?函?撅斤?撣豢嚗靘輻絞銝雿輻??
        /// </summary>
        protected const int TOTAL_LEVEL = 1;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;


        /// <summary>
        /// 雿輻??霅翰????嚗?嚗?
        /// ??芸?嚗??銝?冽???隢???銴?霅?
        /// </summary>
        private const int USER_VALIDATION_CACHE_SECONDS = 30;

        #endregion

        #region ??撖虫? (Service Instances)

        /// <summary>
        /// ToolUtility ????(ToolUtility Provider)
        ///
        /// ?飛隤芣?嚗?
        /// 隞暻潭 ToolUtility嚗?
        /// - ?銝?極?琿??伐????亥?閮??RM ??蝑??賬?
        /// - ?箔?暻潛???芋撘?? ToolUtility ?臬靘?嚗?閬絞銝蝞∠???
        /// - 靘陷瘜典嚗??湔?萄遣撖虫?嚗敺??冽釣?伐?蝚血? SOLID ????
        ///
        /// 閮剛?璅∪?嚗rovider Pattern
        /// </summary>
        protected readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// CRM ???瘙?(CRM Connection Pool)
        ///
        /// ?飛隤芣?嚗?
        /// 隞暻潭???瘙?
        /// - CRM 蝟餌絞???敺?皞?銝瘥活?賣撱粹????
        /// - ???瘙??遣蝡????嚗?銴蝙?剁????扯??
        /// - ?園???典????芸?甇賊??唳?銝准?
        ///
        /// 閮剛?璅∪?嚗bject Pool Pattern
        /// </summary>
        protected readonly ICrmConnectionPool _connectionPool;

        /// <summary>
        /// HTTP 銝???? (HTTP Context Accessor)
        ///
        /// ?飛隤芣?嚗?
        /// ?箔?暻潮?閬?
        /// - ??ASP.NET Core 銝哨?HttpContext 銝蝮賣?舐??撠文?刻??臭遙?葉嚗?
        /// - IHttpContextAccessor ??摰?撘?摮??嗅?隢???銝???
        /// - ? ASP.NET Core 靘陷瘜典蝟餌絞???典???
        /// </summary>
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// 雿輻??霅翰??
        /// ??芸?嚗??銝?冽?函???折?銴?霅?
        ///
        /// ?? 摰閮剛?嚗?
        /// - Key: SessionId + PasswordHash嚗甇?Session Collision嚗?
        /// - Value: (LastValidated, IsValid, PasswordHash)
        /// - 撽???瘥?撖Ⅳ??嚗Ⅱ靽?嗉澈隞賭???
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastValidated, bool IsValid, string PasswordHash)>
            _userValidationCache = new();

        /// <summary>
        /// 撌亙憿撖虫? (Tool Utility Instance)
        ///
        /// ?飛隤芣?嚗?
        /// ?惇?扳?靘? ToolUtility ????
        /// ?箔?暻潛撅祆扯??舐?亙???_toolUtilityProvider嚗?
        /// - 蝪∪?隞?Ⅳ嚗?憿?臭誑?湔??ToolUtility嚗??函??靘?
        /// - 撱園頛嚗??閬???????敺祕靘?
        /// </summary>
        protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

        /// <summary>
        /// 閮擃???銝? (In-Memory Data Context)
        ///
        /// ?飛隤芣?嚗?
        /// 隞暻潭閮擃???銝?嚗?
        /// - 摮?蝔???????憒?嗉?閮?蝯?????
        /// - ?箔?暻潛隞嚗??箏隞亥?擛?????撖虫?嚗葫閰衣???Ｙ嚗?
        /// - 靘陷瘜典嚗?憭瘜典嚗?′蝺函Ⅳ靘陷??
        /// </summary>
        protected readonly IInMemoryDataContext InMemoryContext;

        /// <summary>
        /// ????隞 (Payment Service Interface)
        ///
        /// ?飛隤芣?嚗?
        /// 鞎痊??隞狡?賊??平??頛胯?
        /// ?箔?暻潛隞嚗??箏隞交?銝???甈暹?靘?靽∠?～INE Pay 蝑???
        /// 閮剛?璅∪?嚗trategy Pattern
        /// </summary>

        /// <summary>
        /// 摰??HttpContext 摮? (Safe HttpContext Access)
        ///
        /// ?飛隤芣?嚗?
        /// ?惇?扳?靘??函??孵?靘???HttpContext??
        /// ?箔?暻潮?閬畾???
        /// - Controller ??HttpContext ?典遣瑽撘葉?航??????
        /// - 雿輻 IHttpContextAccessor ?臭誑?冽?摰摮???
        /// - 憒??賭??舐嚗??箇撣賂??脫迫?梯??航炊??
        ///
        /// 閮剛???嚗?
        /// - ?芸?雿輻 IHttpContextAccessor嚗?舫?嚗?
        /// - 憒?憭望?嚗?閰血憿? HttpContext??
        /// - 憒??賢仃?????蝢拍??航炊閮??
        /// </summary>
        protected new HttpContext HttpContext
        {
            get
            {
                // ?芸?雿輻 IHttpContextAccessor嚗?舫?嚗?
                var context = _httpContextAccessor?.HttpContext;

                // 憒? IHttpContextAccessor 瘝???嚗?閰虫蝙?典憿? HttpContext
                if (context == null)
                {
                    context = base.HttpContext;
                }

                // 憒?隞??null嚗??箸??儔?撣?
                if (context == null)
                {
                    throw new InvalidOperationException(
                        "HttpContext is not available. Ensure the request is running inside an ASP.NET Core HTTP pipeline and IHttpContextAccessor is registered.");
                }

                return context;
            }
        }

        #endregion

        #region 撱箸??賢? (Constructor)

        /// <summary>
        /// ???摨?嗅 (Initialize Base Controller)
        ///
        /// ?飛隤芣?嚗?
        /// 撱箸??賢??舫??亥◤?萄遣??銵??寞???
        /// ?ㄐ??鈭?嚗?
        /// 1. 撽??嚗Ⅱ靽?閬????質◤瘜典??
        /// 2. 靽???撠釣?亦???摮絲靘?敺?雿輻??
        /// 3. ????銝?嚗遣蝡?瘜典閮擃???銝???
        ///
        /// 靘陷瘜典?暺?
        /// - 擛血?嚗??乩?靘陷?琿?撖虫???
        /// - ?舀葫閰行改??臭誑頛??典??拐辣?踵??祕????
        /// - ?暑?改??臭誑?寞??啣?瘜典銝??祕雿?
        ///
        /// ?隤芣?嚗?
        /// - httpContextAccessor: 摮? HTTP 隢?銝???
        /// - memoryCache: 閮擃翰????
        /// - paymentService: 隞狡????
        /// - toolUtilityProvider: 撌亙憿????
        /// - connectionPool: CRM ???瘙?
        /// - inMemoryContext: 閮擃???銝?嚗?賂????詨捆嚗?
        /// </summary>
        protected BaseChurchController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            IInMemoryDataContext inMemoryContext = null)
        {
            // ========================================
            // ?靽桀儔嚗?摮?IHttpContextAccessor
            // ========================================
            // ?靽桀儔 "HttpContext ?芸?憪?" ?航炊????
            // ??靽? IHttpContextAccessor嚗??隞亙隞颱????典?? HttpContext
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            // ?? DI 瘜典 ToolUtility ????
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));

            // ?舀?拍車?孵?嚗?
            // 1. ?唳撘??? DI 瘜典閮擃???銝?嚗?佗??踹???靘陷嚗?
            // 2. ?撘??湔 new 撖虫?嚗?敺摰對??郊瘛掠嚗?
            if (inMemoryContext != null)
            {
                // 雿輻 DI 瘜典?祕靘??刻嚗?
                InMemoryContext = inMemoryContext;
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] 雿輻 DI 瘜典??InMemoryContext");
            }
            else
            {
                // ???詨捆嚗?亙遣蝡祕靘?撠郊瘛掠嚗?
                // 敺?DI ??銝剜抒?憟隞狡撱箏 adapter??
                // Base controller ?芷?閬? ChurchReport session/context 銝脰絲靘?銝??誑 QPay 憿?雿銝餉??亙??
                var donationPaymentCreateGatewayAdapter =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(IDonationPaymentCreateGatewayAdapter))
                        as IDonationPaymentCreateGatewayAdapter;
                var lineNotificationWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineNotificationWorkflow))
                        as ILineNotificationWorkflow;
                var lineReplyWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineReplyWorkflow))
                        as ILineReplyWorkflow;
                InMemoryContext = new InMemoryDataContextSmallGroup(
                    httpContextAccessor, memoryCache, toolUtilityProvider, donationPaymentCreateGatewayAdapter, lineNotificationWorkflow, lineReplyWorkflow);
                System.Diagnostics.Debug.WriteLine("[BaseChurchController] Created InMemoryContext from DI services.");
            }

            // 摮??????
        }

        #endregion

        #region ?航炊?? (Error Handling)

        /// <summary>
        /// 蝯曹??航炊???寞? (Unified Error Handling Method)
        ///
        /// ?飛隤芣?嚗?
        /// ?箔?暻潮?閬絞銝?航炊??嚗?
        /// - ?踹?瘥瘜??撖?try-catch??
        /// - 蝣箔??航炊鋡急迤蝣箄??????
        /// - ??銝?渡??航炊???澆???
        ///
        /// ??瘚?嚗?
        /// 1. 閮??航炊?唳隤?
        /// 2. ?潮?LINE ?蝯衣恣?
        /// 3. ?寞?隢?憿?餈??拍????
        ///
        /// ?嚗?
        /// - exception: ?潛??撣貊隞?
        /// - methodName: ?潛??航炊?瘜?蝔梧??冽餈質馱嚗?
        ///
        /// 餈??潘?
        /// - AJAX 隢?嚗???JSON ?航炊閮
        /// - 銝?祈?瘙?????航炊?
        /// </summary>
        protected IActionResult HandleError(Exception exception, string methodName)
        {
            // 蝯??航炊閮
            string errorMessage = $"?航炊閮 : FullName = {GetType().FullName}, " +
                                $"Method = {methodName}, " +
                                $"Time = {DateTime.Now}, " +
                                $"Description = {exception}";

            // 撖怠餈質馱?亥? (? null 瑼Ｘ)
            try
            {
                ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1, errorMessage);
            }
            catch (Exception traceEx)
            {
                // 餈質馱憭望?銝蔣?輸隤方???蝔?
                System.Diagnostics.Debug.WriteLine($"TraceByLevel 憭望?: {traceEx.Message}");
            }

            // ?潮?LINE ?
            SendLineErrorNotification(errorMessage);

            // ?斗?臬??AJAX 隢? (? null 瑼Ｘ)
            bool isAjaxRequest = false;
            try
            {
                isAjaxRequest = Request?.Headers != null &&
                               Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            catch
            {
                // ?⊥??斗隢?憿?嚗?閮剔??AJAX
                isAjaxRequest = false;
            }

            if (isAjaxRequest)
            {
                // AJAX 隢?餈? JSON
                return Json(new
                {
                    status = "error",
                    message = exception.Message,
                    timestamp = DateTime.Now
                });
            }
            else
            {
                // 銝?祈?瘙??隤日???
                return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = exception.Message });
            }
        }

        /// <summary>
        /// ?潮?LINE ?航炊? (Send LINE Error Notification)
        ///
        /// ?飛隤芣?嚗?
        /// ?嗥頂蝯梁??隤斗?嚗蜓?蝞∠??～?
        /// ?箔?暻潛 LINE嚗?
        /// - ?單??改?蝞∠??∪隞亦??單?圈??
        /// - 靘踹?改???銝停?賜??啜?
        /// - ?舫??改??喃蝙?萎辣蝟餌絞??嚗INE ?虜??具?
        ///
        /// ?航炊??嚗?
        /// - 憒? LINE ?潮仃??銝蔣?蹂蜓閬平??蝔?
        /// - ????LINE ?潮仃???亥???
        /// </summary>
        protected void SendLineErrorNotification(string errorMessage)
        {
            try
            {
                ChurchReportLineAdminNotificationService.NotifyDefaultError("BaseChurchController", errorMessage);
            }
            catch (Exception ex)
            {
                // LINE ?憭望?銝蔣?蹂蜓閬?蝔?
                try
                {
                    ToolUtility?.TraceByLevel(TOTAL_LEVEL, LEVEL_1,
                        $"LINE ??潮仃?? {ex.Message}");
                }
                catch
                {
                    // 憒???蕭頩日憭望?嚗蝙??Debug 頛詨
                    System.Diagnostics.Debug.WriteLine($"LINE ??潮仃??餈質馱憭望?: {ex.Message}");
                }
            }
        }

        #endregion

        #region ViewBag 閮剖?頛?寞? (ViewBag Setup Helper Methods)

        /// <summary>
        /// 閮剖?憭?蝯??Ｗ???(Set Multi-Group Layout Parameters)
        ///
        /// ?飛隤芣?嚗?
        /// ViewBag ??ASP.NET MVC 銝剔靘??? View ???嗚?
        /// ?瘜捱摰??Ｘ?閰脤＊蝷箔?暻潭見??閬賡?柴?
        ///
        /// 璆剖??摩嚗?
        /// - 憒??臬?撠?璅∪?銝頛?孵?撠?嚗＊蝷箏蝝?憭?蝯???
        /// - 憒??舀?芋撘?撌脰??亥???憿舐內?桐?撠??底蝝啗???
        /// - 憒??舀毽?芋撘?憿舐內?拙??蝯梯? + 閰喟敦嚗?
        ///
        /// ?箔?暻潮?閬?
        /// - ?冽擃?嚗??嗥??＊蝷粹?嗥??賊?
        /// - 撠?摩嚗Ⅱ靽?嗡??蕙頝?
        /// </summary>
        protected void SetMultiGroupLayoutParameter()
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            bool integrateFlag = IsIntegrateDataLoaded();

            // 靽格迤嚗Ⅱ靽? MultiGroupView 暺?撠?敺?靽? HybridView 璅∪?
            // 霈??梁絞閮???蝯??晞?憿舐內
            if (displayViewType == "MultiGroupView" && !integrateFlag)
            {
                // 蝝?撠?璅∪?嚗??芷??遙雿?蝯?
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            }
            else if (displayViewType == "IntegrateView" && integrateFlag)
            {
                // ?桐?撠?璅∪?嚗????蝯?
                ViewBag.MultiGroupIndex = "IntegrateView";
            }
            else if (displayViewType == "MultiGroupView" && integrateFlag)
            {
                // 瘛瑕?璅∪?嚗?憭?蝯?撌脤??銝凋???
                // 甇斗??府憿舐內???梁絞閮???蝯??晞???
                ViewBag.MultiGroupIndex = "HybridView";
            }
            else
            {
                // ?身??IntegrateView ??HybridView
                // 憒????交????撠曹蝙??HybridView嚗Ⅱ靽??瘨仃
                ViewBag.MultiGroupIndex = integrateFlag ? "HybridView" : "IntegrateView";
            }

            // 「奉獻管理」按鈕屬於全站導覽列權限，不應依賴奉獻付款頁面的表單模型是否已初始化。
            // 先用目前登入者 CRM contact.new_church_jobtitle 判斷；只有登入 contact 尚未載入時，
            // 才保留舊的 DonationPaymentFormModel.IsAOfficeWorker 作為最後 fallback。
            ViewBag.IsAOfficeWorker = ResolveDonationManagementAccessFlag();
        }

        /// <summary>
        /// 解析 Layout 是否要顯示「奉獻管理／奉獻稽核」導覽入口。
        ///
        /// 根因說明：
        /// _Layout.cshtml 每一頁都會渲染，但 DonationPaymentManager.m_DonationPaymentFormModel
        /// 只會在奉獻付款流程初始化後才具有完整狀態。若使用者剛登入或停留在回報統計等非奉獻頁，
        /// 直接讀表單模型會得到預設 false，導致原本有權限的會計同工看不到「奉獻管理」按鈕。
        ///
        /// 正確資料來源是登入者的 CRM contact 職稱；奉獻付款表單狀態只能作為舊流程相容 fallback。
        /// </summary>
        private string ResolveDonationManagementAccessFlag()
        {
            try
            {
                var personalModel = InMemoryContext?.PersonalInfomationModel;
                if (personalModel != null && personalModel.m_LoginContact == null)
                {
                    try
                    {
                        personalModel.SetPersonalInfomationViewModel();
                    }
                    catch
                    {
                        // 某些入口頁可能尚未能載入登入 contact；不要讓導覽列渲染失敗，改走 fallback。
                    }
                }

                var loginContact = personalModel?.m_LoginContact;
                if (loginContact != null)
                {
                    var toolUtility = ToolUtility;
                    var jobTitle = toolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
                    return DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle) ? "是的" : "否";
                }
            }
            catch
            {
                // 導覽列權限判斷不應中斷頁面輸出；下方 fallback 會維持舊流程可用。
            }

            return InMemoryContext?.DonationPaymentManager?.m_DonationPaymentFormModel?.IsAOfficeWorker == true
                ? "是的"
                : "否";
        }

        /// <summary>
        /// 瑼Ｘ?游?鞈??臬撌脰???(Check if Integrate Data is Loaded)
        ///
        /// ?飛隤芣?嚗?
        /// ?游?鞈??舀??孵?撠??底蝝啗?閮?
        /// ?箔?暻潮?閬炎?伐?
        /// - 蝣箔??冽??甇?Ⅱ????
        /// - ?踹?憿舐內蝛箇??隤斤?鞈?
        ///
        /// 瑼Ｘ璇辣嚗?
        /// - ?勗?拐辣摮
        /// - LoadFlag ??true嚗”蝷箄??歇頛嚗?
        /// </summary>
        protected bool IsIntegrateDataLoaded()
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
            return weeklyReport != null && weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 閮剖??箸 ViewBag ? (Set Basic ViewBag Parameters)
        ///
        /// ?飛隤芣?嚗?
        /// ?瘜身摰???嗅?賡?閬??箸 ViewBag ???
        /// ?箔?暻潸?蝯曹?閮剖?嚗?
        /// - ?踹?瘥?嗅??撖怎??隞?Ⅳ
        /// - 蝣箔?????ａ??閬?鞈?
        ///
        /// 閮剖????賂?
        /// - ?餃憿???嗅?蝔?
        /// - 鞎餌憿?
        /// - 敹急?撠?憿?
        /// - 鞎餌鞈????
        /// </summary>
        protected void SetupBasicViewBag()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            // 閮剖?蝜唾祥暺????
            SetupFeeDataListCount();
            SetupMemberInfoViewBag();
        }

        /// <summary>
        /// Setup member-info navigation access flag.
        /// </summary>
        protected void SetupMemberInfoViewBag()
        {
            try
            {
                var cached = HttpContext?.Session?.GetString("_MemberInfoAccess");
                if (!string.IsNullOrEmpty(cached))
                {
                    ViewBag.MemberInfoAccess = cached;
                    return;
                }

                var personalModel = InMemoryContext?.PersonalInfomationModel;
                if (personalModel != null && personalModel.m_LoginContact == null)
                {
                    try
                    {
                        personalModel.SetPersonalInfomationViewModel();
                    }
                    catch
                    {
                        // Login contact may not be ready on some entry requests. Do not cache a negative result.
                    }
                }

                var loginContact = personalModel?.m_LoginContact;
                if (loginContact == null)
                {
                    ViewBag.MemberInfoAccess = null;
                    return;
                }

                var toolUtility = ToolUtility;
                var jobTitle = toolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
                var loginType = InMemoryContext?.ListManager?.LoginType ?? string.Empty;
                var access = MemberInfoAccessResolver.Resolve(jobTitle, loginType);

                if (!string.IsNullOrEmpty(access))
                {
                    HttpContext?.Session?.SetString("_MemberInfoAccess", access);
                }

                ViewBag.MemberInfoAccess = access;
            }
            catch
            {
                ViewBag.MemberInfoAccess = null;
            }
        }
        /// <summary>
        /// 閮剖?蝜唾祥暺?鞈??賊????(Set Fee Data List Count Status)
        ///
        /// ?飛隤芣?嚗?
        /// ?瘜炎?交?行?鞎餌鞈?嚗蒂閮剖?撠??????胯?
        /// ?箔?暻潮?閬?
        /// - 霈?嗥?頂蝯曹葉?臬????
        /// - ??閬死??
        ///
        /// ????荔?
        /// - ????"蝜唾祥???歇????
        /// - ?∟???"蝜唾祥?????∟???
        /// </summary>
        protected void SetupFeeDataListCount()
        {
            bool hasFeeData = InMemoryContext.FeeList.FeeDataList != null &&
                            InMemoryContext.FeeList.FeeDataList.Count > 0;

            ViewBag.FeeDataListCount = hasFeeData ? "已載入收費資料" : "尚未載入收費資料";
        }

        #endregion

        #region Session 摰撽? (Session Security Validation)

        /// <summary>
        /// 蝣箔? AJAX 隢?雿輻甇?Ⅱ?冽????(Ensure Correct User Data for AJAX Requests)
        ///
        /// ? ??芸??嚗?
        /// - 雿輻閮擃翰???銴?霅?30 蝘嚗?
        /// - 璇辣??Debug 頛詨嚗??券?閬?嚗?
        /// - ??餈?嚗ail-Fast嚗?撠?敹??炎??
        ///
        /// ?飛隤芣?嚗?
        /// ??AJAX 隢?銝哨??冽??Session ?航撌脩??寡???
        /// ?瘜Ⅱ靽??蝙?函??舀迤蝣箇??冽鞈???
        ///
        /// ??芸?蝑嚗?
        /// 1. **敹怠?撽?蝯?**嚗?0 蝘銝?銴?霅?銝?冽
        /// 2. **Lazy Loading**嚗?券?閬?????Session
        /// 3. **璇辣 Logging**嚗?函???湔?????
        /// 4. **??餈?**嚗??衣Ⅱ隤??停蝡餈?
        /// </summary>
        protected virtual void EnsureCorrectUserData()
        {
            try
            {
                // ========================================
                // Step 0: 敹怠?瑼Ｘ嚗??賢???詨?嚗?
                // ========================================
                var sessionId = HttpContext?.Session?.Id;
                if (string.IsNullOrEmpty(sessionId))
                {
                    return; // ??Session嚗?撽?
                }

                // ========================================
                // Step 1: Lazy Loading - ?芸?閬?霈??Session
                // ========================================
                var sessionPassword = HttpContext?.Session?.GetString("_LoginPassword");
                var listManagerPassword = InMemoryContext?.ListManager?.m_Password;

                // 憒??抵?箇征嚗?撽?
                if (string.IsNullOrEmpty(sessionPassword) && string.IsNullOrEmpty(listManagerPassword))
                {
                    return;
                }

                // 閮??嗅?撖Ⅳ???券?皝??冽敹怠? Key嚗?
                var currentPasswordHash = GetStableHash(sessionPassword ?? listManagerPassword ?? "");
                var cacheKey = $"{sessionId}_{currentPasswordHash}";

                // ========================================
                // Step 2: 摰?翰?炎?伐??? ?脫迫 Session Collision嚗?
                // ========================================
                if (_userValidationCache.TryGetValue(cacheKey, out var cached))
                {
                    var cacheAge = (DateTime.UtcNow - cached.LastValidated).TotalSeconds;

                    // ? 敹怠??賭葉 + 撖Ⅳ??銝?湛???撽?嚗?
                    if (cacheAge < USER_VALIDATION_CACHE_SECONDS &&
                        cached.IsValid &&
                        cached.PasswordHash == currentPasswordHash)
                    {
                        // 敹怠??賭葉銝??剁??湔餈?
                        return;
                    }
                }

                // ========================================
                // Step 3: 敹怠?憭望???摮嚗銵??湧?霅?
                // ========================================
                var sessionAccount = HttpContext?.Session?.GetString("_LoginAccount");

                // 瑼Ｘ Session ??ListManager ??蝣潭?虫???
                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword == listManagerPassword)
                {
                    // ? 撽???嚗?啣翰??
                    _userValidationCache[cacheKey] = (DateTime.UtcNow, true, currentPasswordHash);

                    // 皜??? Session ??敹怠??嚗?蝣潸??湔?嚗?
                    CleanupOldCacheForSession(sessionId, cacheKey);
                    return;
                }

                // ========================================
                // Step 4: ??銝??湛??閬??啗???
                // ========================================
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] ??銝??湛??頛 ListManager 鞈?");
#endif

                if (!string.IsNullOrEmpty(sessionPassword) &&
                    !string.IsNullOrEmpty(listManagerPassword) &&
                    sessionPassword != listManagerPassword)
                {
                    // ?頛 ListManager 鞈?
                    InMemoryContext.ListManager.SetupListManager(
                        sessionAccount ?? "",
                        sessionPassword,
                        InMemoryContext.ListManager.m_SelectDate != default
                            ? InMemoryContext.ListManager.m_SelectDate
                            : DateTime.Now);

                    // 雿輻?啁?撖Ⅳ???湔敹怠?
                    var newPasswordHash = GetStableHash(sessionPassword);
                    var newCacheKey = $"{sessionId}_{newPasswordHash}";
                    _userValidationCache[newCacheKey] = (DateTime.UtcNow, true, newPasswordHash);

                    // 皜??翰??
                    CleanupOldCacheForSession(sessionId, newCacheKey);
                    return;
                }

                // ========================================
                // Step 5: 憒? Session 撖Ⅳ?箇征嚗?閰血?隢?銝剖?敺?LINE ID
                // ========================================
                if (string.IsNullOrEmpty(sessionPassword))
                {
                    var principal = HttpContext?.User;
                    var loginType = principal?.FindFirst(ChurchReport.Security.LoginClaimsFactory.LoginTypeClaim)?.Value;
                    var passwordKey = principal?.FindFirst(ChurchReport.Security.LoginClaimsFactory.PasswordKeyClaim)?.Value;

                    if (principal?.Identity?.IsAuthenticated == true &&
                        loginType == "LINE" &&
                        !string.IsNullOrEmpty(passwordKey) &&
                        passwordKey != listManagerPassword)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] Session ???箇征嚗蝙??LINE ID ?頛");
#endif

                        InMemoryContext.ListManager.SetupListManager(
                            "LineIdLogin",
                            passwordKey,
                            InMemoryContext.ListManager.m_SelectDate != default
                                ? InMemoryContext.ListManager.m_SelectDate
                                : DateTime.Now);

                        HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                        HttpContext?.Session?.SetString("_LoginPassword", passwordKey);

                        // ?湔敹怠?
                        var linePasswordHash = GetStableHash(passwordKey);
                        var lineCacheKey = $"{sessionId}_{linePasswordHash}";
                        _userValidationCache[lineCacheKey] = (DateTime.UtcNow, true, linePasswordHash);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[BaseChurch.EnsureCorrectUserData] 撽?憭望?: {ex.Message}");
#endif
            }
        }

        /// <summary>
        /// 閮?蝛拙???蝣潮?皝??冽敹怠? Key嚗?
        ///
        /// ?? 摰隤芣?嚗?
        /// - 雿輻 SHA256 ??嚗甇Ｗ?蝣潭??摮?
        /// - ?? 8 摮?撟唾﹛摰?扯? Key ?瑕漲
        /// - ??蝣唳?璈?嚗? / 2^32嚗扔雿?
        ///
        /// ?箔?暻潔??湔?典?蝣潘?
        /// - 摰?改??踹?撖Ⅳ瘣拇??唳隤?閮擃翰??
        /// - Key ?瑕漲嚗??Dictionary Key 憭批?
        /// </summary>
        private static string GetStableHash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "EMPTY";

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
                var hash = Convert.ToBase64String(bytes);
                return hash.Length > 8 ? hash.Substring(0, 8) : hash;
            }
        }

        /// <summary>
        /// 皜??? Session ??敹怠??
        ///
        /// ?? 閮擃恣??
        /// - ?脫迫撖Ⅳ霈敺???翰?敞蝛?Memory Leak嚗?
        /// - ?脫迫 Session ID ??? Session Collision
        /// - ?噶皜??典????嚗???5 ??嚗?
        ///
        /// 皜?蝑嚗?
        /// 1. 蝘駁?? Session 雿?蝣潮?皝????嚗?嗅?蝣潸??湛?
        /// 2. 蝘駁?????5 ???????殷?閮擃???
        /// 3. 靽??嗅????翰????
        /// </summary>
        private static void CleanupOldCacheForSession(string sessionId, string currentCacheKey)
        {
            try
            {
                var keysToRemove = new System.Collections.Generic.List<string>();
                var now = DateTime.UtcNow;

                foreach (var kvp in _userValidationCache)
                {
                    // 瑼Ｘ 1嚗?銝 Session 雿?蝣潔??????殷?Session Collision ?脰風嚗?
                    if (kvp.Key.StartsWith(sessionId + "_") && kvp.Key != currentCacheKey)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                    // 瑼Ｘ 2嚗?????5 ???????殷?Memory Leak ?脰風嚗?
                    else if ((now - kvp.Value.LastValidated).TotalMinutes > 5)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                // ?寞活蝘駁
                foreach (var key in keysToRemove)
                {
                    _userValidationCache.TryRemove(key, out _);
                }

#if DEBUG
                if (keysToRemove.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CleanupOldCache] 撌脫???{keysToRemove.Count} ?翰???殷?Session={sessionId.Substring(0, Math.Min(8, sessionId.Length))}...)");
                }
#endif
            }
            catch
            {
                // 皜?憭望?銝蔣?蹂蜓瘚?
            }
        }


        /// <summary>
        /// ?岫敺?瘙葉?? LINE ?冽 ID (Try to Get LINE User ID from Request)
        ///
        /// ?飛隤芣?嚗?
        /// LINE ?餃???冽 ID ???怠隢???Referer 銝准?
        /// ?瘜圾??冽 ID??
        ///
        /// 閫???摩嚗?
        /// - 敺?HTTP Referer 璅銝剖???LINE ?冽 ID ?澆?
        /// - LINE ?冽 ID 隞?"U" ?嚗摨衣 33 ????
        ///
        /// ?箔?暻潮?閬?
        /// - ??Session ?箏仃???臭誑敺?瘙葉?Ｗ儔?冽頨思遢
        /// - ??蝟餌絞?捆?航??
        /// </summary>
        /// <summary>
        /// 撽??嗅? Session ?臬?? (Validate Current Session)
        ///
        /// ?飛隤芣?嚗?
        /// Session 撽???Web ?摰??閬??
        /// ?瘜炎?亦?嗥??餃???虫??嗆???
        ///
        /// 閮剛?璅∪?嚗emplate Method Pattern
        /// - ?????霅?蝔?
        /// - 摮??亙隞亥?撖怎摰炎??
        ///
        /// Fail-Fast ??嚗?
        /// - 銝?潛??撠梁??唾???false
        /// - 銝匱蝥銵?敹??炎??
        ///
        /// 撽??嚗?
        /// 1. Session ?臬摮
        /// 2. Session ?臬??嚗? 撠?嚗?
        /// 3. ?冽頨思遢?臬銝??
        ///
        /// 雿輻?孵?嚗?
        /// ??Controller Action ????恬?
        /// if (!ValidateSession())
        /// {
        ///     return RedirectToAction("Login", "Authentication");
        /// }
        /// </summary>
        protected bool ValidateSession()
        {
            try
            {
                // ========================================
                // Step 1: 瑼Ｘ Session ?臬摮
                // ========================================
                var sessionUserId = HttpContext.Session.GetString("_SessionUserId");
                if (string.IsNullOrEmpty(sessionUserId))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] Session user id is missing.");
                    return false;
                }

                // ========================================
                // Step 2: 瑼Ｘ Session ?萄遣??嚗甇ａ???Session嚗?
                // ========================================
                var sessionCreatedAt = HttpContext.Session.GetString("_SessionCreatedAt");
                if (!string.IsNullOrEmpty(sessionCreatedAt))
                {
                    if (DateTime.TryParse(sessionCreatedAt, out DateTime createdTime))
                    {
                        var sessionAge = DateTime.UtcNow - createdTime;
                        // Session 頞? 8 撠?閬??嚗?憭?霅瑕惜嚗?
                        if (sessionAge.TotalHours > 8)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 撌脤???({sessionAge.TotalHours:F2} 撠?)");
                            return false;
                        }
                    }
                }

                // ========================================
                // Step 3: 撽??冽頨思遢銝?湔?
                // ========================================
                // 瑼Ｘ InMemoryContext 銝剔??冽鞈??臬??Session 銝??
                var currentAccount = InMemoryContext?.ListManager?.m_Account;
                if (string.IsNullOrEmpty(currentAccount))
                {
                    System.Diagnostics.Debug.WriteLine("[ValidateSession] ?冽鞈?銝??冽 InMemoryContext");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 撽??? - UserId: {sessionUserId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateSession] Session 撽?憭望?: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撘瑕??? Session ID (Regenerate Session ID)
        ///
        /// ?飛隤芣?嚗?
        /// Session ID ????臬??冽?雿喳祕??
        /// ?箔?暻潮?閬?
        /// - ?脫迫 Session Fixation ?餅?
        /// - ?冽??霈?蝣箔?摰
        ///
        /// 雿輻??嚗?
        /// - 甈?霈敺?
        /// - ??????
        /// - 摰?摰瑼Ｘ
        ///
        /// 瘜冽?鈭?嚗?
        /// - 甇斗瘜?皜銝阡?撱?Session
        /// - 雿?靽??冽鞈?嚗蝙?冽???喉?
        /// </summary>
        protected void RegenerateSessionId()
        {
            try
            {
                // ?怠???鞈?
                var userId = HttpContext.Session.GetString("_SessionUserId");
                var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
                var realIp = HttpContext.Session.GetString("_SessionRealIp");

                // 皜??Session
                HttpContext.Session.Clear();

                // 撘瑕????Session ID
                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

                // ?Ｗ儔鞈?嚗蝙?冽???嚗?
                if (!string.IsNullOrEmpty(userId))
                {
                    HttpContext.Session.SetString("_SessionUserId", userId);
                    HttpContext.Session.SetString("_SessionIdentifier", $"{userId}_{DateTime.UtcNow.Ticks}");
                    HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
                    HttpContext.Session.SetString("_SessionUserAgent", userAgent ?? "");
                    HttpContext.Session.SetString("_SessionRealIp", realIp ?? "");
                }

                System.Diagnostics.Debug.WriteLine("[RegenerateSessionId] Session data cleared. ASP.NET Core does not rotate the Session ID here; identity is bound to the auth ticket.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerateSessionId] ???憭望?: {ex.Message}");
                throw;
            }
        }

        protected async System.Threading.Tasks.Task IssueAuthTicketAsync(string contactId, string account, string passwordKey, string loginType)
        {
            try
            {
                var principal = ChurchReport.Security.LoginClaimsFactory.Build(contactId, account, passwordKey, loginType);
                await HttpContext.SignInAsync(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    principal);

                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] Issued auth ticket. loginType={loginType}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IssueAuthTicket] Failed to issue auth ticket: {ex.Message}");
            }
        }

        /// <summary>
        /// ?脣???瘙絞閮?閮?(Get Connection Pool Statistics)
        ///
        /// ?飛隤芣?嚗?
        /// ????瘙????????
        /// ?瘜???瘙?蝯梯?鞈???
        ///
        /// 蝯梯?鞈??嚗?
        /// - 蝮賡???
        /// - 瘣餉?????
        /// - ?蔭????
        /// - 蝑?隢???
        /// - ??/?閮
        /// - 頞???霅仃????
        ///
        /// ?箔?暻潮?閬?
        /// - ?扯??嚗?暸?瘙?憿?
        /// - 摰寥?閬?嚗捱摰?阡?閬?????
        /// - ??閮箸嚗蕭頩日?雿輻璅∪?
        /// </summary>
        protected ConnectionPoolStats GetConnectionPoolStats()
        {
            try
            {
                if (_connectionPool == null)
                {
                    return new ConnectionPoolStats
                    {
                        TotalConnections = 0,
                        ActiveConnections = 0,
                        IdleConnections = 0,
                        WaitingRequests = 0,
                        TotalAcquireCount = 0,
                        TotalReleaseCount = 0,
                        TimeoutCount = 0,
                        ValidationFailureCount = 0
                    };
                }

                return _connectionPool.GetStats();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetConnectionPoolStats] ?脣?蝯梯?鞈?憭望?: {ex.Message}");

                // 餈?蝛箇絞閮?閮?
                return new ConnectionPoolStats
                {
                    TotalConnections = 0,
                    ActiveConnections = 0,
                    IdleConnections = 0,
                    WaitingRequests = 0,
                    TotalAcquireCount = 0,
                    TotalReleaseCount = 0,
                    TimeoutCount = 0,
                    ValidationFailureCount = 0
                };
            }
        }

        #endregion

        #region 鞈?? (Resource Disposal)

        /// <summary>
        /// 釋放本 Controller 自身的資源。本方法由 ASP.NET Core 於每個 request 結束時呼叫。
        /// </summary>
        /// <remarks>
        /// 重要：本方法「刻意不」釋放 <c>ToolUtility</c>，請勿還原該呼叫。
        ///
        /// 生命週期不相符是唯一理由：<c>ToolUtility</c> 取自
        /// <c>IToolUtilityProvider.GetToolUtility()</c>，其實作 <c>ToolUtilityFactory.GetInstance()</c>
        /// 回傳的是「程序級單一實例」（static 欄位，double-check locking 建立），
        /// 存活期等同整個 Worker Process；而 Controller 是 per-request 物件，存活期僅一次請求。
        /// 由短命物件去釋放長命物件，等同「一個請求結束時把整個程序共用的資源關掉」，
        /// 之後所有請求都會操作到已釋放的物件。
        ///
        /// 歷史背景（避免日後有人順手補回來）：此處原本確實呼叫 <c>ToolUtility?.Dispose()</c>。
        /// 它長期未造成可見故障，只因 <c>ToolUtilityClass.Dispose</c> 內的
        /// <c>(m_Crm2011OrganizationService as IDisposable)?.Dispose()</c>，
        /// 在 <c>OnPremiseClient</c> 尚未實作 <see cref="IDisposable"/> 之前是無作用的空操作。
        /// 一旦該型別補上確定性釋放，這個既有的生命週期錯誤立即顯現為
        /// <see cref="ObjectDisposedException"/>（ServiceChannel 已關閉），實測會使登入流程失敗。
        ///
        /// 釋放責任歸屬：程序級單例於程序結束時回收，不由任何 request 範圍或 operation 範圍物件負責。
        /// 本 Controller 自身的 request 範圍資源（含注入的 IOrganizationService 租約），
        /// 由基底類別與 DI 容器在 request scope 結束時確定性釋放，無須於此重複處理。
        /// </remarks>
        public new void Dispose()
        {
            // 不得在此釋放 ToolUtility —— 它是程序級單例，理由見上方 remarks。
            base.Dispose();
        }

        #endregion
    }
}
