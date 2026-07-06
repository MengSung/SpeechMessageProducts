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
