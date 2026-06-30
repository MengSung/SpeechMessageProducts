using ChurchReport.Payments;

namespace ChurchReport.Models;

/// <summary>
/// 舊 <c>QpayManager</c> 名稱的相容包裝。
/// 實際的 ChurchReport 奉獻付款 UI 狀態、CRM 更新、LINE 通知與付款建立流程，
/// 已移到 <see cref="DonationPaymentManager"/>；本類別只能保留建構子轉交，不能再加入任何產品流程邏輯。
/// </summary>
[System.Obsolete("Use DonationPaymentManager. QpayManager is retained only as a compatibility alias during migration.")]
public class QpayManager : DonationPaymentManager
{
    /// <summary>
    /// 舊程式無參數建立 manager 時的相容入口。
    /// </summary>
    public QpayManager()
        : base()
    {
    }

    /// <summary>
    /// 新流程使用的中性 adapter 建構子。
    /// </summary>
    public QpayManager(DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        : base(donationPaymentCreateGatewayAdapter)
    {
    }

    /// <summary>
    /// 測試或過渡期可直接提供中性 adapter 介面。
    /// </summary>
    public QpayManager(IDonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        : base(donationPaymentCreateGatewayAdapter)
    {
    }

    /// <summary>
    /// 舊程式仍以 QPay 命名 adapter 建立 manager 時使用的相容建構子。
    /// </summary>
    public QpayManager(QPayCreatePaymentGatewayAdapter qPayCreatePaymentGatewayAdapter)
        : base(qPayCreatePaymentGatewayAdapter)
    {
    }
}
