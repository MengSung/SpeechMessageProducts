/*
 * 檔案整體說明
 *
 * 目的與定位：一次性 client 租借。Dispose 只歸還 lease，不直接銷毀長生命週期的 pooled client； client 的最終 Dispose 只由其建立者 pool 決定。
 *
 * 主要功能與流程：
 * 本檔宣告 IClientLease。
 * - 將本次租借標記為不可重用，歸還時由 pool 淘汰其 client。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/Localization/DataverseChurchTextOverrideServiceFactoryTests.cs、
 * ToolUtility/Dataverse/BoundedClientPool.cs、ToolUtility/Dataverse/DataverseConnectionManager.cs、
 * ToolUtility/Dataverse/DataverseGateway.cs、ToolUtility/Dataverse/IBoundedClientPool.cs 追查目前工作樹中的合作／使用位置。
 */
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
