/*
 * 檔案整體說明
 *
 * 目的與定位：Session 初始化服務介面
 *
 * 主要功能與流程：
 * 本檔宣告 ISessionInitializationService。
 * 資料／選項欄位包含 ISessionInitializationService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// Session 初始化服務介面
    /// </summary>
    public interface ISessionInitializationService
    {
        /// <summary>
        /// 初始化使用者 Session
        /// </summary>
        Task<SessionData> InitializeSessionAsync(
            Entity loginContact,
            LoginType loginType,
            string account,
            string password);

        /// <summary>
        /// 清除 Session
        /// </summary>
        void ClearSession();
    }
}
