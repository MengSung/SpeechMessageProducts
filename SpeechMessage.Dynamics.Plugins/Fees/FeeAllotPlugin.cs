/*
 * 檔案整體說明
 *
 * 目的與定位：P5-11 收費單同步外掛外殼，承接退役 AllotPlugIn 的入列方向。
 * 本檔只負責平台事件、Depth、Shadow／Live 服務邊界與一次性 SDK 借用；教會
 * 歸屬、付款日、每日 sourceId 與待辦原子建立全部委派 GoodShepherd Core。
 *
 * 主要功能與流程：只接受 new_fee 的 Create／Update、PostOperation、同步事件，
 * 由 Core 讀取 fee→contact→account 並以教會當地日期建立 AllotQuery；未來付款日
 * 不建待辦。活動輸入不能指定教會，Live／Shadow 均不執行舊 DLL 或推播。
 *
 * 使用邊界與維護：PluginShellBase 管理本次 context、recording、trace 與服務；
 * 本類不保存 Entity、Session、token、快取、Timer 或背景工作。失敗交回平台，
 * Shadow 的寫入意圖由共用 recorder 有界輸出；離線測試不代表 D365 實機驗收。
 */
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Fees;

namespace SpeechMessage.Dynamics.Plugins.Fees;

/// <summary>收費單 PostOperation 入列每日撥款查詢待辦的薄外掛。</summary>
public sealed class FeeAllotPlugin : PluginShellBase
{
    /// <summary>保留退役 AllotPlugIn 的深度二門檻；Create 不受深度限制。</summary>
    public FeeAllotPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2, DepthException.Create)) { }

    /// <summary>只在核定同步 PostOperation 事件委派 Core 建立待辦。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_fee"
            || input.MessageName is not ("Create" or "Update"))
            return;
        if (input.Stage != 40 || input.Mode != 0)
            throw new InvalidPluginExecutionException("Fee allot requires synchronous PostOperation.");

        _ = FeeAllotTaskService.Enqueue(services.Organization, input.PrimaryEntityId, new ChurchClock());
    }
}
