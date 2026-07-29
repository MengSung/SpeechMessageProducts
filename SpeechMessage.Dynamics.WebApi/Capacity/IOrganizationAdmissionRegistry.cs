// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionRegistry.cs
// 目的：定義 Canonical Organization 容量登錄與引用計數租約，讓多個 Profile Generation
//       可共用唯一的 Admission Manager，同時保有各自獨立的 Client／Token／Handler Runtime。
//
// 生命週期契約：
// - 每次 Runtime Generation 建立時取得一個 Registration。
// - Generation 完成 drain 並回收 Transport 後才釋放 Registration。
// - 最後一個 Registration 釋放後，Registry 才 Dispose Manager 與 Runtime Host Slot。
// - Registration 與 Registry 的同步／非同步 Dispose 都必須可重複且只執行一次。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 表示一個 Profile Generation 對共用 Organization Admission Manager 的引用租約。
/// Registration 不包含 Request、User、Session、JWT、Token 或 Credential；它只保存不可變 Plan 與 Manager 引用。
/// 呼叫端必須在 Generation 的執行租約與 Transport 都完成清理後才釋放此物件，避免過早釋放 Host Slot。
/// </summary>
public interface IOrganizationAdmissionRegistration : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// 取得此 Registration 綁定的不可變 Organization 容量計畫。
    /// </summary>
    OrganizationAdmissionPlan Plan { get; }

    /// <summary>
    /// 取得同一 Canonical Organization 所共用的 Admission Manager。
    /// 只有容量、排隊與 Host Slot 狀態可以共用；Token、Credential、Client 與 Handler 不可放入 Manager。
    /// </summary>
    IOrganizationAdmissionManager Manager { get; }
}

/// <summary>
/// 以 Canonical Organization Key 管理共用 Admission Manager 的程序內 Registry。
/// Registry 同時維護 GUID、正規化 Base URI、Admission Namespace 與 Lease Namespace 的反向索引，
/// 任何部分碰撞或跨 Organization Namespace 重用都會在建立新 Manager 前 fail closed。
/// </summary>
public interface IOrganizationAdmissionRegistry : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// 取得或共用符合完整 Canonical Identity 與 Capacity Digest 的 Admission Manager Registration。
    /// 若 Plan 與既有反向索引衝突，方法會同步拋出 <see cref="InvalidOperationException"/>，且不建立任何新資源。
    /// </summary>
    /// <param name="plan">已驗證且不含秘密值的 Organization Admission Plan。</param>
    /// <returns>由呼叫端唯一擁有、必須確定性釋放的 Registration。</returns>
    IOrganizationAdmissionRegistration Acquire(OrganizationAdmissionPlan plan);

    /// <summary>
    /// 取得目前仍被至少一個 Profile Generation 引用的 Canonical Organization Entry 數量。
    /// 此值只供 readiness、測試與 bounded telemetry 使用，不代表目前 Request 或 Session 數。
    /// </summary>
    int EntryCount { get; }
}
