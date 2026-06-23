using ChurchReport.WebServiceConnector;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;


namespace ChurchReport.Tools
{
    public class QPayFeeProcessor : Controller, IDisposable
    {
        #region 設定與配置
        // ✅ 透過 appsettings.json 讀取設定，避免硬編碼
        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });
        private static IConfiguration m_Configuration => s_lazyConfiguration.Value;
        #endregion

        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        private QPayProcessor m_QPayProcessor { get; }

        // 透過建構函數注入取得 ToolUtilityClass
        private readonly ToolUtilityClass m_ToolUtilityClass;

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

        #region 建構函數
        /// <summary>
        /// 預設建構函數，使用 Factory 模式獲取 ToolUtilityClass 實例
        /// </summary>
        public QPayFeeProcessor()
        {
            // ✅ 從 appsettings.json 讀取 LINE Channel Access Token
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            //// 客製化
            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_QPayProcessor = new QPayProcessor(m_LineMessagingClient, m_PushUtility, m_ReplyUtility);

            // 使用 Factory 模式取得 ToolUtilityClass 單例
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        }

        /// <summary>
        /// 建構函數，使用 Dependency Injection 模式
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public QPayFeeProcessor(IToolUtilityProvider toolUtilityProvider)
        {
            if (toolUtilityProvider == null)
                throw new ArgumentNullException(nameof(toolUtilityProvider));

            // ✅ 從 appsettings.json 讀取 LINE Channel Access Token
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            m_PushUtility = new PushUtility(m_LineMessagingClient);
            m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);

            m_QPayProcessor = new QPayProcessor(m_LineMessagingClient, m_PushUtility, m_ReplyUtility);

            m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
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

        ~QPayFeeProcessor()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }
        #endregion

        /// <summary>
        /// 從 appsettings.json 取得 LINE Channel Access Token
        /// ✅ 根據 CRM 組織名稱動態選擇對應的 Token
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
                        System.Diagnostics.Trace.WriteLine($"[QPayFeeProcessor] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[QPayFeeProcessor] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayFeeProcessor] 錯誤: 讀取 LINE Token 設定失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        //[HttpGet]
        //[Route("QPayReturnUrl")]
        //public async Task<IActionResult> QPayReturnUrl(int? id = 0)
        //{
        //    return new OkObjectResult("付款結果可能成功");
        //}

        public ActionResult QPayFeeProcessorReturnUrl(string ShopNo, String PayToken, QryOrderPay aQryOrderPay, string correlationId = "", string requestContext = "", QryOrder orderQueryDebugInfo = null, string orderQueryDebugError = "")
        {
            try
            {
                //m_PushUtility.SendMessage(MENGSUNG_LINE_ID, "QPayReturnUrl_001");

                Entity aFeeEntity = this.m_ToolUtilityClass.RetrieveEntity("new_fee", new Guid(aQryOrderPay.TSResultContent.Param1));
                bool isPaymentSuccess = QPayPaymentResultHelper.IsPaymentSuccess(aQryOrderPay);
                string paymentStatusText = QPayPaymentResultHelper.GetPaymentStatusText(aQryOrderPay);
                bool isPaymentDebugLogEnabled = QPayPaymentDebugLogger.IsEnabled();

                if (aFeeEntity == null)
                {
                    if (isPaymentDebugLogEnabled)
                    {
                        QPayPaymentDebugLogger.WritePaymentResult(
                            nameof(QPayFeeProcessor),
                            isPaymentSuccess ? "FeeEntityNotFoundSuccess" : "FeeEntityNotFoundFailure",
                            ShopNo,
                            PayToken,
                            aQryOrderPay,
                            isPaymentSuccess,
                            paymentStatusText,
                            "FeeEntity not found by Param1.",
                            correlationId,
                            requestContext,
                            orderQueryDebugInfo,
                            orderQueryDebugError);
                    }

                    if (isPaymentSuccess)
                    {
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.OrderId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                    else
                    {
                        ViewBag.IsSuccess = false;
                        ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                        ViewBag.OrderId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                }
                else { }

                // 取得付款人
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(aFeeEntity, "new_contact_new_fee"));
                // 取得付款人姓名
                String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                // 取得付款人 Line Id
                String UserLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");

                // 收費單描述說明 - 精緻美化版本
                var paymentAmount = ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString("N0");
                var paymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                
                // 取得類別文字（區分奉獻或課程繳費）
                string categoryText = "";
                bool isCoursePayment = false;
                try 
                {
                    // 先檢查是否為課程繳費（透過課程 Lookup 判斷）
                    Guid checkDiscipleId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aFeeEntity, "new_disciple_lessons_new_fee");
                    if (checkDiscipleId != Guid.Empty)
                    {
                        // 是課程繳費 - 取得課程名稱
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
                        // 非課程繳費 - 取得奉獻類別
                        int categoryOption = this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_category");
                        categoryText = GetDedicationCategoryText(categoryOption);
                    }
                }
                catch { categoryText = "繳費"; }

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
                    $"  訂單編號：{aQryOrderPay.TSResultContent.OrderNo}" + Environment.NewLine +
                    $"  付款方式：💳 信用卡" + Environment.NewLine +
                    Environment.NewLine +
                    "📝 處理狀態" + Environment.NewLine +
                    "┈┈┈┈┈┈┈┈┈" + Environment.NewLine +
                    $"  {(isPaymentSuccess ? "✓" : "✕")} {paymentStatusText}" + Environment.NewLine;

                string existingPaymentRecords = this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records") ?? string.Empty;
                int currentPayStatus = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aFeeEntity, "new_pay_status");
                bool hasProcessedOrder = existingPaymentRecords.Contains(aQryOrderPay.TSResultContent.OrderNo);
                string branchName = isPaymentSuccess
                    ? (hasProcessedOrder || currentPayStatus != 100000000 ? "SuccessAlreadyProcessed" : "SuccessProcessing")
                    : "FailureProcessing";

                if (isPaymentDebugLogEnabled)
                {
                    QPayPaymentDebugLogger.WritePaymentResult(
                        nameof(QPayFeeProcessor),
                        branchName,
                        ShopNo,
                        PayToken,
                        aQryOrderPay,
                        isPaymentSuccess,
                        paymentStatusText,
                        "FeeId=" + aFeeEntity.Id +
                        ";CurrentPayStatus=" + currentPayStatus +
                        ";HasProcessedOrder=" + hasProcessedOrder +
                        ";ContactId=" + aContact.Id +
                        ";IsCoursePayment=" + isCoursePayment +
                        ";CategoryText=" + categoryText,
                        correlationId,
                        requestContext,
                        orderQueryDebugInfo,
                        orderQueryDebugError);
                }

                if (isPaymentSuccess)
                {
                    if (hasProcessedOrder != true && currentPayStatus == 100000000)
                    {
                        #region 信用卡會回傳2次，一次是RETURN_URL、一次是BACKEND_URL，為免收費單紀錄信用卡兩次，所以如果這裡已經有信用卡訂單編號，就不再處理了
                        // 收費單付款日期
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeEntity, "new_pay_date", DateTime.Now.ToLocalTime());
                        // 收費單總共實收金額
                        Money aTotalPaid = new Money(Convert.ToUInt32(this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid").Value + new Money((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).Value));
                        this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeEntity, "new_fee_really_paid", aTotalPaid);
                        // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_big_chinese_number", MoneyToChinese((Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString()));

                        // 如果收費單付款方式是"未知"，則才預設是信用卡
                        if (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way") == 100000004)
                        {   // 信用卡
                            this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);// 100000001 = 信用卡
                        }
                        else
                        {
                            // 如果收費單付款方式不是"未知"，則不改變
                        }

                        // 收費單付款狀態
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeEntity, "new_pay_status", 100000001); // 100000001 = 信用卡已繳費


                        // 收費單說明
                        String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果成功!" + Environment.NewLine + Description);

                        // 付款紀錄
                        String aPaymentRecords =
                                this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeEntity, "new_payment_records") +
                                DateTime.Now.ToString() +
                                ": ReturnUrl => 信用卡訂單編號= " + aQryOrderPay.TSResultContent.OrderNo +
                                "，金額:" + ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString() +
                                "，PayToken = " + PayToken +
                                Environment.NewLine;

                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_payment_records", aPaymentRecords);

                        if (aQryOrderPay.TSResultContent.OrderNo.StartsWith("C"))
                        {
                            // 已付款信用卡訂單編號
                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_q_paid_card_order_no", aQryOrderPay.TSResultContent.OrderNo);
                        }

                        // 更新收費單
                        this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

                        #region// 取得上課紀錄單，更新報名狀態
                        Guid aStorLessonsId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aFeeEntity, "new_stor_lessons_new_fee");
                        if (aStorLessonsId != Guid.Empty)
                        {
                            Entity aStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsId);

                            #region 報名狀態
                            // 有審核的教會=>報名成功:行道會聖谷教會
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aStorLessons, "new_enroll_status", 100000008);
                            #endregion

                            this.m_ToolUtilityClass.UpdateEntity(ref aStorLessons);
                        }
                        #endregion

                        #region// 設定連絡人信用卡資訊
                        if (aQryOrderPay.TSResultContent.CCToken != "")
                        {
                            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

                            if (IsCreditCardInList(aContact, aQryOrderPay) != true)
                            {
                                VisaInfo =
                                        aQryOrderPay.TSResultContent.CCToken + "，" +
                                        aQryOrderPay.TSResultContent.LeftCCNo + "，" +
                                        aQryOrderPay.TSResultContent.RightCCNo + "，" +
                                        //aQryOrderPay.TSResultContent.AuthCode + "，" +
                                        aQryOrderPay.TSResultContent.CCExpDate +
                                        "|" + VisaInfo;

                                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContact, "new_visa_info", VisaInfo);

                                this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                            }
                        }
                        #endregion

                        #region LINE 通知付款人

                        // 建立成功訊息（依據奉獻或課程繳費顯示不同結尾）
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
                        
                        // 取得收費單的課程Lookup是否有值
                        Guid aDiscipleLessonsId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aFeeEntity, "new_disciple_lessons_new_fee");

                        if (aDiscipleLessonsId == Guid.Empty)
                        {
                            // 收費單的課程Lookup沒有值
                            this.m_PushUtility.SendMessage(UserLineId, successMessage);
                        }
                        else
                        {
                            // 收費單的課程Lookup有值 - 取得該課程
                            Entity aDiscipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aDiscipleLessonsId);

                            if (aDiscipleLessonsEntity != null)
                            {
                                // 取得Line群組邀請網址
                                String LineGroupInviteAddress = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessonsEntity, "new_line_group_invite_address");

                                if (!string.IsNullOrEmpty(LineGroupInviteAddress))
                                {
                                    // 有Line群組邀請網址
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

                        // 設定 ViewBag 並返回美觀的結果頁面
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = isCoursePayment
                            ? "報名繳費成功，會透過LINE另行通知課程資訊，感謝您的支持。"
                            : "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.FullName = aFullName;
                        ViewBag.Amount = ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString();
                        ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                        ViewBag.OrderId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.TransactionId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";

                        // 使用已判斷的類別文字（奉獻類別或課程名稱）
                        ViewBag.DedicationCategory = categoryText;

                        return View("~/Views/QPayCard/PaymentResult.cshtml");

                        #endregion
                    }
                    else
                    {
                        // 設定 ViewBag 並返回美觀的結果頁面
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = isCoursePayment
                            ? "報名繳費成功，會透過LINE另行通知課程資訊，感謝您的支持。"
                            : "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.FullName = aFullName;
                        ViewBag.Amount = ((int)Convert.ToUInt32(aQryOrderPay.TSResultContent.Amount) / 100).ToString();
                        ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                        ViewBag.OrderId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.TransactionId = aQryOrderPay.TSResultContent.OrderNo;
                        ViewBag.PaymentMethod = "信用卡";

                        // 使用已判斷的類別文字（奉獻類別或課程名稱）
                        ViewBag.DedicationCategory = categoryText;

                        return View("~/Views/QPayCard/PaymentResult.cshtml");
                    }
                }
                else
                {
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeEntity, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeEntity, "new_description", aOriginalDescription + "信用卡付款結果失敗!" + Environment.NewLine + Description);

                    // 更新收費單
                    this.m_ToolUtilityClass.UpdateEntity(ref aFeeEntity);

                    // LINE 通知付款人 - 失敗訊息美化版
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

                    // 設定 ViewBag 並返回美觀的錯誤頁面
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                    ViewBag.FullName = aFullName;
                    ViewBag.OrderId = aQryOrderPay.TSResultContent.OrderNo;
                    ViewBag.ErrorDetails = Description;
                    
                    return View("~/Views/QPayCard/PaymentResult.cshtml");
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                System.Diagnostics.Trace.WriteLine(ErrorString);
                System.Diagnostics.Trace.WriteLine($"StackTrace: {e.StackTrace}");
                
                // 發送錯誤通知但不中斷執行
                try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString); } catch { }
                
                // 返回錯誤內容而不是拋出例外
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
        /// </summary>
        /// <param name="LowerMoney"></param>
        /// <returns></returns>
        public string MoneyToChinese(string LowerMoney)

        {

            string functionReturnValue = null;

            bool IsNegative = false; // 是否是負數

            if (LowerMoney.Trim().Substring(0, 1) == "-")

            {

                // 是負數則先轉為正數

                LowerMoney = LowerMoney.Trim().Remove(0, 1);

                IsNegative = true;

            }

            string strLower = null;

            string strUpart = null;

            string strUpper = null;

            int iTemp = 0;

            // 保留兩位小數 123.489→123.49　　123.4→123.4

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

            while (iTemp <= strLower.Length)

            {

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

            // 對壹圓以下的金額的處理

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

                return "負" + functionReturnValue;

            }

            else

            {

                return functionReturnValue;

            }

        }

        public bool IsCreditCardInList(Entity aContact, QryOrderPay aQryOrderPay)
        {
            #region// 取得連絡人信用卡資訊

            String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

            if (VisaInfo != "")
            {
                // 有儲存的信用卡
                String[] VisaInfoSplit = VisaInfo.Split('|');

                if (VisaInfoSplit.Length > 0)
                {
                    // 檢驗一個一個的信用卡是否重覆
                    foreach (String CreditCard in VisaInfoSplit)
                    {
                        String[] VisaCCTokenSplit = CreditCard.Split('，');

                        if (VisaCCTokenSplit.Length == 4)
                        {
                            if
                            (
                                VisaCCTokenSplit[1] == aQryOrderPay.TSResultContent.LeftCCNo &&
                                VisaCCTokenSplit[2] == aQryOrderPay.TSResultContent.RightCCNo &&
                                VisaCCTokenSplit[3] == aQryOrderPay.TSResultContent.CCExpDate
                            )
                            {
                                // 有一樣的信用卡
                                return true;
                            }
                        }
                    }
                }
            }
            else
            {
                // 還沒有儲存的信用卡
                return false;
            }

            // 每個儲存的信用卡與目前要儲存的信用卡都不一樣
            return false;

            #endregion
        }

        /// <summary>
        /// 取得奉獻類別文字
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
