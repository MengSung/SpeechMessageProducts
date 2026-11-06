/*
 * 檔案整體說明
 *
 * 目的與定位：將舊推播需求轉成CRM的待送LINE訊息實體，透過注入的資料操作建立記錄；記錄建立與LINE實際送達是不同階段，不能把Create成功宣稱為已送達。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessageService。
 * 主要操作入口包含 CreatePushMessage。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs、
 * ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs、ToolUtility/Core/ToolUtilityFacade.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.LineMessaging
{
    public class LineMessageService : ILineMessageService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public LineMessageService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ;
        }

        public void CreatePushMessage(string userId, string subject, string message)
        {
            // Simplified: create a Message entity in CRM
            var entity = new Microsoft.Xrm.Sdk.Entity("linemessage")
            {
                ["userid"] = userId,
                ["subject"] = subject,
                ["message"] = message
            };

            _organizationService.Create(entity);
        }
    }
}
