/*
檔案整體說明

目的與定位：定義 GoodShepherd.Core 會籍功能使用的穩定角色識別，供權限判斷、設定與
文字目錄使用，不以中文標籤或教會自訂名稱作為程式身份。

主要功能與流程：列舉值對應固定 Role.* 資源鍵；OptionSet 整數與角色的實際對照由 RoleCatalog
依 D365 contact.customertypecode 的可信 metadata 建立。未知選項不會轉成任一角色。

使用邊界與維護：本檔只宣告純 enum，不執行查詢、不持有使用者／教會資料、不建立快取、
背景工作或可 Dispose 資源。列舉成員是產品內穩定代碼，新增角色須同時更新資源與
RoleCatalog，不能重用顯示文字或選項值充當列舉身份。

失敗處理與驗證：此檔沒有執行期失敗路徑；RoleCatalog 對缺少或未知 metadata 採 fail-closed。
由 GoodShepherd.Core.Tests 雙目標測試驗證代碼、metadata 與資源的一致性。
*/
namespace SpeechMessageProducts.GoodShepherd.Core.Members;

/// <summary>會籍角色的穩定產品識別；顯示文字及 D365 選項整數由其他明確資料來源決定。</summary>
public enum RoleCode
{
    /// <summary>牧者與師母角色。</summary>
    PastorCouple,
    /// <summary>區牧角色。</summary>
    ZoneShepherd,
    /// <summary>小區長角色。</summary>
    ZoneLeader,
    /// <summary>小組長角色。</summary>
    GroupLeader,
    /// <summary>副小組長角色。</summary>
    AssistantGroupLeader,
    /// <summary>核心同工角色。</summary>
    CoreWorker,
    /// <summary>小組組員角色。</summary>
    Member,
    /// <summary>未入組角色。</summary>
    NotInGroup,
    /// <summary>新朋友角色。</summary>
    Newcomer,
    /// <summary>外教會角色。</summary>
    OtherChurch,
    /// <summary>結案角色。</summary>
    Closed
}
