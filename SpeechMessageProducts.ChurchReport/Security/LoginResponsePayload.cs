/*
 * 檔案整體說明
 *
 * 目的與定位：定義前端登入回應及其factory，保留既有欄位名稱與空名單的相容形狀，同時限制回傳資料不含登入憑證；回應是目前請求的結果，不作全域身分快取。
 *
 * 主要功能與流程：
 * 本檔宣告 LoginResponsePayload、LoginResponseFactory。
 * 主要操作入口包含 LoginResponsePayload、Build。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LoginResponseFactoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace ChurchReport.Security
{
    public sealed record LoginResponsePayload(
        string DisplayViewType,
        string ActiveListId,
        string message,
        string fullname);

    public static class LoginResponseFactory
    {
        public static LoginResponsePayload Build(string displayViewType, string activeListId, string fullName)
        {
            var safeFullName = fullName ?? string.Empty;

            return new LoginResponsePayload(
                displayViewType ?? string.Empty,
                activeListId ?? string.Empty,
                $"登入 {safeFullName} 成功!",
                safeFullName);
        }
    }
}
