/*
 * 檔案整體說明
 *
 * 目的與定位：在已解析的權限範圍內檢查請求的會友或名單ID是否可見，拒絕空ID、未知權限及不在可見集合內的請求；它是第二道範圍檢查，不能以caller自己提供的可見集合當權威。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoScopeGuard。
 * 主要操作入口包含 IsContactAllowed、IsListAllowed。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
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

        public static bool IsListAllowed(
            string access,
            IReadOnlyCollection<string> visibleListIds,
            string requestedListId)
        {
            if (string.IsNullOrWhiteSpace(requestedListId) ||
                (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList) ||
                visibleListIds == null ||
                visibleListIds.Count == 0)
            {
                return false;
            }

            foreach (var visibleListId in visibleListIds)
            {
                if (string.Equals(visibleListId, requestedListId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
