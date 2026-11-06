/*
 * 檔案整體說明
 *
 * 目的與定位：Bounded pool 的不可變快照。快照只包含計數，不保留 client、租約或使用者資料， 因此可安全由診斷端點讀取且不延長任何連線生命週期。
 *
 * 主要功能與流程：
 * 本檔宣告 DataversePoolMetrics。
 * - 累計因故障、健康檢查、閒置或 shutdown 而淘汰的 client 數。
 * 資料／選項欄位包含 DataversePoolMetrics、Idle、Leased、Faulted、Waiting、AcquireTimeouts、Created、Discarded、TotalAcquires、
 * TotalReleases、SubPoolCount，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/DataverseTraceTests.cs、ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs、
 * ToolUtility/Dataverse/BoundedClientPool.cs、ToolUtility/Dataverse/DataverseConnectionManager.cs、
 * ToolUtility/Dataverse/IBoundedClientPool.cs 追查目前工作樹中的合作／使用位置。
 */
namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Bounded pool 的不可變快照。快照只包含計數，不保留 client、租約或使用者資料，
/// 因此可安全由診斷端點讀取且不延長任何連線生命週期。
/// </summary>
public sealed class DataversePoolMetrics
{
    /// <summary>目前 idle client 數。</summary>
    public int Idle { get; init; }

    /// <summary>目前 leased client 數。</summary>
    public int Leased { get; init; }

    /// <summary>累計被標記為故障的 client 數。</summary>
    public long Faulted { get; init; }

    /// <summary>目前等待租借的請求數。</summary>
    public int Waiting { get; init; }

    /// <summary>累計 Acquire 逾時次數。</summary>
    public long AcquireTimeouts { get; init; }

    /// <summary>累計建立的 client 數。</summary>
    public long Created { get; init; }

    /// <summary>累計因故障、健康檢查、閒置或 shutdown 而淘汰的 client 數。</summary>
    public long Discarded { get; init; }

    /// <summary>累計成功取得租約次數。</summary>
    public long TotalAcquires { get; init; }

    /// <summary>累計成功歸還租約次數。</summary>
    public long TotalReleases { get; init; }

    /// <summary>目前 keyed 子池數。</summary>
    public int SubPoolCount { get; init; }
}
