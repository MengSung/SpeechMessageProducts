using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Tools;

/// <summary>
/// 舊 <c>QPayFeeProcessor</c> 名稱的相容包裝。
/// 真正的收費單付款完成後流程已移到 <see cref="DonationFeePaymentProcessor"/>；
/// 此類別只能保留建構子與舊方法名稱轉交，不能再加入 CRM、LINE 或付款結果分支邏輯。
/// </summary>
[System.Obsolete("Use DonationFeePaymentProcessor. QPayFeeProcessor is retained only as a compatibility alias during migration.")]
public class QPayFeeProcessor : DonationFeePaymentProcessor
{
    /// <summary>
    /// 舊程式無參數建立處理器時的相容入口。
    /// </summary>
    public QPayFeeProcessor()
        : base()
    {
    }

    /// <summary>
    /// 舊測試或 DI 路徑仍提供 ToolUtilityProvider 時的相容入口。
    /// </summary>
    public QPayFeeProcessor(IToolUtilityProvider toolUtilityProvider)
        : base(toolUtilityProvider)
    {
    }

    /// <summary>
    /// 舊方法名稱的相容入口；所有實際流程都轉交給中性的 <see cref="DonationFeePaymentProcessor.HandlePaymentReturn"/>。
    /// </summary>
    public ActionResult QPayFeeProcessorReturnUrl(
        string ShopNo,
        string PayToken,
        QPayWorkflowPaymentResult paymentResult,
        string correlationId = "",
        string requestContext = "")
    {
        return HandlePaymentReturn(ShopNo, PayToken, paymentResult, correlationId, requestContext);
    }
}