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
            "<path d='M8 64c1.5-17.2 10.9-29.6 24-29.6S54.5 46.8 56 64H8z'/>" +
            "</g></svg>";

        /// <summary>Male default avatar.</summary>
        public const string Male =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#5b8fd0'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<circle cx='32' cy='23.8' r='10.4'/>" +
            "<path d='M7.5 64c1.6-17.6 11.2-29.8 24.5-29.8S54.9 46.4 56.5 64h-49z'/>" +
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
            "<circle cx='32' cy='24.2' r='9.7' fill='#ffffff'/>" +
            "<path d='M8 64c1.5-17.4 10.9-30.1 24-30.1S54.5 46.6 56 64H8z' fill='#ffffff'/>" +
            "</g></svg>";
    }
}
