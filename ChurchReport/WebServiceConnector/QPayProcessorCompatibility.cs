using ChurchReport.Payments;
using ChurchReport.Tools;
using Line.Messaging;

namespace ChurchReport.WebServiceConnector;

/// <summary>
/// 舊 <c>QPayProcessor</c> 名稱的相容包裝。
/// 實際奉獻付款流程已移到 <see cref="DonationPaymentProcessor"/>；
/// 本類別只保留既有程式、測試或外部編譯參照不會立刻中斷，不能再加入任何產品流程邏輯。
/// </summary>
[System.Obsolete("Use DonationPaymentProcessor. QPayProcessor is retained only as a compatibility alias during migration.")]
public class QPayProcessor : DonationPaymentProcessor
{
    /// <summary>
    /// 舊程式仍注入 QPay 命名 adapter 時，透過中性介面轉交給主要 processor。
    /// </summary>
    public QPayProcessor(QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter)
        : base((IDonationPaymentCreateGatewayAdapter)qPayCreatePaymentGatewayAdapter)
    {
    }

    /// <summary>
    /// 舊 LINE Bot 整合流程仍會直接 new QPayProcessor；此建構子只轉交到新 processor，
    /// 確保主實作不再留在 QPay 命名的 partial class。
    /// </summary>
    public QPayProcessor(
        LineMessagingClient lineMessagingClient,
        PushUtility pushUtility,
        ReplyUtility replyUtility,
        QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter)
        : base(
              lineMessagingClient,
              pushUtility,
              replyUtility,
              (IDonationPaymentCreateGatewayAdapter)qPayCreatePaymentGatewayAdapter)
    {
    }
}
