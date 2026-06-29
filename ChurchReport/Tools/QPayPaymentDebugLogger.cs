using ChurchReport.Payments;

namespace ChurchReport.Tools;

/// <summary>
/// 舊 <c>QPayPaymentDebugLogger</c> 名稱的相容包裝。
/// 實際記錄邏輯集中在 <see cref="DonationPaymentDebugLogger"/>，避免新程式繼續依賴 QPay 命名。
/// </summary>
[System.Obsolete("Use DonationPaymentDebugLogger. QPayPaymentDebugLogger is retained only as a compatibility alias during migration.")]
internal static class QPayPaymentDebugLogger
{
    public static void WritePaymentResult(
        string processorName,
        string branchName,
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult result,
        bool isPaymentSuccess,
        string paymentStatusText,
        string note = "",
        string correlationId = "",
        string requestContext = "")
    {
        DonationPaymentDebugLogger.WritePaymentResult(
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
    }

    public static bool IsEnabled()
    {
        return DonationPaymentDebugLogger.IsEnabled();
    }
}