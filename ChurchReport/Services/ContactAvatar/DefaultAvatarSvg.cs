namespace ChurchReport.Services.ContactAvatar
{
    /// <summary>
    /// Gender-specific default contact avatars for rows without a CRM entity image.
    /// Dynamics can store gendercode as the standard 1/2 values, while this project
    /// also writes 200000/200001 from GalleryViewModel.
    /// </summary>
    public static class DefaultAvatarSvg
    {
        /// <summary>Returns male, female, or neutral SVG based on CRM contact.gendercode.</summary>
        public static string ForGender(int? genderCode)
        {
            if (genderCode == 1 || genderCode == 200000) return Male;
            if (genderCode == 2 || genderCode == 200001) return Female;
            return Neutral;
        }

        /// <summary>Neutral default avatar.</summary>
        public const string Neutral =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#aeb6bf'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<circle cx='32' cy='24.2' r='10.2'/>" +
            "<path d='M25 34.2h14v7H25z'/>" +
            "<path d='M8 64c1.5-17.2 10.9-27 24-27s22.5 9.8 24 27H8z'/>" +
            "</g></svg>";

        /// <summary>Male default avatar.</summary>
        public const string Male =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#5b8fd0'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<circle cx='32' cy='23.8' r='10.4'/>" +
            "<path d='M25 34h14v7.3H25z'/>" +
            "<path d='M7.5 64c1.6-17.6 11.2-27.4 24.5-27.4S54.9 46.4 56.5 64h-49z'/>" +
            "</g></svg>";

        /// <summary>Female default avatar.</summary>
        public const string Female =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs>" +
            "<clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath>" +
            "<linearGradient id='fbg' x1='14' y1='8' x2='52' y2='58' gradientUnits='userSpaceOnUse'>" +
            "<stop offset='0' stop-color='#ee8fba'/>" +
            "<stop offset='1' stop-color='#c95791'/>" +
            "</linearGradient>" +
            "</defs>" +
            "<circle cx='32' cy='32' r='32' fill='url(#fbg)'/>" +
            "<g clip-path='url(#clp)'>" +
            "<path d='M17.5 33.6c0-12 6-19.6 14.5-19.6s14.5 7.6 14.5 19.6c0 8.7-3.8 16.5-9.7 20.2h-9.6c-5.9-3.7-9.7-11.5-9.7-20.2z' fill='#ffffff' opacity='.5'/>" +
            "<circle cx='32' cy='24.2' r='9.7' fill='#ffffff'/>" +
            "<path d='M24.7 34.1h14.6v7.3H24.7z' fill='#ffffff'/>" +
            "<path d='M8 64c1.5-17.4 10.9-27.3 24-27.3S54.5 46.6 56 64H8z' fill='#ffffff'/>" +
            "</g></svg>";
    }
}
