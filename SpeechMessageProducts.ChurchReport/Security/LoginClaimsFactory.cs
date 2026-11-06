/*
 * 檔案整體說明
 *
 * 目的與定位：將已完成認證的登入資料轉成ClaimsPrincipal，保留流程必要識別而排除密碼等憑證；建立claims不能替代前置認證，產生的principal必須由目前登入流程持有。
 *
 * 主要功能與流程：
 * 本檔宣告 LoginClaimsFactory。
 * 主要操作入口包含 Build。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChurchReport.Security
{
    public static class LoginClaimsFactory
    {
        public const string ContactIdClaim = "church:contactId";
        public const string AccountClaim = "church:account";
        public const string PasswordKeyClaim = "church:pwdkey";
        public const string LoginTypeClaim = "church:loginType";

        public static ClaimsPrincipal Build(string contactId, string account, string passwordKey, string loginType)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, contactId ?? string.Empty),
                new Claim(ContactIdClaim, contactId ?? string.Empty),
                new Claim(AccountClaim, account ?? string.Empty),
                new Claim(PasswordKeyClaim, passwordKey ?? string.Empty),
                new Claim(LoginTypeClaim, loginType ?? string.Empty),
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }
    }
}
