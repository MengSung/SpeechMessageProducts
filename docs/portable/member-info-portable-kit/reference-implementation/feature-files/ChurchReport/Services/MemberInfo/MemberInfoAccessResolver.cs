using System;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoAccessResolver
    {
        /// <summary>
        /// 將 CRM 職稱文字與登入類型收斂成會友資訊的兩級權限。職稱可能包含多個角色片段，故採 Contains；
        /// 「檢視全教會照片資訊」是授權用特殊職稱，語意上與牧師傳道／牧養主任相同，皆可檢視全教會在籍資料。
        /// 未命中特殊職稱時才以精確的「小組長」登入類型降階到牧養名單，其他身分一律不授權。
        /// </summary>
        public static string Resolve(string churchJobTitle, string loginType)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();

            if (jobTitle.Contains("牧師傳道") || jobTitle.Contains("牧養主任") || jobTitle.Contains("檢視全教會照片資訊"))
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
