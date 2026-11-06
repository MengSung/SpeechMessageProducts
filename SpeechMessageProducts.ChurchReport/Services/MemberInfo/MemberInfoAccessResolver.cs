/*
 * 檔案整體說明
 *
 * 目的與定位：將伺服器取得的教會職稱與登入類型解析為全教會或牧養名單範圍；牧師傳道／牧養主任等角色優先，無符合條件者不授權，避免畫面參數自行升級存取。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoAccessResolver。
 * 主要操作入口包含 Resolve。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/ViewComponents/MemberInfoNavViewComponent.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoAccessResolver
    {
        public static string Resolve(string churchJobTitle, string loginType)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();

            if (jobTitle.Contains("牧師傳道") ||
                jobTitle.Contains("牧養主任") ||
                jobTitle.Contains("檢視全教會照片資訊"))
            {
                return MemberInfoAccess.Church;
            }

            if (string.Equals((loginType ?? string.Empty).Trim(), "小組長", StringComparison.Ordinal))
            {
                return MemberInfoAccess.ShepherdList;
            }

            return null;
        }
    }
}
