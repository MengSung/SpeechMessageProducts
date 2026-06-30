using System;
using System.IO;
using ChurchReport.Payments;
using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using static ChurchReport.Services.MyPayFeeTypeHelper;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay callback 後的 ChurchReport LINE 通知服務。
    /// 此服務消費共用金流層的 <see cref="PaymentWorkflowResult"/>，但訊息內容、
    /// 收費單欄位與 LINE 推播策略都屬於 ChurchReport 產品流程，因此不放進共用金流專案。
    /// </summary>
    public class MyPayNotificationService
    {
        private readonly ILogger<MyPayNotificationService> _logger;
        private readonly MyPayMessageBuilder _messageBuilder;
        private readonly MyPayFeeTypeHelper _feeTypeHelper;

        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        });

        private static IConfiguration Configuration => s_lazyConfiguration.Value;

        public MyPayNotificationService(
            ILogger<MyPayNotificationService> logger,
            MyPayMessageBuilder messageBuilder,
            MyPayFeeTypeHelper feeTypeHelper)
        {
            _logger = logger;
            _messageBuilder = messageBuilder;
            _feeTypeHelper = feeTypeHelper;
        }

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

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {result?.ProductOrderId}");
                throw;
            }
        }

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

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE失敗通知失敗 - OrderId: {result?.ProductOrderId}");
                throw;
            }
        }

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
    }
}
