/*
 * 檔案整體說明
 *
 * 目的與定位：判斷登入後返回網址是否為站內相對路徑，拒絕外部host、雙斜線及反斜線等可造成開放轉址的形式；只回傳分類結果，不自行重新導向或保存登入資料。
 *
 * 主要功能與流程：
 * 本檔宣告 LocalReturnUrl。
 * 主要操作入口包含 IsLocal。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOA
 * uth.cs 追查目前工作樹中的合作／使用位置。
 */
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
