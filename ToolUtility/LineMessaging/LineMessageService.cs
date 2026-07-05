// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/LineMessaging/LineMessageService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessageService
// 主要成員：CreatePushMessage
// 引用命名空間：Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
