/*
檔案整體說明
目的與定位：部署 Live 準備階段的固定原因碼與安全識別包裝，供 CLI 將失敗定位至
準備關卡；不是比對結果，也不能作為降級 dormant 或通過實機驗收的證據。
主要流程：每個受測關卡指定不可重編的碼；只保存有界、跳脫後的模組、情境、
alias 與實體識別。窄範圍 Capture 保留真實例外供共用告警及數字 fault 診斷，
不讀取 Message、Data、TraceText、ErrorDetails、欄位值、憑證或業務內容。
生命週期與隔離：包裝只屬單一失敗命令，沒有靜態可變狀態、SDK、快取或背景工作；
inner 的保留期間與原命令失敗相同，不另保存服務或 delegate。正常呼叫端取消原樣
傳遞，內部逾時仍是失敗；Capture 只包操作本身，不取代既有 finally／Dispose。
驗證方式：離線真準備關卡故障注入、控制字元／長度、數字 fault、取消與原例外身分
測試；新增碼必須追加，不能重用舊碼或放寬原先安全檢查。
*/
using System;
using System.Globalization;
using System.Threading;
using SpeechMessage.Dynamics.Deployment.Shadow;

namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>固定碼表；數字為操作紀錄的持久契約，新增原因只可向後追加。</summary>
internal enum LivePreparationReason
{
    /// <summary>當下完整快照無法產生安全 off 計畫，尚未建立資料。</summary>
    OffPlan = 1,
    /// <summary>舊步驟或流程仍啟用，禁止與新程式併行。</summary>
    LegacyEnabled = 2,
    /// <summary>權威 D34、測試 token 或必要設定驗證失敗。</summary>
    Safety = 3,
    /// <summary>來源中沒有明列的 liveShadow 情境。</summary>
    NoScenario = 4,
    /// <summary>情境對應不到唯一新步驟或活動。</summary>
    AmbiguousBinding = 5,
    /// <summary>所選步驟未啟用，或設定並非 Live。</summary>
    StepState = 6,
    /// <summary>情境沒有恰好一個空欄位共享測試聯絡人。</summary>
    ContactCount = 7,
    /// <summary>權威 metadata 取得或結構驗證失敗。</summary>
    Metadata = 8,
    /// <summary>活動含尚未核定專用樣板的輸入，不可默默忽略。</summary>
    ActivityInputs = 9,
    /// <summary>流程樣板讀取、解析或選取失敗，或沒有可安全複製的停用樣板。</summary>
    ActivityTemplate = 10,
    /// <summary>情境 factory 的欄位、別名、映像或期望驗證拒絕。</summary>
    PrepareScenario = 11,
    /// <summary>Live 不可使用 Shadow 切換／token 提供者，違反能力隔離。</summary>
    ForbiddenShadowCapability = 12,
    /// <summary>固定來源檔讀取或嚴格解析失敗。</summary>
    ScenarioSource = 13,
    /// <summary>註冊來源、部署產物或組織現況快照準備失敗。</summary>
    RegistrationSnapshot = 14,
    /// <summary>白名單服務連線建立失敗，不能推定為權限或快取。</summary>
    Connection = 15,
    /// <summary>本機同組織寫鎖或操作證據目錄無法建立；尚未執行業務寫入。</summary>
    LocalResources = 16
}

/// <summary>只含固定原因與受審識別的單命令失敗；原例外保留但永不直接列印。</summary>
internal sealed class LivePreparationFailure : Exception
{
    /// <summary>建立單行有界摘要；null 代表該關卡尚未選到情境或別名，不能猜測。</summary>
    internal LivePreparationFailure(LivePreparationReason reason, string? module, string? scenario = null,
        string? alias = null, string? entity = null, Exception? inner = null)
        : base("Live preparation rejected this operation.", inner)
    {
        SafeSummary = "LIVE-PREP-" + ((int)reason).ToString("D2", CultureInfo.InvariantCulture) +
            " module=" + ShadowSeedCreateException.EscapeDiagnosticAlias(module) +
            "; scenario=" + ShadowSeedCreateException.EscapeDiagnosticAlias(scenario) +
            "; alias=" + ShadowSeedCreateException.EscapeDiagnosticAlias(alias) +
            "; entity=" + ShadowSeedCreateException.EscapeDiagnosticAlias(entity);
    }

    /// <summary>僅可列印此安全摘要；最大四個64字元來源識別經可見跳脫，不含原錯誤本文。</summary>
    internal string SafeSummary { get; }

    /// <summary>
    /// 只包住明確準備操作的失敗，不捕捉 caller 正常取消，也不覆蓋已有更精確的原因。
    /// delegate 同步返回後即不保留；既有 owner 的 finally 與原始 SDK fault 完全保留。
    /// </summary>
    internal static T Capture<T>(LivePreparationReason reason, string? module, string? scenario,
        string? alias, string? entity, Func<T> operation, CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        try { return operation(); }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested) { throw; }
        catch (LivePreparationFailure) { throw; }
        catch (Exception failure) { throw new LivePreparationFailure(reason, module, scenario, alias, entity, failure); }
    }
}
