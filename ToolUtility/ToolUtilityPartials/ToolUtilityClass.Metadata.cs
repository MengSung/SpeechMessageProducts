/*
 * 檔案整體說明
 *
 * 目的與定位：將ToolUtilityClass舊metadata入口委派給Facade，保持欄位名稱查詢的API相容性；主partial負責連線與Dispose，此檔不可建立第二個SDK或自行保存查詢結果。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * 主要操作入口包含 GetEntityAttributeNames、GetEntityAttributeTypes、GetEntityAttributeMetadata。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace
{
    public partial class ToolUtilityClass
    {
        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeNames
        /// </summary>
        public HashSet<string> GetEntityAttributeNames(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeNames(entityLogicalName) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeTypes
        /// </summary>
        public Dictionary<string, string> GetEntityAttributeTypes(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeTypes(entityLogicalName) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeMetadata
        /// </summary>
        public Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata> GetEntityAttributeMetadata(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeMetadata(entityLogicalName) ?? new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
