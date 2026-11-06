/*
 * 檔案整體說明
 *
 * 目的與定位：認證服務介面
 *
 * 主要功能與流程：
 * 本檔宣告 IAuthenticationService。
 * 資料／選項欄位包含 IAuthenticationService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using ChurchReport.Models.Authentication;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// 認證服務介面
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// 驗證帳號密碼
        /// </summary>
        Task<AuthResult> ValidateCredentialsAsync(string account, string password);

        /// <summary>
        /// 驗證 LINE User ID
        /// </summary>
        Task<AuthResult> ValidateLineIdAsync(string lineUserId);
    }
}
