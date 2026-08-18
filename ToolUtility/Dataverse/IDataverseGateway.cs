using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Scoped、per-operation 的 Dataverse Gateway。每次最外層 Execute 取得一條 lease，
/// 巢狀 Execute 重用同一條 lease，最外層結束時才歸還，避免跨 request 持有 client。
/// </summary>
public interface IDataverseGateway
{
    /// <summary>執行不回傳值的 CRM 操作。</summary>
    void Execute(Action<IOrganizationService> work);

    /// <summary>執行並回傳 CRM 操作結果。</summary>
    T Execute<T>(Func<IOrganizationService, T> work);
}
