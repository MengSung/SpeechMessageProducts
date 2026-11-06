/*
 * 檔案整體說明
 *
 * 目的與定位：聯絡人查詢與舊登入入口的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host
 * 已完成安全純化。
 *
 * 主要功能與流程：
 * 本檔宣告 IContactService。
 * - 依舊系統客戶編號查啟用聯絡人；查無資料回 null，不把外部編號當資料庫列身份或授權。
 * - 依 LINE 綁定值查第一筆啟用聯絡人；查無回 null，值必須來自已驗證流程而非身分授權替代物。
 * - 依 LINE 綁定值查集合，保留不同資料庫 ID 的合法記錄；caller 須處理 SDK 分頁與發布驗證。
 * - 依顯示姓名查集合而不按姓名合併；姓名不能替代資料庫 ID 或登入授權。
 * 資料／選項欄位包含 IContactService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/ContactOperations/ContactDataService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 聯絡人查詢與舊登入入口的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。
    /// 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 已完成安全純化。
    /// </summary>
    /// <remarks>
    /// 呼叫端先以伺服器驗證使用者／租戶／教會與資料範圍；任何名稱、路由或 ID 參數均不能建立授權。
    /// 可變 Entity、集合及 ArrayList 僅屬單次操作，caller 不得跨 scope 共用；實作須在發布前驗正式 ID 及完整結果。
    /// SDK 由 host 借入並擁有 timeout、drain 與 Dispose；本契約不保存 request、Session、credential 或執行資源。
    /// 真正故障由實作保留原 Exception 並交最終 host 共用告警；預期查無與明確 caller 取消不偽裝成系統錯誤。
    /// </remarks>
    public interface IContactService
    {
        /// <summary>
        /// 依舊系統客戶編號查啟用聯絡人；查無資料回 null，不把外部編號當資料庫列身份或授權。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByContactId(string contactId);

        /// <summary>
        /// 依 LINE 綁定值查第一筆啟用聯絡人；查無回 null，值必須來自已驗證流程而非身分授權替代物。
        /// </summary>
        /// <param name="lineId">已驗證流程提供的 LINE 綁定值，不得輸出至診斷。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByLineId(string lineId);

        /// <summary>
        /// 依 LINE 綁定值查集合，保留不同資料庫 ID 的合法記錄；caller 須處理 SDK 分頁與發布驗證。
        /// </summary>
        /// <param name="lineId">已驗證流程提供的 LINE 綁定值，不得輸出至診斷。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveCollectionByLineId(string lineId);

        /// <summary>
        /// 依顯示姓名查集合而不按姓名合併；姓名不能替代資料庫 ID 或登入授權。
        /// </summary>
        /// <param name="contactFullName">用於查詢的顯示姓名，不是唯一身份。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveCollectionByName(string contactFullName);

        /// <summary>
        /// 沿用帳號及密碼核對入口；不符時回 null，密碼只活於本次核對且不得進入日誌或快取鍵。
        /// </summary>
        /// <param name="accountNumber">僅供此次核對的帳號，不可據此快取已驗證登入結果。</param>
        /// <param name="password">僅供此次核對的機密，不可記錄、保留或送至通知。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByAccountNumber(string accountNumber, string password);

        /// <summary>
        /// 沿用舊聯絡資訊顯示格式；輸出含個資，只能在原已授權流程短暫使用，不可送入診斷或共用快取。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        string GetContactInfoByContactId(string contactId);

        /// <summary>
        /// 依舊系統客戶編號查啟用聯絡人；查無資料回 null，不把外部編號當資料庫列身份或授權。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="count">本次舊查詢回報的筆數；只屬此次 caller，不得跨請求共用。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByContactId(IOrganizationService? externalService, string contactId, ref int count);

        /// <summary>
        /// 沿用姓名查詢及舊聯絡資訊格式；顯示結果不是唯一身份，也不得被當登入成功證明。
        /// </summary>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        string GetContactInfoByFullName(string fullName);

        /// <summary>
        /// 依姓名取得舊查詢的第一筆或 null；合法同名資料應使用集合入口，不以第一筆建立授權。
        /// </summary>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByFullName(string fullName);

        /// <summary>
        /// 依姓名取得舊查詢的第一筆或 null；合法同名資料應使用集合入口，不以第一筆建立授權。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByFullName(IOrganizationService? externalService, string fullName);

        /// <summary>
        /// 沿用姓名查詢及舊聯絡資訊格式；顯示結果不是唯一身份，也不得被當登入成功證明。
        /// </summary>
        /// <param name="externalService">借入的已授權 SDK；caller 擁有生命週期，null 保留舊查無資料語義。</param>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        string GetContactInfoByFullName(IOrganizationService? externalService, string fullName);

        /// <summary>
        /// 沿用個人識別欄位查詢；輸入及結果均不得寫入診斷，caller 負責原授權範圍及分頁。
        /// </summary>
        /// <param name="nationId">敏感個人識別條件，僅於此次操作使用，不得診斷輸出。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveCollectionByNationId(string nationId);

        /// <summary>
        /// 保留歷史登入結果字串（成功為資料庫 ID，失敗為舊業務文案）；此介面不簽發票證也不建立 Session。
        /// </summary>
        /// <param name="accountNumber">僅供此次核對的帳號，不可據此快取已驗證登入結果。</param>
        /// <param name="password">僅供此次核對的機密，不可記錄、保留或送至通知。</param>
        /// <returns>舊格式的操作結果；只屬此次 caller，不能把字串或集合物件當授權或共用快照。</returns>
        string AccountLogin(string accountNumber, string password);

        /// <summary>
        /// 只檢查帳號記錄是否存在，無資料回 null；記錄存在不能取代密碼核對或伺服器授權。
        /// </summary>
        /// <param name="accountNumber">僅供此次核對的帳號，不可據此快取已驗證登入結果。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveAccountEntity(string accountNumber);

        /// <summary>
        /// 以姓名與手機共同查第一筆或 null；兩者仍是查詢值，不是可信登入身份或去重鍵。
        /// </summary>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <param name="mobileNumber">手機條件；非唯一身份或授權。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByFullNameAndMobile(string fullName, string mobileNumber);

        /// <summary>
        /// 回傳同名記錄集合，資料庫 ID 不同者均須保留；caller 不得按姓名靜默去重。
        /// </summary>
        /// <param name="fullName">姓名條件；不同正式 ID 的同名資料不得合併。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveCollectionByFullName(string fullName);

        /// <summary>
        /// 保留舊命名別名，實際回單筆或 null；不能把方法名稱誤解為完整集合。
        /// </summary>
        /// <param name="lineId">已驗證流程提供的 LINE 綁定值，不得輸出至診斷。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveByLineIdForCollection(string lineId);

        /// <summary>
        /// 保留舊拼字及多條件認獻聯絡人查詢；空條件與 OR 語義依既有實作，所有值必須安全編碼且不可診斷輸出。
        /// </summary>
        /// <param name="dedicationNumber">認獻編號條件，須安全編碼且不作教會權限。</param>
        /// <param name="contactName">聯絡人顯示名稱，只是查詢值或提示。</param>
        /// <param name="homePhone">市話條件，不可記錄。</param>
        /// <param name="mobile">手機條件，不可記錄。</param>
        /// <param name="nationId">敏感個人識別條件，僅於此次操作使用，不得診斷輸出。</param>
        /// <param name="lastSixDigit">舊地址後六碼條件，不可記錄。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName, string homePhone, string mobile, string nationId, string lastSixDigit);

        /// <summary>
        /// 保留舊拼字及奉獻編號前綴查詢；前綴是資料條件，不是租戶／教會授權，caller 負責完整頁發布。
        /// </summary>
        /// <param name="dedicationStartNumber">舊編號前綴，不是教會或租戶身份。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber);

    }
}
