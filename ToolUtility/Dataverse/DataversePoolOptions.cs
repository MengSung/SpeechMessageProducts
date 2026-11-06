/*
 * 檔案整體說明
 *
 * 目的與定位：Keyed bounded pool 的容量、逾時、閒置回收與健康檢查設定。 設定物件本身不擁有連線；連線的最長生命週期由 pool 的 shutdown 或淘汰路徑決定。
 *
 * 主要功能與流程：
 * 本檔宣告 DataversePoolOptions。
 * - idle client 超過此時間且池高於 MinSize 時會被淘汰。
 * - idle client 超過此時間未驗證時，下一次出借前執行 WhoAmI 健康檢查。
 * 主要操作入口包含 Validate。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs、ToolUtility.Dataverse.Tests/DataverseTraceTests.cs、
 * ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs、ToolUtility.Dataverse.Tests/RunCServiceGraphTests.cs、
 * ToolUtility/Dataverse/BoundedClientPool.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Keyed bounded pool 的容量、逾時、閒置回收與健康檢查設定。
/// 設定物件本身不擁有連線；連線的最長生命週期由 pool 的 shutdown 或淘汰路徑決定。
/// </summary>
public sealed class DataversePoolOptions
{
    /// <summary>每個子池在首次使用時預熱的最小 client 數量。</summary>
    public int MinSize { get; set; } = 3;

    /// <summary>每個子池允許同時租借的最大 client 數量。</summary>
    public int MaxN { get; set; } = 20;

    /// <summary>等待可用 client 的最長時間。</summary>
    public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>idle client 超過此時間且池高於 MinSize 時會被淘汰。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>idle client 超過此時間未驗證時，下一次出借前執行 WhoAmI 健康檢查。</summary>
    public TimeSpan HealthInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>驗證設定可供 pool 在建構時 fail fast。</summary>
    public void Validate()
    {
        if (MinSize < 1)
            throw new ArgumentOutOfRangeException(nameof(MinSize), "MinSize 必須至少為 1。 ");
        if (MaxN < MinSize)
            throw new ArgumentOutOfRangeException(nameof(MaxN), "MaxN 必須大於或等於 MinSize。 ");
        if (AcquireTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(AcquireTimeout));
        if (IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeout));
        if (HealthInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HealthInterval));
    }
}
