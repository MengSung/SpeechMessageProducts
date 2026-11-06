/*
 * 檔案整體說明
 *
 * 目的與定位：以 Pool Key 分隔的 bounded client pool。應用程式只能取得 lease，不能取得 pool 內部集合。
 *
 * 主要功能與流程：
 * 本檔宣告 IBoundedClientPool。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility/Dataverse/BoundedClientPool.cs、ToolUtility/Dataverse/DataverseConnectionManager.cs、
 * ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Threading;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 以 Pool Key 分隔的 bounded client pool。應用程式只能取得 lease，不能取得 pool 內部集合。
/// </summary>
public interface IBoundedClientPool : IDisposable
{
    /// <summary>取得指定隔離鍵的 client 租約。</summary>
    IClientLease Acquire(DataverseConnectionKey key, CancellationToken cancellationToken = default);

    /// <summary>讀取目前 pool 的計數快照。</summary>
    DataversePoolMetrics GetMetrics();
}
