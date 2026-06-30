using ChurchReport.Payments;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChurchReport.Tools
{
    /// <summary>
    /// ChurchReport 產品流程用的付款除錯記錄器。
    /// 它只記錄已標準化後的付款結果與產品流程分支，不負責 provider protocol、CRM 更新或 LINE 通知。
    /// </summary>
    internal static class DonationPaymentDebugLogger
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
            DonationPaymentWorkflowResult result,
            bool isPaymentSuccess,
            string paymentStatusText,
            string note = "",
            string correlationId = "",
            string requestContext = "")
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
                    requestContext);

                lock (s_writeLock)
                {
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DonationPaymentDebugLogger] Write failed: " + ex.Message);
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
                ? "DonationPayment_Dedication_CreditCard"
                : configuredPrefix.Trim());

            string fileName = filePrefix + "_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
            return Path.Combine(logDirectory, fileName);
        }

        private static string BuildLogEntry(
            string processorName,
            string branchName,
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult result,
            bool isPaymentSuccess,
            string paymentStatusText,
            string note,
            string correlationId,
            string requestContext)
        {
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
            builder.AppendLine("TransactionFailureHint=" + Clean(DonationPaymentResultHelper.GetPaymentFailureHint(result)));
            builder.AppendLine("ShopNo=" + Clean(shopNo));
            builder.AppendLine("PayToken=" + Mask(payToken));
            builder.AppendLine("WorkflowShopNo=" + Clean(result?.ShopNo));
            builder.AppendLine("WorkflowPayToken=" + Mask(result?.PayToken));
            builder.AppendLine("OrderNo=" + Clean(result?.OrderNo));
            builder.AppendLine("AmountRaw=" + Clean(result?.AmountMinorUnits));
            builder.AppendLine("AmountNtd=" + TryFormatAmountNtd(result?.AmountMinorUnits));
            builder.AppendLine("WorkflowAmount=" + (result?.Amount?.ToString("0.##") ?? string.Empty));
            builder.AppendLine("Status=" + Clean(result?.Status));
            builder.AppendLine("Description=" + Clean(result?.Description));
            builder.AppendLine("ProductEntityId=" + Clean(result?.ProductEntityId));
            builder.AppendLine("PaymentOrganization=" + Clean(result?.PaymentOrganization));
            builder.AppendLine("PaymentCategory=" + Clean(result?.PaymentCategory));
            builder.AppendLine("PayType=" + Clean(result?.PayType));
            builder.AppendLine("ProviderTransactionId=" + Clean(result?.ProviderTransactionId));
            builder.AppendLine("CCToken=" + Mask(result?.CCToken));
            builder.AppendLine("LeftCCNo=" + MaskCardLeft(result?.LeftCCNo));
            builder.AppendLine("RightCCNo=" + MaskCardRight(result?.RightCCNo));
            builder.AppendLine("CCExpDate=" + Mask(result?.CCExpDate));
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

        private static string Clean(string value)
        {
            return value?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
        }

        private static IConfiguration Configuration => s_lazyConfiguration.Value;
    }
}
