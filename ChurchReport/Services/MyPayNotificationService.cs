using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;
using Line.Messaging;
using ChurchReport.Models;
using ChurchReport.Tools;
using static ChurchReport.Services.MyPayFeeTypeHelper;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay LINE 通知發送服務
    /// 負責根據收費單類型發送對應的 LINE 通知訊息
    /// </summary>
    public class MyPayNotificationService
    {
        private readonly ILogger<MyPayNotificationService> _logger;
        private readonly MyPayMessageBuilder _messageBuilder;
        private readonly MyPayStatusHelper _statusHelper;
        private readonly MyPayFeeTypeHelper _feeTypeHelper;

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

        public MyPayNotificationService(
            ILogger<MyPayNotificationService> logger,
            MyPayMessageBuilder messageBuilder,
            MyPayStatusHelper statusHelper,
            MyPayFeeTypeHelper feeTypeHelper)
        {
            _logger = logger;
            _messageBuilder = messageBuilder;
            _statusHelper = statusHelper;
            _feeTypeHelper = feeTypeHelper;
        }

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
                        System.Diagnostics.Trace.WriteLine($"[MyPayNotificationService] LINE Token loaded for organization: {organization}");
                        return token;
                    }
                }

                // 使用預設組織
                var defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                var defaultToken = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];

                if (string.IsNullOrEmpty(defaultToken))
                {
                    System.Diagnostics.Trace.WriteLine("[MyPayNotificationService] 警告: LINE Channel Access Token 未設定");
                }

                return defaultToken ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[MyPayNotificationService] 錯誤: 讀取 LINE Token 設定失敗 - {ex.Message}");
                return string.Empty;
            }
        }

        #region LINE 訊息發送

        /// <summary>
        /// ========================================
        /// 發送 LINE 訊息
        /// ========================================
        /// </summary>
        public void SendLineMessage(string lineId, string message)
        {
            try
            {
                var channelAccessToken = GetLineChannelAccessToken();
                var lineMessagingClient = new LineMessagingClient(channelAccessToken);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}");
                throw;
            }
        }

        #endregion

        #region 成功通知發送

        /// <summary>
        /// ========================================
        /// 發送 LINE 成功通知（使用 MyPayReturnModel）
        /// ========================================
        /// </summary>
        public void SendLineNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            MyPayReturnModel model,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) return;

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                // 解析付款金額
                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) &&
                    decimal.TryParse(model.actual_cost, out var parsedActual))
                {
                    amount = parsedActual;
                }
                else if (!string.IsNullOrEmpty(model.cost) &&
                         decimal.TryParse(model.cost, out var parsedCost))
                {
                    amount = parsedCost;
                }

                DateTime paymentTime = _statusHelper.ParseFinishTime(model.finishtime);

                string message;

                if (feeType == FeeType.Dedication)
                {
                    string dedicationCategory = _feeTypeHelper.GetDedicationCategoryName(feeEntity);

                    message = _messageBuilder.BuildDedicationSuccessMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        dedicationCategory,
                        paymentTime
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = _feeTypeHelper.GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;

                    message = _messageBuilder.BuildCoursePaymentSuccessMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        courseName,
                        courseSchedule,
                        courseLocation,
                        paymentTime
                    );
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";

                    message = _messageBuilder.BuildGeneralPaymentSuccessMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        itemName,
                        paymentTime
                    );
                }

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

        #endregion

        #region 失敗通知發送

        /// <summary>
        /// ========================================
        /// 發送 LINE 失敗通知（使用 MyPayReturnModel）
        /// ========================================
        /// </summary>
        public void SendLineFailureNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            MyPayReturnModel model,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) return;

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                // 解析應付金額（優先使用 CRM 中的金額）
                decimal amount = 0m;

                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0)
                {
                    amount = shouldPayMoney.Value;
                }
                else if (!string.IsNullOrWhiteSpace(model.actual_cost) &&
                         decimal.TryParse(model.actual_cost, out var parsedActual))
                {
                    amount = parsedActual;
                }
                else if (!string.IsNullOrWhiteSpace(model.cost) &&
                         decimal.TryParse(model.cost, out var parsedCost))
                {
                    amount = parsedCost;
                }

                DateTime paymentTime = _statusHelper.ParseFinishTime(model.finishtime);
                string statusMessage = _statusHelper.GetPaymentStatusMessage(model.prc);

                string message;

                if (feeType == FeeType.Dedication)
                {
                    string dedicationCategory = _feeTypeHelper.GetDedicationCategoryName(feeEntity);

                    message = _messageBuilder.BuildDedicationFailureMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        dedicationCategory,
                        paymentTime,
                        statusMessage
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = _feeTypeHelper.GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;

                    message = _messageBuilder.BuildCoursePaymentFailureMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        courseName,
                        courseSchedule,
                        courseLocation,
                        paymentTime,
                        statusMessage
                    );
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";

                    message = _messageBuilder.BuildGeneralPaymentFailureMessage(
                        fullName,
                        model.order_id,
                        model.uid,
                        amount,
                        itemName,
                        paymentTime,
                        statusMessage
                    );
                }

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE失敗通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

        #endregion

        #region 舊版相容方法

        /// <summary>
        /// ========================================
        /// 發送付款通知（使用個別參數，舊版相容）
        /// ========================================
        /// </summary>
        public void SendPaymentNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            string orderId,
            string transactionId,
            string cost,
            string fullName,
            string itemName,
            FeeType feeType,
            decimal amount,
            Entity contactEntity)
        {
            try
            {
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty) return;

                if (contactEntity == null)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                }

                if (contactEntity == null) return;

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                string message;

                if (feeType == FeeType.Dedication)
                {
                    message = _messageBuilder.BuildDedicationSuccessMessage(
                        fullName,
                        orderId,
                        transactionId,
                        amount,
                        itemName,
                        DateTime.Now
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";

                    message = _messageBuilder.BuildCoursePaymentSuccessMessage(
                        fullName,
                        orderId,
                        transactionId,
                        amount,
                        itemName,
                        courseSchedule,
                        courseLocation,
                        DateTime.Now
                    );
                }
                else
                {
                    message = _messageBuilder.BuildGeneralPaymentSuccessMessage(
                        fullName,
                        orderId,
                        transactionId,
                        amount,
                        itemName,
                        DateTime.Now
                    );
                }

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendNotification: 發送 LINE失敗 - OrderId: {orderId}");
            }
        }

        #endregion
    }
}
