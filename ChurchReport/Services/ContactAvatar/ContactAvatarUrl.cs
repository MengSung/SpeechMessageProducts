namespace ChurchReport.Services.ContactAvatar
{
    public static class ContactAvatarUrl
    {
        public const string LinePictureUrlAttribute = "new_line_picture_url";

        public static string NormalizeHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var trimmed = url.Trim();
            if (!System.Uri.TryCreate(trimmed, System.UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri.Scheme == System.Uri.UriSchemeHttps || uri.Scheme == System.Uri.UriSchemeHttp
                ? trimmed
                : null;
        }
    }
}
