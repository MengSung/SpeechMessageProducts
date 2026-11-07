/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P2-02 GoodShepherd.Core 的雙目標產品組件選擇及通知服務骨架，
 * 防止 net48 測試宿主被誤認為產品組件 target，或新核心未納入預定模組邊界。
 *
 * 主要功能與流程：每個測試宿主載入其 ProjectReference 對應的產品組件，讀取
 * TargetFrameworkAttribute 並確認 MemberNotificationService 型別存在。net48 宿主
 * 必須載入 net462 產品輸出；net10.0 宿主必須載入 net10.0 產品輸出。此 smoke 不呼叫
 * 服務、不連線 D365／LINE，也不讀取 secrets；錯誤 target 或缺少骨架型別時以明確
 * xUnit assertion 失敗。
 *
 * 使用邊界與維護：測試只讀載入組件的中繼資料，沒有共享可變狀態、網路、檔案寫入、
 * 背景工作或需額外 Dispose 的資源。新增服務行為後，應以各自的契約測試驗證，不可
 * 將本 smoke 當成教會隔離、授權、推播或生命週期驗收。
 */
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>
/// 驗證雙目標測試宿主實際載入相符產品 target，並以未注入外部依賴的方式確認 P2-02
/// 通知服務骨架存在。若參考解析、產品 target 或骨架缺失，斷言會在離線環境確定失敗。
/// </summary>
public sealed class GoodShepherdCoreSmokeTests
{
    /// <summary>
    /// 先確認通知服務骨架存在，再核對當前測試宿主選取的產品 target metadata。
    /// 空專案尚無任何 Compile 項目時，SDK 不會產生 TargetFrameworkAttribute；因此先
    /// 以服務缺失作為預期紅燈，加入產品來源後再由 framework 斷言保護雙目標解析。
    /// </summary>
    [Fact(DisplayName = "雙目標宿主載入正確的產品組件")]
    public void ProjectReference_LoadsMatchingProductTarget_AndServiceSkeleton()
    {
        var productAssembly = Assembly.Load("SpeechMessageProducts.GoodShepherd.Core");

        Assert.NotNull(productAssembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Notifications.MemberNotificationService",
            throwOnError: false));

        var framework = productAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
#if NETFRAMEWORK
        Assert.Equal(".NETFramework,Version=v4.6.2", framework?.FrameworkName);
#else
        Assert.Equal(".NETCoreApp,Version=v10.0", framework?.FrameworkName);
#endif
    }
}
