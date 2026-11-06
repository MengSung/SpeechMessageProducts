/*
 * 檔案整體說明
 *
 * 目的與定位：Dataverse 連線管理器的唯一應用程式入口。呼叫端只能取得 lease 與 metrics， 不會接觸 raw client 或池內可變集合；Singleton 的生命週期由 DI 管理至應用程式關閉。
 *
 * 主要功能與流程：
 * 本檔宣告 IDataverseConnectionManager。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/DataverseTraceTests.cs、ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs、
 * ToolUtility.Dataverse.Tests/RunCServiceGraphTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs、
 * ToolUtility.Tests/DependencyInjection/ProductRegistrationTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Threading;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Dataverse 連線管理器的唯一應用程式入口。呼叫端只能取得 lease 與 metrics，
/// 不會接觸 raw client 或池內可變集合；Singleton 的生命週期由 DI 管理至應用程式關閉。
/// </summary>
public interface IDataverseConnectionManager : IDisposable
{
    /// <summary>依組合根設定的 Pool Key 取得一條短命租約。</summary>
    IClientLease Acquire(CancellationToken cancellationToken = default);

    /// <summary>取得所有 keyed 子池的 metrics 快照。</summary>
    DataversePoolMetrics GetMetrics();
}
