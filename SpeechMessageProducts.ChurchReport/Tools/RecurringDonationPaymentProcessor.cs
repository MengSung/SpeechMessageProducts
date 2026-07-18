// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class RecurringDonationPaymentProcessor
// 主要成員：Dispose、GetLineChannelAccessToken、HandlePaymentReturn、CreateFee、SetFeeParameter、MoneyToChinese、IsCreditCardInList、ProcessStageNumber、TransferToDeductTotalNum、GetDedicationCategoryText
// 引用命名空間：ChurchReport.WebServiceConnector、Line.Messaging、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Configuration、Microsoft.Xrm.Sdk、ChurchReport.Payments、System
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.WebServiceConnector;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ChurchReport.Payments;
using System;
using System.Collections.Generic;
using System.IO;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;


namespace ChurchReport.Tools
{
    /// <summary>
    /// ChurchReport 定期定額奉獻付款完成後的產品流程處理器。
    /// 此類別負責認獻單期數、CRM 收費單建立、信用卡資訊、LINE 通知與付款結果頁面，
    /// 因此它留在 ChurchReport 產品層，不放入可重用的金流核心。
    /// </summary>
    public class RecurringDonationPaymentProcessor : Controller, IDisposable
    {
        #region 資料區

        #region 設定與配置
        // 由應用程式主機提供有效設定，避免此舊流程重建 base-only provider。
        private static IConfiguration m_Configuration => RuntimeConfiguration.Current;
        #endregion

        private LineMessagingClient m_LineMessagingClient { get; }

        private PushUtility m_PushUtility { get; }

        private ReplyUtility m_ReplyUtility { get; }

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        ToolUtilityClass m_ToolUtilityClass;

        // 胡夢嵩回傳　EXCEPTION　專用的ＩＤ
        private const String MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";
        #endregion
        #region 初始化
        public RecurringDonationPaymentProcessor()
            : this(null, null)
        {
        }

        public RecurringDonationPaymentProcessor(ILineNotificationWorkflow? lineNotificationWorkflow)
            : this(lineNotificationWorkflow, null)
        {
        }

        public RecurringDonationPaymentProcessor(
            ILineNotificationWorkflow? lineNotificationWorkflow,
            ILineReplyWorkflow? lineReplyWorkflow)
        {
            // ✅ 從 appsettings.json 讀取 LINE Channel Access Token
            var channelAccessToken = GetLineChannelAccessToken();
            this.m_LineMessagingClient = new LineMessagingClient(channelAccessToken);

            // Recurring donation flow stays in ChurchReport; LINE push/reply delivery
            // is delegated to workflow-backed helper classes.
            m_PushUtility = new PushUtility(m_LineMessagingClient, lineNotificationWorkflow);
            m_ReplyUtility = new ReplyUtility(
                m_LineMessagingClient,
                new LineMessagingProcessor.LineMessagingProcessorClass(m_LineMessagingClient),
                lineReplyWorkflow);

            // 透過 Factory 取得 ToolUtilityClass 單一實例
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        }

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

        ~RecurringDonationPaymentProcessor()
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
                        System.Diagnostics.Trace.WriteLine($"[RecurringDonationPaymentProcessor] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[RecurringDonationPaymentProcessor] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[RecurringDonationPaymentProcessor] 錯誤: 讀取 LINE Token 設定失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        #endregion
        #region 主程式
        public ActionResult HandlePaymentReturn(string ShopNo, String PayToken, DonationPaymentWorkflowResult paymentResult, string correlationId = "", string requestContext = "")
        {
            try
            {
                bool isPaymentSuccess = DonationPaymentResultHelper.IsPaymentSuccess(paymentResult);
                string paymentStatusText = DonationPaymentResultHelper.GetPaymentStatusText(paymentResult);
                bool isPaymentDebugLogEnabled = DonationPaymentDebugLogger.IsEnabled();

                #region 處理認獻單
                // 取得認獻
                Entity aDedicationBookingEntity = this.m_ToolUtilityClass.RetrieveEntity("new_dedication_booking", new Guid(paymentResult.ProductEntityId));

                #region 沒找到認獻的例外處理
                if (aDedicationBookingEntity == null)
                {
                    if (isPaymentDebugLogEnabled)
                    {
                        DonationPaymentDebugLogger.WritePaymentResult(
                            nameof(RecurringDonationPaymentProcessor),
                            isPaymentSuccess ? "DedicationBookingNotFoundSuccess" : "DedicationBookingNotFoundFailure",
                            ShopNo,
                            PayToken,
                            paymentResult,
                            isPaymentSuccess,
                            paymentStatusText,
                            "Dedication booking not found by Param1.",
                            correlationId,
                            requestContext);
                    }

                    if (isPaymentSuccess)
                    {
                        ViewBag.IsSuccess = true;
                        ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.PaymentMethod = "信用卡定期定額";
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/PaymentReturn/PaymentResult.cshtml");
                    }
                    else
                    {
                        ViewBag.IsSuccess = false;
                        ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                        ViewBag.OrderId = paymentResult.OrderNo;
                        ViewBag.ErrorDetails = paymentStatusText;
                        return View("~/Views/PaymentReturn/PaymentResult.cshtml");
                    }
                }
                else { }
                #endregion
                #region 是否已經有關連到此認獻的第001期收費單
                String aDedicationBookingName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_name");
                //取得認獻單目前的期數
                String aPaidPeriod = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_paid_period");
                bool hasFirstPeriodFee = this.m_ToolUtilityClass.RetrieveFeeByFetchXml(aDedicationBookingName, aDedicationBookingEntity.Id.ToString(), "001").Entities.Count > 0;

                if (hasFirstPeriodFee || aPaidPeriod == "001")
                {
                    if (isPaymentDebugLogEnabled)
                    {
                        DonationPaymentDebugLogger.WritePaymentResult(
                            nameof(RecurringDonationPaymentProcessor),
                            isPaymentSuccess ? "FirstPeriodAlreadyExistsSuccess" : "FirstPeriodAlreadyExistsFailure",
                            ShopNo,
                            PayToken,
                            paymentResult,
                            isPaymentSuccess,
                            paymentStatusText,
                            "DedicationBookingId=" + aDedicationBookingEntity.Id +
                            ";ContactId=" + this.m_ToolUtilityClass.GetEntityLookupAttribute(aDedicationBookingEntity, "new_contact_new_dedication_booking") +
                            ";HasFirstPeriodFee=" + hasFirstPeriodFee +
                            ";PaidPeriod=" + aPaidPeriod,
                            correlationId,
                            requestContext);
                    }

                    // 認獻單目前期數已經是 001
                    // 或是:已經有001期的收費單了，就不再往下繼續執行了
                    ViewBag.IsSuccess = isPaymentSuccess;
                    ViewBag.Message = isPaymentSuccess
                        ? "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。"
                        : "付款失敗，請稍後再試或聯繫教會辦公室。";
                    ViewBag.OrderId = paymentResult.OrderNo;
                    ViewBag.PaymentMethod = "信用卡定期定額";
                    ViewBag.ErrorDetails = paymentStatusText;
                    return View("~/Views/PaymentReturn/PaymentResult.cshtml");
                }
                #endregion
                #endregion
                #region 處理付款人
                // 取得付款人
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(aDedicationBookingEntity, "new_contact_new_dedication_booking"));
                // 取得付款人姓名
                String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname");
                // 取得付款人 Line Id
                String UserLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_lineid");
                #region// 設定連絡人信用卡資訊
                if (paymentResult.CCToken != "")
                {
                    String VisaInfo = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "new_visa_info");

                    if (IsCreditCardInList(aContact, paymentResult) != true)
                    {
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
                #endregion
                #region 收費單描述說明
                String Description =
                                     "姓名     : " + aFullName + Environment.NewLine +
                                     "訂單編號 : " + paymentResult.OrderNo + Environment.NewLine +
                                     "日期     : " + DateTime.Now.ToLocalTime().ToString() + Environment.NewLine +
                                     "實收金額 : " + ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString() + Environment.NewLine +
                                     "付款方式 : " + "信用卡定期定額" + Environment.NewLine +
                                     "總期數   : " + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_total_stages") + Environment.NewLine +
                                     "目前期數 : " + ProcessStageNumber(paymentResult.OrderNo) + Environment.NewLine +
                                     "程式呼叫 : " + paymentResult.Description + Environment.NewLine +
                                     "交易結果 : " + paymentStatusText + Environment.NewLine +
                                     //"這是 ChurcchReport Webhook!!" + Environment.NewLine +
                                     "--------------------" + Environment.NewLine;

                #endregion
                // 建立收費單
                Guid createdFeeId = CreateFee(aContact, aDedicationBookingEntity, paymentResult, Description);

                #region 處理認獻單定期定額的第幾期字串
                String StageNumber = ProcessStageNumber(paymentResult.OrderNo);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_paid_period", StageNumber);

                if (TransferToDeductTotalNum(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_total_stages")) > Convert.ToUInt32(StageNumber.Replace("0", "")))
                {
                    // 總期數大於目前期數
                    // 認獻單狀態 = 進行中
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000001);
                }
                else
                {
                    // 總期數小於或等於目前期數
                    // 認獻單狀態 = 已結束
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000002);
                }
                #endregion

                if (isPaymentDebugLogEnabled)
                {
                    DonationPaymentDebugLogger.WritePaymentResult(
                        nameof(RecurringDonationPaymentProcessor),
                        isPaymentSuccess ? "SuccessProcessing" : "FailureProcessing",
                        ShopNo,
                        PayToken,
                        paymentResult,
                        isPaymentSuccess,
                        paymentStatusText,
                        "DedicationBookingId=" + aDedicationBookingEntity.Id +
                        ";ContactId=" + aContact.Id +
                        ";CreatedFeeId=" + createdFeeId +
                        ";StageNumber=" + StageNumber,
                        correlationId,
                        requestContext);
                }

                if (isPaymentSuccess)
                {
                    // 認獻單備註 = 寫入成功的原因
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain", "信用卡定期定額付款結果成功!" + Environment.NewLine + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain") + Environment.NewLine + "--------------------------------------" + Environment.NewLine + Description);

                    // 更新認獻單
                    this.m_ToolUtilityClass.UpdateEntity(ref aDedicationBookingEntity);

                    //if (this.m_ToolUtilityClass.GetEntityStringAttribute(aDedicationBookingEntity, "new_payment_records").Contains(paymentResult.OrderNo) != true && this.m_ToolUtilityClass.GetOptionSetAttribute(ref aFeeEntity, "new_pay_status") == 100000000)
                    if (paymentResult.OrderNo != "")
                    {
                        #region 信用卡會回傳2次，一次是RETURN_URL、一次是BACKEND_URL，為免收費單紀錄信用卡兩次，所以如果這裡已經有信用卡訂單編號，就不再處理了

                        // LINE 通知付款人
                        this.m_PushUtility.SendMessage(UserLineId, "信用卡付款結果成功!" + Environment.NewLine + Description);

                        #endregion
                    }

                    // 設定 ViewBag 並返回美觀的結果頁面
                    ViewBag.IsSuccess = true;
                    ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
                    ViewBag.FullName = aFullName;
                    ViewBag.Amount = ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString();
                    ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                    ViewBag.OrderId = paymentResult.OrderNo;
                    ViewBag.TransactionId = paymentResult.OrderNo;
                    ViewBag.PaymentMethod = "信用卡定期定額 (第" + ProcessStageNumber(paymentResult.OrderNo) + "期)";

                    // 取得奉獻類別
                    int categoryOption = this.m_ToolUtilityClass.GetOptionSetAttribute(aDedicationBookingEntity, "new_dedication_category");
                    string dedicationCategory = GetDedicationCategoryText(categoryOption);
                    ViewBag.DedicationCategory = dedicationCategory;

                    return View("~/Views/PaymentReturn/PaymentResult.cshtml");
                }
                else
                {
                    // 認獻單狀態 = 啟動失敗
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_booking_status", 100000003);

                    // 認獻單備註 = 寫入失敗的原因
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain", "信用卡定期定額付款結果失敗!" + Environment.NewLine + this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDedicationBookingEntity, "new_explain") + Environment.NewLine + "--------------------------------------" + Environment.NewLine + Description);

                    // 更新認獻單
                    this.m_ToolUtilityClass.UpdateEntity(aDedicationBookingEntity);

                    // LINE 通知付款人
                    this.m_PushUtility.SendMessage(UserLineId, "信用卡付款結果失敗!" + Environment.NewLine + Description);

                    // 設定 ViewBag 並返回美觀的錯誤頁面
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "付款失敗，請稍後再試或聯繫教會辦公室。";
                    ViewBag.FullName = aFullName;
                    ViewBag.OrderId = paymentResult.OrderNo;
                    ViewBag.ErrorDetails = Description;
                    ViewBag.PaymentMethod = "信用卡定期定額";

                    return View("~/Views/PaymentReturn/PaymentResult.cshtml");
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
                             $"<h1>處理認獻單付款時發生錯誤</h1>" +
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
        #endregion
        #region 建立收費單
        public Guid CreateFee(Entity aContact, Entity aDedicationBookingEntity, DonationPaymentWorkflowResult paymentResult, String Description)
        {
            try
            {
                #region 建立收費單

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter(aContact, aFeeToCreated, aDedicationBookingEntity, paymentResult, Description);

                // 新增收費單
                Guid aFeeId = this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
                Entity aRetrievedFee = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

                //指派負責人
                if (aRetrievedFee != null && aContact != null)
                {
                    try
                    {
                        this.m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, this.m_ToolUtilityClass.GetOwnerId(aContact));
                    }
                    catch (System.Exception e)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                    }
                }

                return aFeeId;
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, Entity aDedicationBookingEntity, DonationPaymentWorkflowResult paymentResult, String Description)
        {
            try
            {
                #region 建立收費單所需要的參數
                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 收費單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_name", FullName + "奉獻");

                // 收費單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 收費單姓認獻名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_dedication_booking_new_fee", "new_dedication_booking", aDedicationBookingEntity.Id);

                // 收費單定期定額期數
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_paid_period", ProcessStageNumber(paymentResult.OrderNo));

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationBookingEntity, "new_amount_per_stage"));

                // 收費單實現阿拉伯數字到大寫中文的轉換，金額轉為大寫金額
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number", MoneyToChinese(this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDedicationBookingEntity, "new_amount_per_stage").Value.ToString()));

                // 收費單付款方式，預設是信用卡
                this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeToCreated, "new_pay_way", 100000001);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", DateTime.Now.ToLocalTime());

                // 奉獻類別
                this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeToCreated, "new_category", this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDedicationBookingEntity, "new_dedication_category"));

                // 付款紀錄
                String aPaymentRecords =
                        this.m_ToolUtilityClass.GetEntityStringAttribute(aFeeToCreated, "new_payment_records") +
                        DateTime.Now.ToString() +
                        ": ReturnUrl => 信用卡訂單編號= " + paymentResult.OrderNo +
                        "，金額:" + ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString() +
                        Environment.NewLine;

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_payment_records", aPaymentRecords);

                if (paymentResult.OrderNo.StartsWith("C"))
                {
                    // 已付款信用卡訂單編號
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_q_paid_card_order_no", paymentResult.OrderNo);
                }

                if (DonationPaymentResultHelper.IsPaymentSuccess(paymentResult))
                {
                    // 信用卡付款結果成功
                    // 收費單付款狀態，是信用卡已繳費
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_status", 100000001); // 100000001 = 信用卡已繳費
                    // 收費單實收金額
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100));
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeToCreated, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_description", aOriginalDescription + "信用卡付款結果成功!" + Environment.NewLine + Description);
                }
                else
                {
                    // 信用卡付款結果失敗
                    // 收費單付款狀態，是新建立
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_status", 100000000); // 100000000 = 信用卡新建立
                    // 收費單實收金額為0
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));
                    // 收費單說明
                    String aOriginalDescription = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aFeeToCreated, "new_description");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_description", aOriginalDescription + "信用卡付款結果失敗!" + Environment.NewLine + Description);
                }


                // 奉獻地點
                // 奉獻地點就要依據連絡人所屬教會設定
                // 取得連絡人所屬教會
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aContact, "parentcustomerid"));

                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        #endregion
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

        public bool IsCreditCardInList(Entity aContact, DonationPaymentWorkflowResult paymentResult)
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
                                VisaCCTokenSplit[1] == paymentResult.LeftCCNo &&
                                VisaCCTokenSplit[2] == paymentResult.RightCCNo &&
                                VisaCCTokenSplit[3] == paymentResult.CCExpDate
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

        public string ProcessStageNumber(string OrderNo)
        {
            //處理定期定額的第幾期字串
            if (OrderNo.Contains("_") == true)
            {
                //第二期以後開始有底線
                string[] OrderNoArray = OrderNo.Split('_');

                if (OrderNoArray.Length >= 2)
                {
                    return OrderNoArray[1];
                }
                else
                {
                    return "000";
                }
            }
            else
            {
                //沒有底線，所以是第一期
                return "001";
            }

        }
        private int TransferToDeductTotalNum(string DeductTotalNumber)
        {
            switch (DeductTotalNumber)
            {
                case "3個月":
                    return 3;
                case "6個月":
                    return 6;
                case "12個月":
                    return 12;
                case "18個月":
                    return 18;
                case "24個月":
                    return 24;
                default:
                    return 0;
            }
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
