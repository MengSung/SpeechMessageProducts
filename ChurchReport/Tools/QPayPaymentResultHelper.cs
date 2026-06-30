using ChurchReport.Payments;

namespace ChurchReport.Tools;

/// <summary>
/// 舊 <c>QPayPaymentResultHelper</c> 名稱的相容包裝。
/// 實際付款結果判斷集中在 <see cref="DonationPaymentResultHelper"/>，避免新程式繼續依賴 QPay 命名。
/// </summary>
[System.Obsolete("Use DonationPaymentResultHelper. QPayPaymentResultHelper is retained only as a compatibility alias during migration.")]
internal static class QPayPaymentResultHelper
{
    public static bool IsPaymentSuccess(QPayWorkflowPaymentResult result)
    {
        return DonationPaymentResultHelper.IsPaymentSuccess(result);
    }

    public static string GetPaymentStatusText(QPayWorkflowPaymentResult result)
    {
        return DonationPaymentResultHelper.GetPaymentStatusText(result);
    }

    public static string GetPaymentFailureHint(QPayWorkflowPaymentResult result)
    {
        return DonationPaymentResultHelper.GetPaymentFailureHint(result);
    }
}