/*
 * 檔案整體說明
 *
 * 目的與定位：將關係目標與角色配對轉成會友詳細頁／網格共用文字，保留來源順序並處理空角色及重複配對；此處只整理顯示文字，不合併不同會友的資料列。
 *
 * 主要功能與流程：
 * 本檔宣告 RelationGoalFormatter。
 * 主要操作入口包含 Format。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace ChurchReport.Services.MemberInfo
{
    public static class RelationGoalFormatter
    {
        public static string Format(
            IEnumerable<(string Role, string TargetName)>? items)
        {
            if (items == null)
            {
                return string.Empty;
            }

            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var role = (item.Role ?? string.Empty).Trim();
                var targetName = (item.TargetName ?? string.Empty).Trim();
                if (role.Length == 0 && targetName.Length == 0)
                {
                    continue;
                }

                if (!seen.Add(role + "\u001f" + targetName))
                {
                    continue;
                }

                values.Add(role.Length == 0 ? targetName : role + ": " + targetName);
            }

            return string.Join("、", values);
        }
    }
}
