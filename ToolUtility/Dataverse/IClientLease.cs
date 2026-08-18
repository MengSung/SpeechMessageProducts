using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 一次性 client 租借。Dispose 只歸還 lease，不直接銷毀長生命週期的 pooled client；
/// client 的最終 Dispose 只由其建立者 pool 決定。
/// </summary>
public interface IClientLease : IDisposable
{
    /// <summary>取得本次租借的組織服務代理。</summary>
    IOrganizationService Service { get; }

    /// <summary>將本次租借標記為不可重用，歸還時由 pool 淘汰其 client。</summary>
    void MarkFaulted();
}
