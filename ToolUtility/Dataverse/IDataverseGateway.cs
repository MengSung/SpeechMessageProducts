/*
 * 檔案整體說明
 *
 * 目的與定位：Scoped、per-operation 的 Dataverse Gateway。每次最外層 Execute 取得一條 lease， 巢狀 Execute 重用同一條 lease，最外層結束時才歸還，避免跨 
 * request 持有 client。
 *
 * 主要功能與流程：
 * 本檔宣告 IDataverseGateway。
 * 資料／選項欄位包含 IDataverseGateway，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/DataverseTraceTests.cs、ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs、
 * ToolUtility.Dataverse.Tests/RunCServiceGraphTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs、
 * ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs 追查目前工作樹中的合作／使用位置。
 */
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
