using System;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoAccessResolver
    {
        public static string Resolve(string churchJobTitle, string loginType)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();

            if (jobTitle.Contains("牧師傳道") || jobTitle.Contains("牧養主任"))
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
