namespace ChurchReport.Security
{
    public static class LocalReturnUrl
    {
        public static bool IsLocal(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (url[0] == '/')
            {
                return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
            }

            return url.Length > 1 && url[0] == '~' && url[1] == '/';
        }
    }
}
