/*
檔案整體說明
目的與定位：Live 編排器與共用 Shadow SDK round 的單命令接線，沒有自己的種子或比較邏輯。
主要流程：每個已核定 LiveCheckScenario 精確對應一個 prepared；讀寫追蹤只限當下組織，
D34 與用量由命令 owner LiveSafety 提供，round 的 SDK 及清理仍由原實作持有。
隔離與生命週期：所有映射只活於命令，沒有 static mutable、其他組織後備或背景工作。
SDK、Safety 均借用，不 Dispose 外層依賴；每輪 owner 由 LiveCheckRunner 最後釋放。
失敗與驗證：找不到同一情境物件時拒絕，不按顯示名稱挑第一筆；取消先檢再委派。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SpeechMessage.Dynamics.Deployment.Shadow;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>命令區域 adapter，所有有狀態依賴由 CLI using 擁有。</summary>
internal sealed class LiveSdkRuntime(ShadowSdkRuntime sdk, LiveSafety safety,
    IReadOnlyDictionary<LiveCheckScenario, ShadowSdkPreparedScenario> scenarios,
    IReadOnlyDictionary<LiveCheckScenario, LiveActivityTemplate> activities) : ILiveCheckRuntime
{
    /// <summary>每輪重新讀取權威 D34，不能使用啟動時快照。</summary>
    public void VerifyD34(CancellationToken cancellationToken) => _ = safety.Verify(cancellationToken);
    /// <summary>單組織當下追蹤值。</summary>
    public int ReadTraceSetting() => sdk.ReadTraceSetting();
    /// <summary>只還原追蹤，不改步驟啟用狀態。</summary>
    public void WriteTraceSetting(int value, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); sdk.WriteTraceSetting(value); }
    /// <summary>測試 Channel 前後用量。</summary>
    public long ReadUsage(CancellationToken cancellationToken) => safety.Usage(cancellationToken);
    /// <summary>只使用本命令已準備的配對，不載入其他組織資料。</summary>
    public ILiveCheckRound CreateRound(LiveCheckScenario scenario) => scenarios.TryGetValue(scenario, out var prepared)
        ? sdk.CreateLiveRound(prepared, activities.TryGetValue(scenario, out var activity) ? activity : null) : throw new InvalidDataException("Live scenario was not prepared.");
}
