using Microsoft.Extensions.Configuration;
using QPay.Domain;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChurchReport.Tools
{
    internal static class QPayPaymentDebugLogger
    {
        private static readonly object s_writeLock = new object();
        private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            return builder.Build();
        });

        public static void WritePaymentResult(
            string processorName,
            string branchName,
            string shopNo,
            string payToken,
            QryOrderPay result,
            bool isPaymentSuccess,
            string paymentStatusText,
            string note = "",
            string correlationId = "",
            string requestContext = "",
            QryOrder orderQueryResult = null,
            string orderQueryError = "")
        {
            if (!IsEnabled())
            {
                return;
            }

            try
            {
                string logFilePath = GetLogFilePath();
                string logDirectory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string logEntry = BuildLogEntry(
                    processorName,
                    branchName,
                    shopNo,
                    payToken,
                    result,
                    isPaymentSuccess,
                    paymentStatusText,
                    note,
                    correlationId,
                    requestContext,
                    orderQueryResult,
                    orderQueryError);

                lock (s_writeLock)
                {
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[QPayPaymentDebugLogger] Write failed: " + ex.Message);
            }
        }

        public static bool IsEnabled()
        {
            return bool.TryParse(Configuration["PaymentDebugLog:Enabled"], out bool enabled) && enabled;
        }

        private static bool MaskSensitiveData()
        {
            string configuredValue = Configuration["PaymentDebugLog:MaskSensitiveData"];
            return string.IsNullOrWhiteSpace(configuredValue)
                || bool.TryParse(configuredValue, out bool maskSensitiveData) && maskSensitiveData;
        }

        private static string GetLogFilePath()
        {
            string configuredDirectory = Configuration["PaymentDebugLog:Directory"];
            string logDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? "Logs/PaymentDebug"
                : configuredDirectory.Trim();

            if (!Path.IsPathRooted(logDirectory))
            {
                logDirectory = Path.Combine(Directory.GetCurrentDirectory(), logDirectory);
            }

            string configuredPrefix = Configuration["PaymentDebugLog:FilePrefix"];
            string filePrefix = SanitizeFileName(string.IsNullOrWhiteSpace(configuredPrefix)
                ? "QPay_Dedication_CreditCard"
                : configuredPrefix.Trim());

            string fileName = filePrefix + "_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
            return Path.Combine(logDirectory, fileName);
        }

        private static string BuildLogEntry(
            string processorName,
            string branchName,
            string shopNo,
            string payToken,
            QryOrderPay result,
            bool isPaymentSuccess,
            string paymentStatusText,
            string note,
            string correlationId,
            string requestContext,
            QryOrder orderQueryResult,
            string orderQueryError)
        {
            var tsResult = result?.TSResultContent;
            var orderInfo = FindOrderInfo(orderQueryResult, tsResult?.OrderNo);
            var builder = new StringBuilder();

            builder.AppendLine("============================================================");
            builder.AppendLine("CorrelationId=" + Clean(correlationId));
            builder.AppendLine("Time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.AppendLine("MachineName=" + Clean(Environment.MachineName));
            builder.AppendLine("ProcessId=" + Process.GetCurrentProcess().Id);
            builder.AppendLine("ThreadId=" + Environment.CurrentManagedThreadId);
            builder.AppendLine("AspNetCoreEnvironment=" + Clean(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")));
            builder.AppendLine("CashEnvironment=" + Clean(Configuration["Cash_Environment"]));
            builder.AppendLine("CurrentDirectory=" + Clean(Directory.GetCurrentDirectory()));
            builder.AppendLine("BaseDirectory=" + Clean(AppContext.BaseDirectory));
            builder.AppendLine("RequestContext=" + Clean(requestContext));
            builder.AppendLine("Processor=" + Clean(processorName));
            builder.AppendLine("Branch=" + Clean(branchName));
            builder.AppendLine("IsPaymentSuccess=" + isPaymentSuccess);
            builder.AppendLine("PaymentStatusText=" + Clean(paymentStatusText));
            builder.AppendLine("TransactionFailureHint=" + Clean(QPayPaymentResultHelper.GetPaymentFailureHint(result, orderQueryResult)));
            builder.AppendLine("LegacyExactSCondition=" + (result?.Status == "S" && tsResult?.Status == "S"));
            builder.AppendLine("HasTSResultContent=" + (tsResult != null));
            builder.AppendLine("ShopNo=" + Clean(shopNo));
            builder.AppendLine("PayToken=" + Mask(payToken));
            builder.AppendLine("OrderNo=" + Clean(tsResult?.OrderNo));
            builder.AppendLine("AmountRaw=" + Clean(tsResult?.Amount));
            builder.AppendLine("AmountNtd=" + TryFormatAmountNtd(tsResult?.Amount));
            builder.AppendLine("ApiStatus=" + Clean(result?.Status));
            builder.AppendLine("ApiLeadingCode=" + ExtractLeadingCode(result?.Status));
            builder.AppendLine("ApiDescription=" + Clean(result?.Description));
            builder.AppendLine("TransactionStatus=" + Clean(tsResult?.Status));
            builder.AppendLine("TransactionLeadingCode=" + ExtractLeadingCode(tsResult?.Status));
            builder.AppendLine("TransactionDescription=" + Clean(tsResult?.Description));
            builder.AppendLine("Param1=" + Clean(tsResult?.Param1));
            builder.AppendLine("Param2=" + Clean(tsResult?.Param2));
            builder.AppendLine("Param3=" + Clean(tsResult?.Param3));
            builder.AppendLine("PayType=" + Clean(tsResult?.PayType));
            builder.AppendLine("TSNo=" + Clean(tsResult?.TSNo));
            builder.AppendLine("CCToken=" + Mask(tsResult?.CCToken));
            builder.AppendLine("LeftCCNo=" + MaskCardLeft(tsResult?.LeftCCNo));
            builder.AppendLine("RightCCNo=" + MaskCardRight(tsResult?.RightCCNo));
            builder.AppendLine("CCExpDate=" + Mask(tsResult?.CCExpDate));
            builder.AppendLine("OrderQueryExecuted=" + (orderQueryResult != null || !string.IsNullOrWhiteSpace(orderQueryError)));
            builder.AppendLine("OrderQueryError=" + Clean(orderQueryError));
            builder.AppendLine("OrderQueryStatus=" + Clean(orderQueryResult?.Status));
            builder.AppendLine("OrderQueryDescription=" + Clean(orderQueryResult?.Description));
            builder.AppendLine("OrderQueryCount=" + (orderQueryResult?.OrderList?.Count.ToString() ?? string.Empty));
            builder.AppendLine("OrderQueryOrderNo=" + Clean(orderInfo?.OrderNo));
            builder.AppendLine("OrderQueryTSNo=" + Clean(orderInfo?.TSNo));
            builder.AppendLine("OrderQueryPayStatus=" + Clean(orderInfo?.PayStatus));
            builder.AppendLine("OrderQueryPayStatusHint=" + GetOrderQueryPayStatusHint(orderInfo?.PayStatus));
            builder.AppendLine("OrderQueryPayType=" + Clean(orderInfo?.PayType));
            builder.AppendLine("OrderQueryAmountRaw=" + (orderInfo == null ? string.Empty : orderInfo.Amount.ToString()));
            builder.AppendLine("OrderQueryAmountNtd=" + (orderInfo == null ? string.Empty : TryFormatAmountNtd(orderInfo.Amount.ToString())));
            builder.AppendLine("OrderQueryTSDate=" + Clean(orderInfo?.TSDate));
            builder.AppendLine("OrderQueryApprovedDate=" + Clean(orderInfo?.ApprovedDate));
            builder.AppendLine("OrderQueryPayDate=" + Clean(orderInfo?.PayDate));
            builder.AppendLine("OrderQueryCardParamPresent=" + (orderInfo?.CardParam != null));
            builder.AppendLine("OrderQueryCardPayUrlPresent=" + !string.IsNullOrWhiteSpace(orderInfo?.CardParam?.CardPayURL));
            builder.AppendLine("OrderQueryCardHasAuthCode=" + !string.IsNullOrWhiteSpace(orderInfo?.CardParam?.AuthCode));
            builder.AppendLine("OrderQueryCardAuthCode=" + Mask(orderInfo?.CardParam?.AuthCode));
            builder.AppendLine("OrderQueryCardLeftCCNo=" + MaskCardLeft(orderInfo?.CardParam?.LeftCCNo));
            builder.AppendLine("OrderQueryCardRightCCNo=" + MaskCardRight(orderInfo?.CardParam?.RightCCNo));
            builder.AppendLine("OrderQueryCardCCExpDate=" + Mask(orderInfo?.CardParam?.CCExpDate));
            builder.AppendLine("OrderQueryCardCCToken=" + Mask(orderInfo?.CardParam?.CCToken));
            builder.AppendLine("OrderQueryAuthorizationHint=" + GetOrderQueryAuthorizationHint(orderInfo));
            builder.AppendLine("OrderQueryExpireDate=" + Clean(orderInfo?.ExpireDate));
            builder.AppendLine("OrderQueryRefundFlag=" + Clean(orderInfo?.RefundFlag));
            builder.AppendLine("OrderQueryPrdtName=" + Clean(orderInfo?.PrdtName));
            builder.AppendLine("OrderQueryMemo=" + Clean(orderInfo?.Memo));
            builder.AppendLine("OrderQueryParam1=" + Clean(orderInfo?.Param1));
            builder.AppendLine("OrderQueryParam2=" + Clean(orderInfo?.Param2));
            builder.AppendLine("OrderQueryParam3=" + Clean(orderInfo?.Param3));
            builder.AppendLine("Note=" + Clean(note));
            builder.AppendLine("============================================================");

            return builder.ToString();
        }

        private static string TryFormatAmountNtd(string amountRaw)
        {
            if (!decimal.TryParse(Clean(amountRaw), out decimal amountInCents))
            {
                return string.Empty;
            }

            return (amountInCents / 100m).ToString("0.##");
        }

        private static OrderInfo FindOrderInfo(QryOrder orderQueryResult, string orderNo)
        {
            if (orderQueryResult?.OrderList == null)
            {
                return null;
            }

            foreach (OrderInfo orderInfo in orderQueryResult.OrderList)
            {
                if (orderInfo == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(orderNo) || string.Equals(orderInfo.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase))
                {
                    return orderInfo;
                }
            }

            return null;
        }

        private static string GetOrderQueryPayStatusHint(string payStatus)
        {
            string cleanedPayStatus = Clean(payStatus).ToUpperInvariant();
            switch (cleanedPayStatus)
            {
                case "1C200":
                    return "\u4fe1\u7528\u5361\u8a02\u55ae\u5df2\u5efa\u7acb\u4f46\u672a\u53d6\u5f97\u6388\u6b0a/\u672a\u4ed8\u6b3e\u5165\u5e33\uff1b\u5e38\u8207 E2700 \u4ea4\u6613\u5931\u6557\u540c\u6642\u51fa\u73fe\uff0c\u8acb\u81f3\u6c38\u8c50\u6216\u6536\u55ae\u5f8c\u53f0\u67e5\u6388\u6b0a\u56de\u61c9\u78bc";
                case "Y":
                    return "\u5df2\u5b8c\u6210\u4ed8\u6b3e/\u5df2\u8acb\u6b3e";
                case "N":
                    return "\u4fe1\u7528\u5361\u672a\u8acb\u6b3e\uff08\u53ef\u80fd\u70ba\u672a\u4ed8\u6b3e\u3001\u5f85\u8acb\u6b3e\u3001\u53d6\u6d88\u6388\u6b0a\u6216\u6388\u6b0a\u903e\u671f\uff09";
                default:
                    return string.IsNullOrWhiteSpace(cleanedPayStatus) ? string.Empty : "\u672a\u77e5\u4ed8\u6b3e\u72c0\u614b";
            }
        }

        private static string GetOrderQueryAuthorizationHint(OrderInfo orderInfo)
        {
            if (orderInfo == null)
            {
                return string.Empty;
            }

            bool isCreditCard = string.Equals(Clean(orderInfo.PayType), "C", StringComparison.OrdinalIgnoreCase);
            bool hasApprovedDate = !string.IsNullOrWhiteSpace(orderInfo.ApprovedDate);
            bool hasPayDate = !string.IsNullOrWhiteSpace(orderInfo.PayDate);
            bool hasAuthCode = !string.IsNullOrWhiteSpace(orderInfo.CardParam?.AuthCode);

            if (!isCreditCard)
            {
                return string.Empty;
            }

            if (!hasApprovedDate && !hasPayDate && !hasAuthCode)
            {
                return "\u4fe1\u7528\u5361\u672a\u53d6\u5f97\u6388\u6b0a\u78bc\u3001\u6388\u6b0a\u6642\u9593\u8207\u4ed8\u6b3e\u6642\u9593\uff1b\u82e5 OrderPayQuery \u70ba E2700\uff0cAPP \u7aef\u5df2\u53ef\u5224\u65b7\u662f\u4fe1\u7528\u5361\u6388\u6b0a/\u4ed8\u6b3e\u6c92\u6709\u6210\u7acb\u3002\u7cbe\u78ba\u62d2\u7d55\u539f\u56e0\u9700\u5230\u91d1\u6d41/\u6536\u55ae\u5f8c\u53f0\u67e5\u6388\u6b0a\u56de\u61c9\u78bc\u3002";
            }

            if ((!hasApprovedDate || !hasPayDate) && hasAuthCode)
            {
                return "\u4fe1\u7528\u5361\u6709\u6388\u6b0a\u78bc\u4f46\u6388\u6b0a\u6642\u9593\u6216\u4ed8\u6b3e\u6642\u9593\u7a7a\u767d\uff1b\u8acb\u81f3\u91d1\u6d41\u5f8c\u53f0\u6838\u5c0d\u6388\u6b0a/\u8acb\u6b3e\u72c0\u614b\u662f\u5426\u540c\u6b65\u5ef6\u9072\u6216\u7570\u5e38\u3002";
            }

            if (hasApprovedDate && !hasPayDate)
            {
                return "\u4fe1\u7528\u5361\u5df2\u6709\u6388\u6b0a\u6642\u9593\u4f46\u5c1a\u7121\u4ed8\u6b3e/\u8acb\u6b3e\u6642\u9593\uff1b\u8acb\u78ba\u8a8d\u662f\u5426\u70ba\u5f85\u8acb\u6b3e\u3001\u81ea\u52d5\u8acb\u6b3e\u5c1a\u672a\u5b8c\u6210\u6216\u8acb\u6b3e\u5931\u6557\u3002";
            }

            if (hasApprovedDate && hasPayDate)
            {
                return "\u4fe1\u7528\u5361\u5df2\u6709\u6388\u6b0a\u6642\u9593\u8207\u4ed8\u6b3e/\u8acb\u6b3e\u6642\u9593\uff1b\u82e5\u4ecd\u986f\u793a\u5931\u6557\uff0c\u8acb\u6bd4\u5c0d\u56de\u547c\u6642\u9593\u5dee\u6216\u662f\u5426\u70ba\u4e0d\u540c\u4ea4\u6613\u72c0\u614b\u66f4\u65b0\u3002";
            }

            return "\u4fe1\u7528\u5361\u4ed8\u6b3e/\u8acb\u6b3e\u6642\u9593\u5b58\u5728\u4f46\u6388\u6b0a\u6642\u9593\u7a7a\u767d\uff1b\u8acb\u81f3\u91d1\u6d41\u5f8c\u53f0\u6838\u5c0d\u4ea4\u6613\u72c0\u614b\u3002";
        }

        private static string Mask(string value)
        {
            string cleaned = Clean(value);
            if (!MaskSensitiveData() || string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }

            if (cleaned.Length <= 8)
            {
                return new string('*', cleaned.Length);
            }

            return cleaned.Substring(0, 4) + "..." + cleaned.Substring(cleaned.Length - 4);
        }

        private static string MaskCardLeft(string value)
        {
            string cleaned = Clean(value);
            if (!MaskSensitiveData() || string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }

            return cleaned.Length <= 2
                ? new string('*', cleaned.Length)
                : cleaned.Substring(0, 2) + new string('*', cleaned.Length - 2);
        }

        private static string MaskCardRight(string value)
        {
            string cleaned = Clean(value);
            if (!MaskSensitiveData() || string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }

            return cleaned.Length <= 2
                ? new string('*', cleaned.Length)
                : new string('*', cleaned.Length - 2) + cleaned.Substring(cleaned.Length - 2);
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }

        private static string ExtractLeadingCode(string value)
        {
            string cleaned = Clean(value).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            char[] separators =
            {
                ' ',
                '-',
                '\u2013',
                '\u2014',
                '\uff0d',
                ':',
                '\uff1a',
                ',',
                '\uff0c',
                ';',
                '\uff1b'
            };

            int separatorIndex = cleaned.IndexOfAny(separators);
            if (separatorIndex > 0)
            {
                cleaned = cleaned.Substring(0, separatorIndex);
            }

            return cleaned.Trim();
        }

        private static string Clean(string value)
        {
            return value?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
        }

        private static IConfiguration Configuration => s_lazyConfiguration.Value;
    }
}
