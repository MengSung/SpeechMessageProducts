using System;
using System.Collections.Generic;
using ChurchReport.Payments;

namespace ChurchReport.Tools
{
    /// <summary>
    /// ChurchReport 產品流程用的付款結果判斷工具。
    /// 它只判斷已標準化後的付款結果，不呼叫 provider API，也不更新 CRM 或 LINE。
    /// </summary>
    internal static class DonationPaymentResultHelper
    {
        private static readonly HashSet<string> s_successCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "S",
            "SUCCESS",
            "OK",
            "0000",
            "S0000",
            "S00000"
        };

        private static readonly HashSet<string> s_failureCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "F",
            "FAIL",
            "FAILED",
            "ERROR",
            "N",
            "DECLINED"
        };

        public static bool IsPaymentSuccess(DonationPaymentWorkflowResult result)
        {
            if (result == null)
            {
                return false;
            }

            if (IsFailureStatus(result.Status) || ContainsFailureText(result.Description))
            {
                return false;
            }

            return IsSuccessStatus(result.Status)
                || IsSuccessStatus(result.Description)
                || ContainsSuccessText(result.Description);
        }

        public static string GetPaymentStatusText(DonationPaymentWorkflowResult result)
        {
            if (result == null)
            {
                return "\u7121\u56de\u50b3\u72c0\u614b";
            }

            string status = Clean(result.Status);
            string description = Clean(result.Description);

            if (!string.IsNullOrWhiteSpace(description))
            {
                if (string.IsNullOrWhiteSpace(status) || HasSameLeadingCode(status, description))
                {
                    return description;
                }

                return status + " - " + description;
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            return "\u7121\u56de\u50b3\u72c0\u614b";
        }

        public static string GetPaymentFailureHint(DonationPaymentWorkflowResult result)
        {
            if (result == null)
            {
                return "\u7121\u4ed8\u6b3e\u67e5\u8a62\u7d50\u679c\uff0c\u8acb\u5148\u78ba\u8a8d\u91d1\u6d41\u67e5\u8a62\u662f\u5426\u6210\u529f\u56de\u50b3\u3002";
            }

            if (IsPaymentSuccess(result))
            {
                return "\u4ea4\u6613\u5df2\u5224\u65b7\u70ba\u6210\u529f\uff0c\u7121\u5931\u6557\u539f\u56e0\u3002";
            }

            string descriptionCode = ExtractLeadingCode(result.Description);
            string statusCode = ExtractLeadingCode(result.Status);
            string failureCode = !string.IsNullOrWhiteSpace(descriptionCode) && descriptionCode != statusCode
                ? descriptionCode
                : statusCode;

            return BuildFailureHint(failureCode, result);
        }

        private static string BuildFailureHint(string failureCode, DonationPaymentWorkflowResult result)
        {
            switch ((failureCode ?? string.Empty).ToUpperInvariant())
            {
                case "E2700":
                    return "E2700: \u91d1\u6d41\u56de\u50b3\u4fe1\u7528\u5361\u4ea4\u6613\u5931\u6557\uff0c\u8868\u793a API \u67e5\u8a62\u6210\u529f\uff0c\u4f46\u6388\u6b0a/\u4ea4\u6613\u672c\u8eab\u672a\u6210\u529f\u3002\u5efa\u8b70\u81f3\u6c38\u8c50\u6216\u6536\u55ae\u5f8c\u53f0\u4ee5 OrderNo/TSNo \u67e5\u8a62\u6388\u6b0a\u56de\u61c9\u78bc\uff0c\u4e26\u8acb\u6301\u5361\u4eba\u78ba\u8a8d\u5361\u865f\u3001\u6709\u6548\u671f\u3001CVV\u30013D \u9a57\u8b49\u3001\u984d\u5ea6\u6216\u767c\u5361\u9280\u884c\u98a8\u63a7\u3002";
                case "F":
                case "FAIL":
                case "FAILED":
                    return "F: \u4ea4\u6613\u7d50\u679c\u70ba\u5931\u6557\u3002\u8acb\u642d\u914d PaymentStatusText \u5224\u65b7\uff0c\u5fc5\u8981\u6642\u81f3\u91d1\u6d41\u5f8c\u53f0\u67e5\u8a62\u66f4\u7d30\u7684\u6388\u6b0a\u539f\u56e0\u3002";
                case "N":
                    return "N: \u8a02\u55ae\u672a\u5b8c\u6210\u4ed8\u6b3e\u6216\u672a\u8acb\u6b3e\u3002\u82e5\u70ba\u4fe1\u7528\u5361\uff0c\u53ef\u80fd\u662f\u672a\u4ed8\u6b3e\u3001\u5f85\u8acb\u6b3e\u3001\u53d6\u6d88\u6388\u6b0a\u6216\u6388\u6b0a\u903e\u671f\u3002";
                default:
                    string statusText = GetPaymentStatusText(result);
                    return "\u672a\u5efa\u7acb\u5c08\u7528\u5c0d\u7167\u7684\u5931\u6557\u78bc\u3002FailureCode=" + Clean(failureCode) + "\uff1bPaymentStatusText=" + Clean(statusText) + "\u3002\u8acb\u4ee5 OrderNo/TSNo \u5230\u91d1\u6d41\u5f8c\u53f0\u67e5\u8a62\u5b8c\u6574\u6388\u6b0a\u56de\u61c9\u78bc\u8207\u767c\u5361\u9280\u884c\u56de\u61c9\u3002";
            }
        }

        private static bool IsSuccessStatus(string value)
        {
            return s_successCodes.Contains(ExtractLeadingCode(value));
        }

        private static bool IsFailureStatus(string value)
        {
            string code = ExtractLeadingCode(value);
            return s_failureCodes.Contains(code) || code.StartsWith("F", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsSuccessText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("\u4ea4\u6613\u6210\u529f", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u4ed8\u6b3e\u6210\u529f", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u6388\u6b0a\u6210\u529f", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u8655\u7406\u6210\u529f", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsFailureText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("\u5931\u6557", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u53d6\u6d88", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u653e\u68c4", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u903e\u671f", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u984d\u5ea6\u4e0d\u8db3", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("\u9918\u984d\u4e0d\u8db3", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("declined", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasSameLeadingCode(string left, string right)
        {
            string leftCode = ExtractLeadingCode(left);
            string rightCode = ExtractLeadingCode(right);

            return !string.IsNullOrWhiteSpace(leftCode)
                && string.Equals(leftCode, rightCode, StringComparison.OrdinalIgnoreCase);
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
            return value?.Trim() ?? string.Empty;
        }
    }
}
