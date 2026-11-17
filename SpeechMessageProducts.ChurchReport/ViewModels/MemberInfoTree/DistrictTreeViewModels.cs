/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、
 * SpiritualIdentity、MembershipStatus 均為 OptionSet 顯示文字，而非可直接回寫 CRM 的整數值。；
 * MemberInfoTreeSearchResultViewModel：MatchingListIds 與 HasUngrouped 保留為相容欄位及節點命中 metadata；現行前端主要使用 Rows
 * 直接替換搜尋結果表格，不需按每個命中節點再發請求。Rows 必須由 allowed ids 過濾並去重。
 *
 * 主要功能與流程：
 * 本檔宣告 GroupNodeViewModel、DistrictNodeViewModel、UngroupedNodeViewModel、DistrictTreeViewModel、
 * GroupMemberRowViewModel、MemberInfoTreeSearchResultViewModel。
 * - contact.customertypecode 在 Dynamics 客製化選項集合中的零起始順位。 這不是 OptionSet 原始整數值；null 代表 metadata 未列出或欄位未填。
 * - 表示 CRM 欄位實際有 OptionSet 值，用來區分 metadata 未知舊值與真正空白。
 * - 顯示給使用者的本地化會員身份文字；排序不得使用這個文字欄位。
 * 資料／選項欄位包含 GroupNodeViewModel、ListId、GroupName、LeaderName、GroupTime、GroupPlace、MemberCount、DistrictNodeViewModel、
 * RaceLeaderKey、AreaName、RaceLeaderName、GroupCount、Groups、UngroupedNodeViewModel、DistrictTreeViewModel、Districts，
 * 名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs 追查目前工作樹中的合作／使用位置。
 * P6-07公開邊界：DistrictNodeViewModel、DistrictTreeViewModel、GroupMemberRowViewModel、GroupNodeViewModel、MemberInfoTreeSearchResultViewModel、UngroupedNodeViewModel改為產品內部宣告；外部輸出與既有資源清理契約不變。
 */
using System;
using System.Collections.Generic;

namespace ChurchReport.ViewModels.MemberInfoTree
{
    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    internal class GroupNodeViewModel
    {
        public string ListId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string GroupTime { get; set; } = string.Empty;
        public string GroupPlace { get; set; } = string.Empty;
        public int MemberCount { get; set; }
    }

    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    internal class DistrictNodeViewModel
    {
        public string RaceLeaderKey { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public string RaceLeaderName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int GroupCount { get; set; }
        public List<GroupNodeViewModel> Groups { get; set; } = new List<GroupNodeViewModel>();
    }

    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    internal class UngroupedNodeViewModel
    {
        public int MemberCount { get; set; }
    }

    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    /// <summary>本檔整合下列相關契約：GroupMemberRowViewModel：小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定； Gender、。
    /// P6-07將本宣告限於主機內部；仍由既有呼叫端組裝及消費，不變更資料形狀、流程或資源owner。</summary>
    internal class DistrictTreeViewModel
    {
        public List<DistrictNodeViewModel> Districts { get; set; } = new List<DistrictNodeViewModel>();
        public UngroupedNodeViewModel? Ungrouped { get; set; }
        public string Scope { get; set; } = string.Empty;
    }

    /// <summary>
    /// 小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定；
    /// Gender、SpiritualIdentity、MembershipStatus 均為 OptionSet 顯示文字，而非可直接回寫 CRM 的整數值。
    /// </summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal class GroupMemberRowViewModel
    {
        public string ContactId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string SpiritualIdentity { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// contact.customertypecode 在 Dynamics 客製化選項集合中的零起始順位。
        /// 這不是 OptionSet 原始整數值；null 代表 metadata 未列出或欄位未填。
        /// </summary>
        public int? MembershipStatusOrder { get; set; }
        /// <summary>
        /// 表示 CRM 欄位實際有 OptionSet 值，用來區分 metadata 未知舊值與真正空白。
        /// </summary>
        public bool HasMembershipStatusValue { get; set; }
        /// <summary>
        /// 顯示給使用者的本地化會員身份文字；排序不得使用這個文字欄位。
        /// </summary>
        public string MembershipStatus { get; set; } = string.Empty;
        public string RelationGoals { get; set; } = string.Empty;
    }

    /// <summary>
    /// MatchingListIds 與 HasUngrouped 保留為相容欄位及節點命中 metadata；現行前端主要使用 Rows
    /// 直接替換搜尋結果表格，不需按每個命中節點再發請求。Rows 必須由 allowed ids 過濾並去重。
    /// </summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal class MemberInfoTreeSearchResultViewModel
    {
        public List<string> MatchingListIds { get; set; } = new List<string>();
        public bool HasUngrouped { get; set; }
        public List<GroupMemberRowViewModel> Rows { get; set; } = new List<GroupMemberRowViewModel>();
    }
}
