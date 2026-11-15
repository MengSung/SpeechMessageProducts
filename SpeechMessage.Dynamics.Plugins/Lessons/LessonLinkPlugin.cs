/*
 * 檔案整體說明
 * 目的與定位：P5-07課程連結薄Plugin，保留舊Create／Update及Depth>1返回契約，
 * 由Core建立圖片待辦取代原本在同步PostOperation畫圖及寫入網路磁碟。
 * 主要流程：共用基底驗模式與深度，僅接受new_disciple_lessons的Create／Update，
 * 確認PostOperation後委派LessonLinkService；舊Plugin沒有呼叫問卷產生函式，新版
 * 同樣不要求問卷。舊吞錯改由共用基底保留並重新拋出，不以無連結假成功。
 * 使用邊界與維護：實例只保留基底不可變設定，SDK、來源列及store只屬於Execute，
 * 沒有Session、cache、背景Task、timer或機密。平台SDK借用且不Dispose；Shadow
 * recorder由基底finally排空。同步步驟僅建立資料庫待辦，不推播、不畫圖、不連外。
 * 離線測試驗真入口及有限完整投影，不代表實機啟用；本階段維持停用佈署。
 */
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using System;

namespace SpeechMessage.Dynamics.Plugins.Lessons;

/// <summary>課程Create／Update的無狀態QR待辦入口，問卷差異由原活動另行表達。</summary>
public sealed class LessonLinkPlugin : PluginShellBase
{
    /// <summary>註冊設定交共用基底驗證；保留舊Depth大於一即返回政策。</summary>
    public LessonLinkPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1)) { }

    /// <summary>僅PostOperation讀正式來源歸屬並同步建立待辦，不接受呼叫者指定教會。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_disciple_lessons" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40) throw new InvalidOperationException("Lesson links require PostOperation.");
        _ = LessonLinkService.Enqueue(services.Organization, input.PrimaryEntityId, includePoll: false);
    }
}
