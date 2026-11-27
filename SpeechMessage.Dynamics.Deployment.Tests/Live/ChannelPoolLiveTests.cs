/*
檔案整體說明
目的與定位：提供線 A F3 名額池的兩個真實 Live3P 驗收入口，只有明確選取 Live3P／F3 才應執行。
主要功能與流程：好牧人案例自行建立 c101–c120 二十個測試租戶並同時送出不指定 Channel 的申請；
建設公司案例建立 b101 並驗證正式配發只來自 GoodButler。實際前置、所有權清冊、正式 ID 斷言及 finally
清理由同目錄 F3ChannelPoolLiveSupport 負責；兩個入口沒有建構式、靜態初始化或測試發現時的 I/O。
使用邊界與維護：目前交付僅供離線編譯，不代表已允許執行實機。正式執行須先由主代理持有 lane=A、wp=F3
測試環境鎖及完成受審部署；缺組態、身分、schema、Active key、精確平台步驟或可用池都必須失敗，不能 Skip。
資料與資源：僅使用本輪新 GUID 與 marker 所有的合成 account／request，永不連 goodshepherdback 或呼叫 LINE；
helper 唯一擁有各自獨占的連線、取消來源及先寫後送且 flush 的本機 ignored 清冊，排空所有申請後才釋放與清理。
驗證方式：離線契約只檢查入口標記及純資料安全邊界，不能以編譯成功替代實機並行、隔離或數量基線驗收。
*/
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>兩個固定測試組織的真實 F3 入口；沒有可由測試參數擴權的組織或 Channel 選擇器。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "F3")]
public sealed class ChannelPoolLiveTests
{
    /// <summary>自行建立二十租戶，驗證二十個正式配發名額互異，結束後釋放並回復原數量基線。</summary>
    [Fact]
    public Task SlotRequest_TwentyConcurrent_AllDistinct_Live() =>
        F3ChannelPoolLiveSupport.RunAsync("goodshepherd20", 20);

    /// <summary>自行建立 b101，驗證組織權威產品及正式 Channel 均屬 GoodButler，最後清除本輪資料。</summary>
    [Fact]
    public Task Construction_AllocatesOnlyGoodButlerChannel_Live() =>
        F3ChannelPoolLiveSupport.RunAsync("construction", 1);
}
