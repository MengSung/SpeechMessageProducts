/*
 * 檔案整體說明
 *
 * 目的與定位：集中檢查聯絡人頭像網址是否為可使用的HTTP／HTTPS位址，供畫面選擇遠端照片或預設頭像；網址正常化不是身分驗證，也不在此下載或快取圖片。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactAvatarUrl。
 * 主要操作入口包含 NormalizeHttpUrl。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs 追查目前工作樹中的合作／使用位置。
 */
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
