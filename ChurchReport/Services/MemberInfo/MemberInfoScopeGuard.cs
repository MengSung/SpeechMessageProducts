// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class MemberInfoScopeGuard
// 主要成員：IsContactAllowed
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoScopeGuard
    {
        public static bool IsContactAllowed(
            string access,
            IReadOnlyCollection<string> shepherdContactIds,
            string requestedContactId)
        {
            if (string.IsNullOrWhiteSpace(requestedContactId))
            {
                return false;
            }

            if (string.Equals(access, MemberInfoAccess.Church, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(access, MemberInfoAccess.ShepherdList, StringComparison.Ordinal))
            {
                return false;
            }

            if (shepherdContactIds == null || shepherdContactIds.Count == 0)
            {
                return false;
            }

            foreach (var id in shepherdContactIds)
            {
                if (string.Equals(id, requestedContactId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
