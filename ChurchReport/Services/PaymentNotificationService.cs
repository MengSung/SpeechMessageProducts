using System;
using System.Collections.Generic;
using System.IO;
using ChurchReport.Payments;
using ChurchReport.Tools;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using static ChurchReport.Services.PaymentFeeTypeHelper;

namespace ChurchReport.Services
{
    /// <summary>
    /// 金流 callback 後的 ChurchReport LINE 通知服務。
    /// 此服務消費共用金流層的 <see cref="PaymentWorkflowResult"/>，但訊息內容、
    /// 收費單欄位與 LINE 推播策略都屬於 ChurchReport 產品流程，因此不放進共用金流專案。
    /// </summary>
    public class PaymentNotificationService
    {
        private readonly ILogger<PaymentNotificationService> _logger;
        private readonly PaymentMessageBuilder _messageBuilder;
        private readonly PaymentFeeTypeHelper _feeTypeHelper;
        private readonly ILineNotificationWorkflow _lineNotificationWorkflow;

        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });

        private static IConfiguration Configuration => s_lazyConfiguration.Value;

        public PaymentNotificationService(
            ILogger<PaymentNotificationService> logger,
            PaymentMessageBuilder messageBuilder,
            PaymentFeeTypeHelper feeTypeHelper)
            : this(logger, messageBuilder, feeTypeHelper, CreateDefaultLineNotificationWorkflow())
        {
        }

        public PaymentNotificationService(
            ILogger<PaymentNotificationService> logger,
            PaymentMessageBuilder messageBuilder,
            PaymentFeeTypeHelper feeTypeHelper,
            ILineNotificationWorkflow lineNotificationWorkflow)
        {
            _logger = logger;
            _messageBuilder = messageBuilder;
            _feeTypeHelper = feeTypeHelper;
            _lineNotificationWorkflow = lineNotificationWorkflow ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
        }

        /// <summary>
        /// 建立付款 LINE 通知專用的 deterministic retry key。
        /// 同一筆付款事件重送時會得到同一個 key，LINE 端即可辨識重試請求，
        /// 但 key 內不放付款者姓名、LINE ID、卡號 token 或完整訊息，避免把個資/敏感資料寫進協定 header。
        /// </summary>
        public static string? BuildPaymentLineRetryKey(string? orderId, string? productOrderId, string status)
        {
            var stableId = !string.IsNullOrWhiteSpace(orderId)
                ? orderId.Trim()
                : !string.IsNullOrWhiteSpace(productOrderId)
                    ? productOrderId.Trim()
                    : null;

            if (stableId == null)
            {
                return null;
            }

            var normalizedStatus = string.IsNullOrWhiteSpace(status)
                ? "unknown"
                : status.Trim().ToLowerInvariant();

            return $"churchreport:payment:{stableId}:{normalizedStatus}:payer-line-notice";
        }

        /// <summary>
        /// 透過 LINE Messaging API 推播付款通知。
        /// 此方法仍位於 ChurchReport，是因為 LINE channel token 的選擇、
        /// 使用者 LINE ID 欄位與通知失敗策略都屬於產品流程，不屬於共用金流核心。
        /// </summary>
        public void SendLineMessage(string lineId, string message)
        {
            SendLineMessage(lineId, message, retryKey: null);
        }

        /// <summary>
        /// 透過 LINE Messaging API 推播付款通知。
        /// retryKey 有值時走 LineMessagingProcessor 的可重試入口；沒有 retryKey 時保留舊的 PushUtility 路徑，
        /// 讓既有非付款或無穩定識別碼的通知不被本次重構影響。
        /// </summary>
        public void SendLineMessage(string lineId, string message, string? retryKey)
        {
            try
            {
                var request = new LineNotificationRequest
                {
                    Recipient = LineNotificationRecipient.User(lineId),
                    Content = LineNotificationContent.TextMessage(message),
                    RetryKey = retryKey,
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "ChurchReport.PaymentNotificationService"
                    }
                };

                _lineNotificationWorkflow.SendOrThrowAsync(request).GetAwaiter().GetResult();

                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
                throw;
            }
        }

        /// <summary>
        /// 依收費單類型建立付款成功通知並推播給付款者。
        /// 共用金流核心只提供 <see cref="PaymentWorkflowResult"/>；這裡才根據 ChurchReport CRM
        /// 欄位判斷奉獻、課程或一般繳費，並組出符合教會使用情境的 LINE 文案。
        /// </summary>
        public void SendLineNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            PaymentWorkflowResult result,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) return;

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                decimal amount = result.Amount ?? 0m;
                DateTime paymentTime = DateTime.Now;
                string message;

                if (feeType == FeeType.Dedication)
                {
                    string dedicationCategory = _feeTypeHelper.GetDedicationCategoryName(feeEntity);
                    message = _messageBuilder.BuildDedicationSuccessMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        dedicationCategory,
                        paymentTime);
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = _feeTypeHelper.GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;
                    message = _messageBuilder.BuildCoursePaymentSuccessMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        courseName,
                        courseSchedule,
                        courseLocation,
                        paymentTime);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = _messageBuilder.BuildGeneralPaymentSuccessMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        itemName,
                        paymentTime);
                }

                var retryKey = BuildPaymentLineRetryKey(
                    orderId: result?.ProductOrderId,
                    productOrderId: result?.ProductOrderId,
                    status: "paid");
                SendLineMessage(lineId, message, retryKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[付款回傳] 發送LINE通知失敗 - OrderId: {result?.ProductOrderId}");
                throw;
            }
        }

        /// <summary>
        /// 依收費單類型建立付款失敗通知並推播給付款者。
        /// 失敗通知優先使用收費單應付金額，因為 provider 失敗結果不一定會帶回金額；
        /// 若 CRM 尚未提供應付金額，才退回使用標準化金流結果中的金額。
        /// </summary>
        public void SendLineFailureNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            PaymentWorkflowResult result,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) return;

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                decimal amount = 0m;
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0)
                {
                    amount = shouldPayMoney.Value;
                }
                else if (result.Amount.HasValue)
                {
                    amount = result.Amount.Value;
                }

                DateTime paymentTime = DateTime.Now;
                string statusMessage = string.IsNullOrWhiteSpace(result.ProviderMessage)
                    ? result.Status.ToString()
                    : result.ProviderMessage;
                string message;

                if (feeType == FeeType.Dedication)
                {
                    string dedicationCategory = _feeTypeHelper.GetDedicationCategoryName(feeEntity);
                    message = _messageBuilder.BuildDedicationFailureMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        dedicationCategory,
                        paymentTime,
                        statusMessage);
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = _feeTypeHelper.GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;
                    message = _messageBuilder.BuildCoursePaymentFailureMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        courseName,
                        courseSchedule,
                        courseLocation,
                        paymentTime,
                        statusMessage);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = _messageBuilder.BuildGeneralPaymentFailureMessage(
                        fullName,
                        result.ProductOrderId,
                        result.ProviderTransactionId,
                        amount,
                        itemName,
                        paymentTime,
                        statusMessage);
                }

                var retryKey = BuildPaymentLineRetryKey(
                    orderId: result?.ProductOrderId,
                    productOrderId: result?.ProductOrderId,
                    status: "failed");
                SendLineMessage(lineId, message, retryKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[付款回傳] 發送LINE失敗通知失敗 - OrderId: {result?.ProductOrderId}");
                throw;
            }
        }

        /// <summary>
        /// 依目前 CRM organization 選擇 LINE channel access token。
        /// 多產品或多教會環境共用同一份程式時，這個選擇邏輯仍是 ChurchReport 組態規則；
        /// 因此不放入 provider-neutral 的金流核心。
        /// </summary>
        private static string GetLineChannelAccessToken()
        {
            try
            {
                var organization = Configuration["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    var configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    var token = Configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }

                var defaultOrg = Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                return Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ILineNotificationWorkflow CreateDefaultLineNotificationWorkflow()
        {
            var channelAccessToken = GetLineChannelAccessToken();
            return new LineNotificationWorkflow(new LineMessagingProcessorClass(channelAccessToken));
        }
    }
}
