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
