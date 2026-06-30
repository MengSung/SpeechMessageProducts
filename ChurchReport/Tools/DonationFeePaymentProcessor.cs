using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ChurchReport.Payments;
using System;
using System.Collections.Generic;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;


namespace ChurchReport.Tools
{
    /// <summary>
    /// ChurchReport 收費單付款完成後的產品流程處理器。
    ///
    /// 【這個類別在整個金流流程中的位置】
    /// 1. 金流核心或舊版 adapter 已經完成「付款結果解析」，並把結果整理成 <see cref="DonationPaymentWorkflowResult"/>。
    /// 2. 本類別接手 ChurchReport 自己的產品後處理：查 CRM 收費單、判斷成功或失敗、更新 CRM 欄位、發 LINE、回傳結果頁。
    /// 3. 因為這些事情都依賴 ChurchReport 的 CRM 欄位、LINE 通知文案與課程/奉獻規則，所以不能放進可重用的金流核心。
    ///
    /// 【重要邊界】
    /// - 金流核心負責 provider 協定、簽章、加解密、callback parsing。
    /// - 本類別負責 ChurchReport 的 CRM、LINE、結果頁與舊版欄位相容。
    /// - 其他產品若要重用金流核心，應重用 <c>SpeechMessage.Payments</c> 與 workflow contract，
    ///   但 CRM/LINE 實作要由各產品自己提供，不應直接搬用本類別。
    /// </summary>
    public class DonationFeePaymentProcessor : Controller, IDisposable
    {
        #region 設定與配置
        // 使用 Lazy<IConfiguration> 的目的：
        // 1. 只有第一次真正需要設定時才讀 appsettings.json，避免建構類別時立刻做檔案 I/O。
        // 2. static 共用同一份設定，避免每次付款回傳都重新建立 IConfiguration。
        // 3. reloadOnChange: true 讓 appsettings.json 更新後可被組態系統重新載入。
        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });

        // 保留原本 m_Configuration 命名是為了降低舊程式變更量。
        // 它實際上是從 Lazy<IConfiguration> 取出的全域設定入口。
        private static IConfiguration m_Configuration => s_lazyConfiguration.Value;
        #endregion

        // LINE Messaging SDK 的 client，底層會使用 Channel Access Token 呼叫 LINE API。
        private LineMessagingClient m_LineMessagingClient { get; }

        // PushUtility 負責主動推播訊息給使用者；付款完成通知就是使用這條路徑。
        private PushUtility m_PushUtility { get; }

        // ReplyUtility 是舊流程留下的回覆工具；目前此類別主要使用 PushUtility。
        private ReplyUtility m_ReplyUtility { get; }

        // ToolUtilityClass 是 ChurchReport 存取 Dynamics CRM 的主要工具。
        // 這個類別內所有 RetrieveEntity、SetEntityStringAttribute、UpdateEntity 等 CRM 操作都透過它完成。
        private readonly ToolUtilityClass m_ToolUtilityClass;

        // 共通付款後流程負責執行「更新付款紀錄」與「通知付款者」兩類產品後處理。
        // Donation processor 仍保留奉獻、課程、信用卡 token 與舊回傳頁流程，避免把 ChurchReport 規則推進共用金流核心。
        private readonly PaymentPostPaymentWorkflow m_PostPaymentWorkflow;

        // 將本類別取得的 CRM Entity、付款結果、成功/失敗狀態轉成 PaymentPostPaymentWorkflow 使用的 context。
        // 它是「ChurchReport 產品資料」和「通用 workflow contract」之間的轉接器。
        private readonly ChurchReportPaymentContextBuilder m_PaymentContextBuilder;

        // 負責把付款結果轉成顯示用模型或結果頁邏輯；目前本類別仍保留舊 ViewBag 回傳流程。
        private readonly DonationPaymentReturnPresenter m_ReturnPresenter;

        // 發生未預期例外時通知維護者的 LINE ID。
        // 注意：這是 ChurchReport 的營運監控資訊，不屬於共用金流核心設定。
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        #region 建構函數
        /// <summary>
        /// 預設建構函數，使用 Factory 模式獲取 ToolUtilityClass 實例
        ///
        /// 這條路徑主要服務舊程式碼：某些舊流程會直接 <c>new DonationFeePaymentProcessor()</c>，
        /// 沒有透過 ASP.NET Core DI 容器建立物件。為了不破壞舊流程，這裡仍保留 Factory 取得 CRM 工具的方式。
        ///
        /// 重要：這條路徑使用 No-Op 的 <see cref="PaymentPostPaymentWorkflow"/>，
        /// 避免舊流程在沒有完整 DI context 時重複觸發新的 CRM/LINE handler。
        /// </summary>
        public DonationFeePaymentProcessor()
        {
            // 從 appsettings.json 讀取 LINE Channel Access Token。
            // PushUtility 需要這個 token 才能把付款結果推播給奉獻者或課程報名者。
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            // PushUtility：主動推播付款成功/失敗訊息。
            // ReplyUtility：保留給舊 LINE callback/回覆流程使用。
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            // 使用 Factory 模式取得 ToolUtilityClass 單例。
            // "DYNAMICS365-9.0" 是舊 ChurchReport 專案既有的 CRM 連線識別。
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
            m_PostPaymentWorkflow = CreateNoOpPostPaymentWorkflow();
            m_ReturnPresenter = new DonationPaymentReturnPresenter();
        }

        /// <summary>
        /// 建構函數，使用 Dependency Injection 模式
        ///
        /// 這是比預設建構子更容易測試的路徑：外部把 <see cref="IToolUtilityProvider"/> 傳進來，
        /// 本類別不用知道 ToolUtilityClass 要怎麼建立，只要跟 provider 要一個 CRM 工具即可。
        /// 但這個建構子仍沒有注入完整付款後 workflow，所以也會使用 No-Op workflow。
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public DonationFeePaymentProcessor(IToolUtilityProvider toolUtilityProvider)
        {
            if (toolUtilityProvider == null)
                throw new ArgumentNullException(nameof(toolUtilityProvider));

            // ✅ 從 appsettings.json 讀取 LINE Channel Access Token
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
            m_PostPaymentWorkflow = CreateNoOpPostPaymentWorkflow();
            m_ReturnPresenter = new DonationPaymentReturnPresenter();
        }

        /// <summary>
        /// DI 使用的主要建構子。共通付款後流程與頁面 presenter 都透過 DI 注入，
        /// 讓其他 ASP.NET Core 產品未來可以使用同一套 workflow contract，再實作自己的 CRM/通知 handler。
        ///
        /// 這是目前最完整、最推薦的建立方式：
        /// - <paramref name="toolUtilityProvider"/>：提供 ChurchReport CRM 操作工具。
        /// - <paramref name="postPaymentWorkflow"/>：執行抽離後的付款後處理 contract。
        /// - <paramref name="paymentContextBuilder"/>：把 ChurchReport CRM Entity 包成 workflow context。
        /// - <paramref name="returnPresenter"/>：負責回傳結果頁所需資料。
        /// </summary>
        public DonationFeePaymentProcessor(
            IToolUtilityProvider toolUtilityProvider,
            PaymentPostPaymentWorkflow postPaymentWorkflow,
            ChurchReportPaymentContextBuilder paymentContextBuilder,
            DonationPaymentReturnPresenter returnPresenter)
            : this(toolUtilityProvider)
        {
            m_PostPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
            m_PaymentContextBuilder = paymentContextBuilder ?? throw new ArgumentNullException(nameof(paymentContextBuilder));
            m_ReturnPresenter = returnPresenter ?? throw new ArgumentNullException(nameof(returnPresenter));
        }
        #endregion

        #region 釋放記憶體
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 不需要手動 Dispose ToolUtilityClass，由 Factory 統一管理生命週期
                // m_ToolUtilityClass.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~DonationFeePaymentProcessor()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        /// <summary>
        /// 從 appsettings.json 取得 LINE Channel Access Token
        ///
        /// 【選擇順序】
        /// 1. 先讀取 <c>CrmConnection:Organization</c>，依目前 CRM organization 找對應的 LINE token。
        ///    例如 organization 是 Jesus，會讀取 <c>LineMessaging:Jesus:ChannelAccessToken</c>。
        /// 2. 如果 organization 沒設定或找不到 token，就使用 <c>LineMessaging:DefaultOrganization</c>。
        /// 3. 如果仍找不到，回傳空字串並寫 Trace。這樣程式不會在建構階段直接崩潰，
        ///    但實際送 LINE 時仍可能失敗，方便從 log 追查設定問題。
        /// </summary>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                // 嘗試從組織設定讀取
                var organization = m_Configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    var configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    var token = m_Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];

                    if (!string.IsNullOrEmpty(token))
                    {
                        System.Diagnostics.Trace.WriteLine($"[DonationFeePaymentProcessor] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[DonationFeePaymentProcessor] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DonationFeePaymentProcessor] 錯誤: 讀取 LINE Token 設定失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        private static PaymentPostPaymentWorkflow CreateNoOpPostPaymentWorkflow()
        {
            // 舊程式仍可能直接 new DonationFeePaymentProcessor()；這些路徑沒有 DI 容器提供完整的 handler。
            // 如果這裡硬塞真實 PaymentPostPaymentWorkflow，舊流程可能在不完整 context 下重複更新 CRM 或重複發 LINE。
            // 因此預設與簡易 DI 建構子使用「空 workflow」：它不做任何 record updater / notifier。
            // 正式 ASP.NET Core DI 路徑會透過完整建構子注入真正的 PaymentPostPaymentWorkflow。
            return new PaymentPostPaymentWorkflow(
                Array.Empty<IPaymentRecordUpdater>(),
                Array.Empty<IPaymentPayerNotifier>());
        }

        /// <summary>
        /// 將 ChurchReport 舊付款回傳模型轉成抽離後 workflow 使用的標準付款結果。
        ///
        /// <para>
        /// <see cref="DonationPaymentWorkflowResult"/> 是 ChurchReport 舊流程使用的模型，
        /// 欄位包含 OrderNo、AmountMinorUnits、CCToken 等舊版概念。
        /// <see cref="PaymentWorkflowResult"/> 則是新的付款後 workflow contract，
        /// 只保留產品後處理真正需要的中性欄位：付款狀態、訂單號、交易號、金額、幣別、provider 訊息。
        /// </para>
        ///
        /// <para>
        /// 金額轉換要特別注意：<c>AmountMinorUnits</c> 是「分」或 provider minor unit，
        /// 例如 800 代表 NT$ 8.00，因此這裡除以 100 轉成 decimal 元。
        /// </para>
        /// </summary>
        private static PaymentWorkflowResult CreatePaymentWorkflowResult(
            DonationPaymentWorkflowResult paymentResult,
            bool isPaymentSuccess,
            string paymentStatusText)
        {
            if (paymentResult == null) throw new ArgumentNullException(nameof(paymentResult));

            return new PaymentWorkflowResult
            {
                Status = isPaymentSuccess ? PaymentStatus.Succeeded : PaymentStatus.Failed,
                ProductOrderId = paymentResult.OrderNo ?? string.Empty,
                ProviderTransactionId = paymentResult.OrderNo ?? string.Empty,
                Amount = Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100m,
                Currency = "TWD",
                ProviderMessage = paymentStatusText ?? string.Empty
            };
        }

        private void ExecutePostPaymentWorkflowIfAvailable(
            Entity feeEntity,
            PaymentWorkflowResult workflowResult,
            bool isPaymentSuccess)
        {
            if (feeEntity == null || workflowResult == null || m_PaymentContextBuilder == null)
            {
                // 這個防護是為了支援舊建構路徑：
                // 預設建構子和簡易 DI 建構子沒有注入 ChurchReportPaymentContextBuilder，
                // 因此只能走本類別內原本的 legacy CRM/LINE 流程，不能執行新 workflow。
                return;
            }

            // 共通 workflow 僅處理可共用的付款後責任：
            // - IPaymentRecordUpdater：例如把標準化付款結果寫回 CRM。
            // - IPaymentPayerNotifier：例如依產品規則通知付款者。
            //
            // Donation 特有的課程報名狀態、信用卡 token 儲存與 legacy ViewBag 結果頁仍留在本類別，
            // 因為那些規則高度綁定 ChurchReport 的 CRM schema 與既有 UI，不應推進共用金流核心。
            var context = m_PaymentContextBuilder.Build(
                m_ToolUtilityClass,
                feeEntity,
                workflowResult,
                isPaymentSuccess);

            m_PostPaymentWorkflow.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 處理信用卡付款完成後回到 ChurchReport 的產品流程。
        ///
        /// 【輸入資料】
        /// - <paramref name="ShopNo"/>：店號或金流端商店識別，主要用於 debug log。
        /// - <paramref name="PayToken"/>：本次付款 token，會寫入付款紀錄方便追查。
        /// - <paramref name="paymentResult"/>：已由上游 adapter 整理好的付款結果。
        /// - <paramref name="correlationId"/> / <paramref name="requestContext"/>：除錯追蹤用資料。
        ///
        /// 【主要流程】
        /// 1. 依付款結果中的 ProductEntityId 找到 CRM 收費單 new_fee。
        /// 2. 判斷付款成功或失敗，並組成中性的 <see cref="PaymentWorkflowResult"/>。
        /// 3. 查付款人 contact、姓名、LINE ID。
        /// 4. 判斷這筆收費單是奉獻還是課程繳費，決定通知文案與結果頁文字。
        /// 5. 成功且未處理過時：更新收費單付款欄位、付款紀錄、課程報名狀態、信用卡資料，並推播成功 LINE。
        /// 6. 成功但已處理過時：避免 RETURN_URL / BACKEND_URL 重複回傳造成 CRM 重複入帳，只顯示成功結果頁。
        /// 7. 失敗時：將失敗描述寫回收費單、推播失敗 LINE，並顯示失敗結果頁。
        /// </summary>
        public ActionResult HandlePaymentReturn(string ShopNo, String PayToken, DonationPaymentWorkflowResult paymentResult, string correlationId = "", string requestContext = "")
        {
            try
            {
                // paymentResult.ProductEntityId 對應 ChurchReport CRM 的 new_fee 收費單 Id。
                // 後續所有 CRM 更新都以這張收費單為中心：付款狀態、實收金額、描述、付款紀錄都寫回它。
                Entity aFeeEntity = this.m_ToolUtilityClass.RetrieveEntity("new_fee", new Guid(paymentResult.ProductEntityId));

                // 將 provider/adapter 回傳的狀態轉成產品流程使用的 bool 與可讀文字。
                // IsPaymentSuccess 決定走成功或失敗分支；paymentStatusText 會出現在 CRM 描述、LINE 訊息與結果頁。
                bool isPaymentSuccess = DonationPaymentResultHelper.IsPaymentSuccess(paymentResult);
                string paymentStatusText = DonationPaymentResultHelper.GetPaymentStatusText(paymentResult);

                // 把舊模型轉成新 workflow contract，讓抽離後的付款後處理機制可以接手部分共用責任。
                var workflowResult = CreatePaymentWorkflowResult(paymentResult, isPaymentSuccess, paymentStatusText);

                // debug log 開關集中在 DonationPaymentDebugLogger，避免正式環境無條件寫大量付款紀錄。
                bool isPaymentDebugLogEnabled = DonationPaymentDebugLogger.IsEnabled();

                if (aFeeEntity == null)
                {
                    // 找不到收費單時不能更新 CRM，也不能可靠取得 contact/LINE ID。
                    // 這通常代表 paymentResult.ProductEntityId 錯誤、CRM 資料被刪除，或測試環境資料不同步。
                    if (isPaymentDebugLogEnabled)
                    {
                        DonationPaymentDebugLogger.WritePaymentResult(
                            nameof(DonationFeePaymentProcessor),
                            isPaymentSuccess ? "FeeEntityNotFoundSuccess" : "FeeEntityNotFoundFailure",
                            ShopNo,
                            PayToken,
                            paymentResult,
                            isPaymentSuccess,
                            paymentStatusText,
                            "FeeEntity not found by Param1.",
                            correlationId,
                            requestContext);
                    }

                    if (isPaymentSuccess)
                    {
                        // 付款成功但找不到收費單：對使用者仍顯示成功，避免讓已付款者誤以為付款失敗；
                        // 但 ErrorDetails 保留金流狀態文字，方便客服或工程人員追查。
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                    else
                    {
                        // 付款失敗且找不到收費單：顯示一般失敗訊息，因為沒有 CRM 收費單可寫入失敗紀錄。
                        ViewBag.IsSuccess = false;
                        ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                }
                else { }

                // 取得付款人 contact。
                // new_contact_new_fee 是收費單指向 contact 的 lookup 欄位；
                // 後續會用 contact 取得姓名、LINE ID，以及儲存信用卡 token 摘要。
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(aFeeEntity, "new_contact_new_fee"));

                // 取得付款人姓名，用在 CRM 描述、LINE 通知與結果頁。
                String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");

                // 取得付款人的 LINE ID。此流程使用 PushUtility 主動推播，因此沒有 LINE ID 就無法發送通知。
                String UserLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");

                // 將 minor units 轉成使用者可讀的金額字串。
                // AmountMinorUnits 是分，例如 800 -> 8 元；ToString("N0") 會加千分位但不顯示小數。
                var paymentAmount = ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString("N0");
                var paymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");

                // 取得類別文字（區分奉獻或課程繳費）。
                // 這個分類會影響：
                // - LINE 訊息中顯示「類別」或「項目」。
                // - 結尾文案是感謝奉獻，還是課程報名繳費成功。
                // - 結果頁 ViewBag.DedicationCategory 顯示的文字。
                string categoryText = "";
                bool isCoursePayment = false;
                try
                {
                    // 先檢查是否為課程繳費（透過課程 Lookup 判斷）。
                    // 如果 new_disciple_lessons_new_fee 有值，代表這張收費單是課程/門徒課程報名繳費。
                    Guid checkDiscipleId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aFeeEntity, "new_disciple_lessons_new_fee");
                    if (checkDiscipleId != Guid.Empty)
                    {
                        // 是課程繳費時，進一步讀取課程 Entity 的 new_name，讓通知裡顯示真正課程名稱。
                        isCoursePayment = true;
                        Entity discipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", checkDiscipleId);
                        if (discipleLessonsEntity != null)
                        {
                            string courseName = this.m_ToolUtilityClass.GetEntityStringAttribute(discipleLessonsEntity, "new_name");
                            categoryText = !string.IsNullOrEmpty(courseName) ? courseName : "課程繳費";
                        }
                        else
                        {
                            categoryText = "課程繳費";
                        }
                    }
                    else
                    {
                        // 非課程繳費就依 new_category OptionSet 判斷奉獻類別。
                        // 這裡只做 ChurchReport 顯示文字對應，不涉及任何 provider 狀態。
                        int categoryOption = this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_category");
                        categoryText = GetDedicationCategoryText(categoryOption);
                    }
                }
                catch { categoryText = "繳費"; }

                // Description 是本流程最重要的共用描述字串：
                // - 成功時會附加到 CRM new_description。
                // - 失敗時也會附加到 CRM new_description。
                // - LINE 成功/失敗訊息會把它嵌入文案。
                // - 付款結果頁失敗時會把它放進 ViewBag.ErrorDetails。
                //
                // 因此它刻意包含：付款人、類別/項目、金額、付款時間、訂單編號、付款方式、處理狀態。
                String Description =
                    "╔════════════╗" + Environment.NewLine +
                    "║   💳 信用卡交易通知   ║" + Environment.NewLine +
                    "╚════════════╝" + Environment.NewLine +
                    Environment.NewLine +
                    "📌 交易資訊" + Environment.NewLine +
                    "┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                    $"  👤 姓名：{aFullName}" + Environment.NewLine +
                    (isCoursePayment
                        ? $"  📋 項目：{categoryText}"
                        : $"  🏷️  類別：{categoryText}") + Environment.NewLine +
                    $"  💰 金額：NT$ {paymentAmount}" + Environment.NewLine +
                    $"  📅 時間：{paymentTime}" + Environment.NewLine +
                    Environment.NewLine +
                    "📋 訂單資訊" + Environment.NewLine +
                    "┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                    $"  訂單編號：{paymentResult.OrderNo}" + Environment.NewLine +
                    $"  付款方式：💳 信用卡" + Environment.NewLine +
                    Environment.NewLine +
                    "📝 處理狀態" + Environment.NewLine +
                    "┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                    $"  {(isPaymentSuccess ? "✓" : "✕")} {paymentStatusText}" + Environment.NewLine;

                string existingPaymentRecords = this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records") ?? string.Empty;
                int currentPayStatus = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aFeeEntity, "new_pay_status");
                bool hasProcessedOrder = existingPaymentRecords.Contains(paymentResult.OrderNo);

                // 信用卡付款常會有兩種回傳：
                // - RETURN_URL：使用者瀏覽器付款後被導回網站。
                // - BACKEND_URL：金流平台從伺服器背景通知網站。
                //
                // 兩個回傳可能帶同一個訂單編號。如果不做 hasProcessedOrder/currentPayStatus 防重，
                // CRM 可能會被重複加總實收金額、重複寫付款紀錄、重複發 LINE。
                //
                // currentPayStatus == 100000000 代表舊流程中的「尚未繳費」狀態；
                // 只有成功且尚未繳費且訂單未處理過，才允許真正更新收費單。
                string branchName = isPaymentSuccess
                    ? (hasProcessedOrder || currentPayStatus != 100000000 ? "SuccessAlreadyProcessed" : "SuccessProcessing")
                    : "FailureProcessing";

                if (isPaymentDebugLogEnabled)
                {
                    // 這段 debug log 不是一般 application log，而是付款流程專用追蹤。
                    // 它把分支名稱、CRM 狀態、是否重複訂單、contact id、課程/奉獻分類記錄下來，
                    // 日後如果使用者反映「有扣款但 CRM 沒更新」或「LINE 沒收到」，可以從這裡還原流程分支。
                    DonationPaymentDebugLogger.WritePaymentResult(
                        nameof(DonationFeePaymentProcessor),
                        branchName,
                        ShopNo,
                        PayToken,
                        paymentResult,
                        isPaymentSuccess,
                        paymentStatusText,
                        "FeeId=" + aFeeEntity.Id +
                        ";CurrentPayStatus=" + currentPayStatus +
                        ";HasProcessedOrder=" + hasProcessedOrder +
                        ";ContactId=" + aContact.Id +
                        ";IsCoursePayment=" + isCoursePayment +
                        ";CategoryText=" + categoryText,
                        correlationId,
                        requestContext);
                }

                if (isPaymentSuccess)
                {
                    if (hasProcessedOrder != true && currentPayStatus == 100000000)
                    {
                        #region 信用卡會回傳2次，一次是RETURN_URL、一次是BACKEND_URL，為免收費單紀錄信用卡兩次，所以如果這裡已經有信用卡訂單編號，就不再處理了
                        // 收費單付款日期。
                        // 使用本機時間寫入 CRM，代表 ChurchReport 何時完成產品端入帳處理。
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeEntity, "new_pay_date", DateTime.Now.ToLocalTime());

                        // 收費單總共實收金額。
                        // 舊流程不是直接覆蓋金額，而是用原本 new_fee_really_paid 加上這次付款金額。
                        // 這代表同一張收費單可能支援部分付款或多次付款，但也更需要上方的重複回傳防護。
                        Money aTotalPaid = new Money(Convert.ToUInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid").Value + new Money((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).Value));
                        this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid", aTotalPaid);

                        // 將本次付款金額轉成大寫中文金額，寫入 CRM new_big_chinese_number。
                        // 這通常是為了收據、財務列印或人工對帳時顯示正式金額文字。
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_big_chinese_number", MoneyToChinese((Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString()));

                        // 如果收費單付款方式是「未知」，才改成「信用卡」。
                        // 如果前面流程已經設定成其他付款方式，這裡不覆蓋，避免破壞人工或其他流程已設定的付款方式。
                        if (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way") == 100000004)
                        {   // 信用卡
                            this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);// 100000001 = 信用卡
                        }
                        else
                        {
                            // 如果收費單付款方式不是"未知"，則不改變
                        }

                        // 收費單付款狀態改成「信用卡已繳費」。
                        // 這是 ChurchReport CRM 的 OptionSet 值，不是金流 provider 狀態碼。
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeEntity, "new_pay_status", 100000001); // 100000001 = 信用卡已繳費


                        // 收費單說明欄追加成功紀錄。
                        // 不覆蓋舊描述，而是把本次付款結果附加在後面，保留歷史客服備註與對帳資訊。
                        String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果成功!" + Environment.NewLine + Description);

                        // 付款紀錄欄 new_payment_records 是本流程的防重依據之一。
                        // 這裡寫入訂單編號、金額、PayToken；下一次相同訂單回來時，
                        // hasProcessedOrder 會從這個欄位判斷已處理過。
                        String aPaymentRecords =
                                this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records") +
                                DateTime.Now.ToString() +
                                ": ReturnUrl => 信用卡訂單編號= " + paymentResult.OrderNo +
                                "，金額:" + ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString() +
                                "，PayToken = " + PayToken +
                                Environment.NewLine;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_payment_records", aPaymentRecords);

                        if (paymentResult.OrderNo.StartsWith("C"))
                        {
                            // 已付款信用卡訂單編號。
                            // 舊流程用 C 開頭判斷信用卡訂單，並寫入 new_q_paid_card_order_no 供後續查詢。
                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_q_paid_card_order_no", paymentResult.OrderNo);
                        }

                        // 更新收費單，把前面設定的付款日期、實收金額、付款方式、付款狀態、描述、付款紀錄一次寫回 CRM。
                        this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

                        #region// 取得上課紀錄單，更新報名狀態
                        // 如果這張收費單連到上課紀錄單 new_stor_lessons，付款成功後也要把報名狀態改成成功。
                        // 這是 ChurchReport 課程報名流程的產品規則，不能放進共用金流核心。
                        Guid aStorLessonsId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aFeeEntity, "new_stor_lessons_new_fee");
                        if (aStorLessonsId != Guid.Empty)
                        {
                            Entity aStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsId);

                            #region 報名狀態
                            // 100000008 = 報名成功。
                            // 註解中的「好牧人」是既有業務語境，代表有審核流程的教會完成報名。
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aStorLessons, "new_enroll_status", 100000008);
                            #endregion

                            this.m_ToolUtilityClass.UpdateEntity(ref aStorLessons);
                        }
                        #endregion

                        #region// 設定連絡人信用卡資訊
                        // 如果金流結果有回傳 CCToken，代表這張卡可被保存為下次付款使用的 token。
                        // 儲存時不是只看 token，而是用卡號前段、後段與效期做去重，避免同一張卡重複寫入 contact。
                        if (paymentResult.CCToken != "")
                        {
                            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

                            if (IsCreditCardInList(aContact, paymentResult) != true)
                            {
                                // new_visa_info 的舊格式：
                                // CCToken，卡號前段，卡號後段，效期|下一張卡...
                                // 這是 ChurchReport 舊版信用卡清單格式，為了相容既有 UI 暫時保留。
                                VisaInfo =
                                        paymentResult.CCToken + "，" +
                                        paymentResult.LeftCCNo + "，" +
                                        paymentResult.RightCCNo + "，" +
                                        // AuthCode is not part of the reusable payment core result. +
                                        paymentResult.CCExpDate +
                                        "|" + VisaInfo;

                                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "new_visa_info", VisaInfo);

                                this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                            }
                        }
                        #endregion

                        #region LINE 通知付款人

                        // 建立成功 LINE 訊息。
                        // Description 已包含交易資訊，這裡再依課程/奉獻加上不同結尾：
                        // - 課程：提醒報名繳費成功，必要時附課程 LINE 群組邀請。
                        // - 奉獻：感謝奉獻並回覆祝福語。
                        string successMessage =
                            "✨════════════✨" + Environment.NewLine +
                            "🎉 交易成功通知 🎉" + Environment.NewLine +
                            "✨════════════✨" + Environment.NewLine +
                            Environment.NewLine +
                            Description +
                            Environment.NewLine +
                            "┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                            (isCoursePayment
                                ? "📚 感謝您的報名繳費！" + Environment.NewLine + "祝您學習愉快，願神賜福與您！"
                                : "💝 感謝您的奉獻！" + Environment.NewLine + "願神賜福與您！") + Environment.NewLine;

                        // 取得收費單的課程 Lookup 是否有值。
                        // 有值代表這筆付款與課程相關，可能需要附上課程 LINE 群組邀請連結。
                        Guid aDiscipleLessonsId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aFeeEntity, "new_disciple_lessons_new_fee");

                        if (aDiscipleLessonsId == Guid.Empty)
                        {
                            // 一般奉獻或非課程付款：直接推播成功訊息。
                            this.m_PushUtility.SendMessage(UserLineId, successMessage);
                        }
                        else
                        {
                            // 課程付款：讀取課程資料，若有 LINE 群組邀請網址就附加在訊息後面。
                            Entity aDiscipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aDiscipleLessonsId);

                            if (aDiscipleLessonsEntity != null)
                            {
                                // 課程 LINE 群組邀請網址由課程 Entity 維護。
                                // 付款成功後附上網址，可以讓報名者直接加入課程群組。
                                String LineGroupInviteAddress = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessonsEntity, "new_line_group_invite_address");

                                if (!string.IsNullOrEmpty(LineGroupInviteAddress))
                                {
                                    // 只有在 CRM 有設定邀請網址時才附加，避免訊息中出現空白連結。
                                    successMessage += Environment.NewLine +
                                        "🔔 課程通知" + Environment.NewLine +
                                        "┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                                        "📱 請點擊以下連結" + Environment.NewLine +
                                        "   加入課程 LINE 群組：" + Environment.NewLine +
                                        Environment.NewLine +
                                        LineGroupInviteAddress + Environment.NewLine +
                                        "═══════════════════════" + Environment.NewLine;
                                }
                            }

                            this.m_PushUtility.SendMessage(UserLineId, successMessage);
                        }
                        #endregion

                        // 設定 ViewBag 並返回付款結果頁。
                        // 這是舊 MVC View 使用的資料傳遞方式；目前保留以維持既有 QPayCard/PaymentResult.cshtml 相容。
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = isCoursePayment
                            ? "報名繳費成功，會透過LINE另行通知課程資訊，感謝您的支持。"
                            : "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.FullName = aFullName;
                        ViewBag.Amount = ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString();
                        ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.TransactionId = paymentResult.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";

                        // 使用已判斷的類別文字（奉獻類別或課程名稱）
                        ViewBag.DedicationCategory = categoryText;

                        return View("~/Views/QPayCard/PaymentResult.cshtml");

                        #endregion
                    }
                    else
                    {
                        // 付款成功但已經處理過：
                        // 不再更新 CRM、不再發 LINE、不再新增信用卡資料，避免同一筆交易因 RETURN_URL/BACKEND_URL 重複入帳。
                        // 仍回傳成功結果頁，讓使用者看到付款成功。
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = isCoursePayment
                            ? "報名繳費成功，會透過LINE另行通知課程資訊，感謝您的支持。"
                            : "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.FullName = aFullName;
                        ViewBag.Amount = ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString();
                        ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.TransactionId = paymentResult.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";

                        // 使用已判斷的類別文字（奉獻類別或課程名稱）
                        ViewBag.DedicationCategory = categoryText;

                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                }
                else
                {
                    // 付款失敗時不更新付款狀態為已繳費，也不增加實收金額。
                    // 只把失敗結果寫進描述欄，保留客服與工程查核資訊。
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果失敗!" + Environment.NewLine + Description);

                    // 更新收費單描述，讓 CRM 端能看到這次失敗嘗試。
                    this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

                    // LINE 通知付款人 - 失敗訊息美化版。
                    // 這裡直接提醒使用者檢查信用卡資訊、額度或稍後再試。
                    string failureMessage =
                        "⚠️═══════════════════⚠️" + Environment.NewLine +
                        "❌ 交易失敗通知" + Environment.NewLine +
                        "⚠️═══════════════════⚠️" + Environment.NewLine +
                        Environment.NewLine +
                        Description +
                        Environment.NewLine +
                        "┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                        "🔄 建議處理方式：" + Environment.NewLine +
                        "  • 請檢查信用卡資訊" + Environment.NewLine +
                        "  • 確認信用額度是否足夠" + Environment.NewLine +
                        "  • 稍後重新嘗試付款" + Environment.NewLine +
                        Environment.NewLine +
                        "📞 需要協助？" + Environment.NewLine +
                        "  請聯繫教會辦公室" + Environment.NewLine +
                        "═══════════════════════" + Environment.NewLine;

                    this.m_PushUtility.SendMessage(UserLineId, failureMessage);

                    // 設定 ViewBag 並返回錯誤結果頁。
                    // ErrorDetails 放完整 Description，便於使用者或客服看到訂單、狀態與時間。
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                    ViewBag.FullName = aFullName;
                    ViewBag.OrderId = paymentResult.OrderNo;
                    ViewBag.ErrorDetails = Description;

                    return View("~/Views/QPayCard/PaymentResult.cshtml");
                }
            }
            catch (System.Exception e)
            {
                // 這是最後防線：只要上面任何 CRM、LINE、格式轉換、ViewBag 流程發生未預期例外，就會進來。
                // 這裡不把 exception 直接丟出去，是因為金流 callback/return 通常需要回應頁面或固定內容；
                // 若直接讓 ASP.NET 拋 500，使用者會看到不友善錯誤，金流端也可能重試或誤判。
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                System.Diagnostics.Trace.WriteLine(ErrorString);
                System.Diagnostics.Trace.WriteLine($"StackTrace: {e.StackTrace}");

                // 發送錯誤通知給維護者，但通知失敗不應再造成第二個例外。
                try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString); } catch { }

                // 返回錯誤內容而不是拋出例外。
                // StatusCode 保持 200 是舊流程行為，可能是為了避免金流端把回傳視為完全失敗後重送。
                return new ContentResult
                {
                    Content = $"<html><body>" +
                             $"<h1>處理付款時發生錯誤</h1>" +
                             $"<p>系統處理時發生錯誤，請稍後再試或聯繫客服</p>" +
                             $"<p>ShopNo: {ShopNo}</p>" +
                             $"<p>PayToken: {PayToken}</p>" +
                             $"<p>錯誤訊息: {e.Message}</p>" +
                             $"<p>時間: {DateTime.Now}</p>" +
                             $"</body></html>",
                    ContentType = "text/html",
                    StatusCode = 200
                };
            }
        }

        #region 工具區
        /// <summary>
        /// 實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
        ///
        /// 【用途】
        /// 成功付款後，程式會把本次付款金額寫入 CRM 的 <c>new_big_chinese_number</c>。
        /// 這個欄位通常用在收據、財務報表或人工對帳畫面，讓金額以「壹佰貳拾參圓整」這類正式中文金額顯示。
        ///
        /// 【演算法概念】
        /// 1. 先處理負數：如果第一個字是 "-"，先記住負號，再把金額轉正數處理。
        /// 2. 將金額四捨五入到小數第二位，並確保一定有兩位小數。
        /// 3. 從字串最右邊開始逐字處理，因為最右邊是「分」，往左是「角」、「圓」、「拾」、「佰」...
        /// 4. 每個數字先轉成大寫中文數字，再依位置補上單位。
        /// 5. 最後用 Replace 清理多餘的「零拾」「零佰」「零角零分」等不自然組合。
        ///
        /// 【注意】
        /// 這段是舊程式保留下來的格式化工具，本次只補註解不改邏輯。
        /// 若未來要重構，應先補齊金額格式化測試，尤其是 0、負數、小數、萬/億以上金額。
        /// </summary>
        /// <param name="LowerMoney">阿拉伯數字金額字串，例如 "8"、"123.45"、"-1000"。</param>
        /// <returns>大寫中文金額字串。</returns>
        public string MoneyToChinese(string LowerMoney)

        {

            string functionReturnValue = null;

            bool IsNegative = false; // 是否是負數

            if (LowerMoney.Trim().Substring(0, 1) == "-")

            {

                // 是負數則先轉為正數。
                // 最後 return 前再把「負」補回去，這樣中間的中文金額轉換邏輯不用處理負號。

                LowerMoney = LowerMoney.Trim().Remove(0, 1);

                IsNegative = true;

            }

            string strLower = null;

            string strUpart = null;

            string strUpper = null;

            int iTemp = 0;

            // 保留兩位小數。
            // 例：123.489 -> 123.49；123.4 後面會補成 123.40。

            LowerMoney = Math.Round(double.Parse(LowerMoney), 2).ToString();

            if (LowerMoney.IndexOf(".") > 0)

            {

                if (LowerMoney.IndexOf(".") == LowerMoney.Length - 2)

                {

                    LowerMoney = LowerMoney + "0";

                }

            }

            else

            {

                LowerMoney = LowerMoney + ".00";

            }

            strLower = LowerMoney;

            iTemp = 1;

            strUpper = "";

            // 從最右邊開始往左掃：
            // iTemp = 1 是「分」，iTemp = 2 是「角」，iTemp = 3 是小數點，
            // iTemp = 4 是「圓」前的個位數，後面依序是拾、佰、仟、萬、億。
            while (iTemp <= strLower.Length)

            {

                // 第一個 switch：把阿拉伯數字轉成大寫中文數字。
                // 小數點 "." 在這裡被當成「圓」的位置標記。
                switch (strLower.Substring(strLower.Length - iTemp, 1))

                {

                    case ".":

                        strUpart = "圓";

                        break;

                    case "0":

                        strUpart = "零";

                        break;

                    case "1":

                        strUpart = "壹";

                        break;

                    case "2":

                        strUpart = "貳";

                        break;

                    case "3":

                        strUpart = "叄";

                        break;

                    case "4":

                        strUpart = "肆";

                        break;

                    case "5":

                        strUpart = "伍";

                        break;

                    case "6":

                        strUpart = "陸";

                        break;

                    case "7":

                        strUpart = "柒";

                        break;

                    case "8":

                        strUpart = "捌";

                        break;

                    case "9":

                        strUpart = "玖";

                        break;

                }

                // 第二個 switch：依目前位置補上單位。
                // 因為前面是從右往左掃，所以 iTemp 可以直接代表位數。
                switch (iTemp)

                {

                    case 1:

                        strUpart = strUpart + "分";

                        break;

                    case 2:

                        strUpart = strUpart + "角";

                        break;

                    case 3:

                        strUpart = strUpart + "";

                        break;

                    case 4:

                        strUpart = strUpart + "";

                        break;

                    case 5:

                        strUpart = strUpart + "拾";

                        break;

                    case 6:

                        strUpart = strUpart + "佰";

                        break;

                    case 7:

                        strUpart = strUpart + "仟";

                        break;

                    case 8:

                        strUpart = strUpart + "萬";

                        break;

                    case 9:

                        strUpart = strUpart + "拾";

                        break;

                    case 10:

                        strUpart = strUpart + "佰";

                        break;

                    case 11:

                        strUpart = strUpart + "仟";

                        break;

                    case 12:

                        strUpart = strUpart + "億";

                        break;

                    case 13:

                        strUpart = strUpart + "拾";

                        break;

                    case 14:

                        strUpart = strUpart + "佰";

                        break;

                    case 15:

                        strUpart = strUpart + "仟";

                        break;

                    case 16:

                        strUpart = strUpart + "萬";

                        break;

                    default:

                        strUpart = strUpart + "";

                        break;

                }

                strUpper = strUpart + strUpper;

                iTemp = iTemp + 1;

            }

            // 清理中文金額中多餘的零。
            // 例如「零拾」「零佰」不符合正式中文金額習慣，要縮成「零」。
            strUpper = strUpper.Replace("零拾", "零");

            strUpper = strUpper.Replace("零佰", "零");

            strUpper = strUpper.Replace("零仟", "零");

            strUpper = strUpper.Replace("零零零", "零");

            strUpper = strUpper.Replace("零零", "零");

            strUpper = strUpper.Replace("零角零分", "整");

            strUpper = strUpper.Replace("零分", "整");

            strUpper = strUpper.Replace("零角", "零");

            strUpper = strUpper.Replace("零億零萬零圓", "億圓");

            strUpper = strUpper.Replace("億零萬零圓", "億圓");

            strUpper = strUpper.Replace("零億零萬", "億");

            strUpper = strUpper.Replace("零萬零圓", "萬圓");

            strUpper = strUpper.Replace("零億", "億");

            strUpper = strUpper.Replace("零萬", "萬");

            strUpper = strUpper.Replace("零圓", "圓");

            strUpper = strUpper.Replace("零零", "零");

            // 對壹圓以下金額的處理。
            // 例如 0.50 不應顯示成「圓伍角」，要移除開頭的「圓」或「零」。

            if (strUpper.Substring(0, 1) == "圓")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "零")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "角")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "分")

            {

                strUpper = strUpper.Substring(1, strUpper.Length - 1);

            }

            if (strUpper.Substring(0, 1) == "整")

            {

                strUpper = "零圓整";

            }

            functionReturnValue = strUpper;

            if (IsNegative == true)

            {

                // 前面若偵測到負數，最後才補回「負」。
                return "負" + functionReturnValue;

            }

            else

            {

                return functionReturnValue;

            }

        }

        /// <summary>
        /// 檢查目前付款回傳的信用卡是否已存在於 contact 的舊版信用卡清單。
        ///
        /// 【資料格式】
        /// CRM contact 欄位 <c>new_visa_info</c> 使用舊格式儲存多張卡：
        /// <c>CCToken，LeftCCNo，RightCCNo，CCExpDate|CCToken，LeftCCNo，RightCCNo，CCExpDate</c>
        ///
        /// 【判斷方式】
        /// 不直接比對 token，而是比對：
        /// - 卡號前段 <c>LeftCCNo</c>
        /// - 卡號後段 <c>RightCCNo</c>
        /// - 有效期限 <c>CCExpDate</c>
        ///
        /// 如果三者都相同，就視為同一張卡，避免同一張卡重複寫入 contact。
        /// </summary>
        public bool IsCreditCardInList(Entity aContact, DonationPaymentWorkflowResult paymentResult)
        {
            #region// 取得連絡人信用卡資訊

            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

            if (VisaInfo != "")
            {
                // 有儲存的信用卡。
                // 第一層用 "|" 切開每一張卡。
                String[] VisaInfoSplit = VisaInfo.Split('|');

                if (VisaInfoSplit.Length > 0)
                {
                    // 逐張檢查是否重複。
                    foreach (String CreditCard in VisaInfoSplit)
                    {
                        // 第二層用全形逗號 "，" 切開欄位：
                        // [0] CCToken、[1] 卡號前段、[2] 卡號後段、[3] 效期。
                        String[] VisaCCTokenSplit = CreditCard.Split('，');

                        if (VisaCCTokenSplit.Length == 4)
                        {
                            // 只要卡號前段、後段、效期一致，就判斷為已存在。
                            // 這樣即使 token 被金流端更新，也能避免畫面上出現重複卡片。
                            if
                            (
                                VisaCCTokenSplit[1] == paymentResult.LeftCCNo &&
                                VisaCCTokenSplit[2] == paymentResult.RightCCNo &&
                                VisaCCTokenSplit[3] == paymentResult.CCExpDate
                            )
                            {
                                // 有一樣的信用卡。
                                return true;
                            }
                        }
                    }
                }
            }
            else
            {
                // contact 還沒有任何儲存信用卡。
                return false;
            }

            // 每個儲存的信用卡與目前要儲存的信用卡都不一樣。
            return false;

            #endregion
        }

        /// <summary>
        /// 取得奉獻類別文字
        ///
        /// CRM 的 <c>new_category</c> 是 OptionSet 整數，不能直接顯示給使用者。
        /// 此方法把 ChurchReport 目前使用的奉獻類別代碼轉成 LINE 訊息與結果頁可讀文字。
        /// 若遇到未知代碼，回傳「奉獻」作為保守預設。
        /// </summary>
        private string GetDedicationCategoryText(int categoryOption)
        {
            switch (categoryOption)
            {
                case 100000000: return "什一奉獻";
                case 100000001: return "感恩奉獻";
                case 100000002: return "宣教奉獻";
                case 100000003: return "建堂奉獻";
                case 100000004: return "愛心奉獻";
                case 100000005: return "其他奉獻";
                default: return "奉獻";
            }
        }

        #endregion
    }
}
