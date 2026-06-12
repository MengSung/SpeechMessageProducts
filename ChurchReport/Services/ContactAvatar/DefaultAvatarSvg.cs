namespace ChurchReport.Services.ContactAvatar
{
    /// <summary>
    /// 連絡人無照片時的預設大頭貼（上半身人形剪影 SVG）。
    /// 依 CRM contact.gendercode 區分：1=男性、2=女性、其餘=中性。
    /// 取代舊的「??」「人」文字圖示。
    /// </summary>
    public static class DefaultAvatarSvg
    {
        /// <summary>依性別代碼取得對應剪影 SVG（gendercode：1=男, 2=女, null/其他=中性）。</summary>
        public static string ForGender(int? genderCode)
        {
            if (genderCode == 1) return Male;
            if (genderCode == 2) return Female;
            return Neutral;
        }

        /// <summary>中性：灰底白色半身剪影。</summary>
        public const string Neutral =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#aeb6bf'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<circle cx='32' cy='25' r='11'/>" +
            "<ellipse cx='32' cy='57' rx='19' ry='17'/>" +
            "</g></svg>";

        /// <summary>男性：藍底白色半身剪影（肩較寬、短髮）。</summary>
        public const string Male =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#5b8fd0'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<circle cx='32' cy='24' r='11'/>" +
            "<ellipse cx='32' cy='58' rx='22' ry='18'/>" +
            "</g></svg>";

        /// <summary>女性：玫瑰底白色半身剪影（長髮、肩較窄）。</summary>
        public const string Female =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64' width='64' height='64'>" +
            "<defs><clipPath id='clp'><circle cx='32' cy='32' r='32'/></clipPath></defs>" +
            "<circle cx='32' cy='32' r='32' fill='#df8cb0'/>" +
            "<g clip-path='url(#clp)' fill='#ffffff'>" +
            "<path d='M16 30 a16 16 0 0 1 32 0 v12 a4 4 0 0 1-4 4 h-4 v-16 a8 8 0 0 0-16 0 v16 h-4 a4 4 0 0 1-4-4 z'/>" +
            "<circle cx='32' cy='26' r='10'/>" +
            "<ellipse cx='32' cy='60' rx='17' ry='16'/>" +
            "</g></svg>";
    }
}
