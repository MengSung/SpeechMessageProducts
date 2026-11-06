/*
 * 檔案整體說明
 *
 * 目的與定位：舊host型別與兩個建構多載的相容adapter；所有資料操作只有ContactDataService一份實作。 舊cache鍵不含完整已驗證使用者／租戶／授權epoch，且Entity可被caller修改，
 * 因此不讀寫或保存cache。 logger亦僅為相容而不保存；host負責最終共用告警、SDK timeout與drain，adapter不擁有外部依賴。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactService。
 * - 保留舊無cache多載；借用原授權scope的SDK，Dispose只清參照、不接管連線所有權。
 * - 保留舊cache注入簽章但刻意停用不完整隔離鍵；不把帳密或登入結果存key。 本型別不擁有cache，禁止Dispose caller的cache；重新啟用前必須有完整伺服器授權邊界及不可變快照契約。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs、
 * ToolUtility.Tests/ContactOperations/ContactAuthenticationIsolationTests.cs、
 * ToolUtility.Tests/ContactOperations/ContactServiceTests.cs、ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Tests/DataOperations/ContactCharacterizationTests.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Caching;

namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 舊host型別與兩個建構多載的相容adapter；所有資料操作只有ContactDataService一份實作。
    /// 舊cache鍵不含完整已驗證使用者／租戶／授權epoch，且Entity可被caller修改，因此不讀寫或保存cache。
    /// logger亦僅為相容而不保存；host負責最終共用告警、SDK timeout與drain，adapter不擁有外部依賴。
    /// </summary>
    public class ContactService : ContactDataService
    {
        /// <summary>保留舊無cache多載；借用原授權scope的SDK，Dispose只清參照、不接管連線所有權。</summary>
        /// <param name="logger">相容參數、須非null；不保存或反射。</param>
        /// <param name="organizationService">host擁有的已授權SDK，先drain再由host釋放。</param>
        public ContactService(object logger, IOrganizationService organizationService)
            : base(logger, organizationService) { }

        /// <summary>
        /// 保留舊cache注入簽章但刻意停用不完整隔離鍵；不把帳密或登入結果存key。
        /// 本型別不擁有cache，禁止Dispose caller的cache；重新啟用前必須有完整伺服器授權邊界及不可變快照契約。
        /// </summary>
        /// <param name="logger">僅為相容的非null診斷參數，不保存。</param>
        /// <param name="organizationService">同一已授權scope的借入SDK。</param>
        /// <param name="cacheService">相容參數，可null；不使用、不保存、不處置。</param>
        public ContactService(object logger, IOrganizationService organizationService, ICrmCacheService? cacheService)
            : base(logger, organizationService) { }
    }
}
