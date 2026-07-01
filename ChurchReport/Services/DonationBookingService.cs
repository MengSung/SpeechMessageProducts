namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 認獻單狀態與顯示文字服務。
    ///
    /// 認獻單是 ChurchReport 奉獻領域模型，並非通用金流核心概念。
    /// 因此這個服務留在 ChurchReport，只把原本散在 DonationPaymentManager 的
    /// OptionSet 對照表集中管理，讓 Manager 不再承擔狀態轉換細節。
    /// </summary>
    public sealed class DonationBookingService
    {
        public static string ConvertStatus(int optionSetValue)
        {
            return optionSetValue switch
            {
                100000000 => "尚未啟動",
                100000001 => "進行中",
                100000002 => "已結案",
                100000003 => "啟動失敗",
                100000004 => "已取消",
                _ => "尚未啟動"
            };
        }
    }
}
