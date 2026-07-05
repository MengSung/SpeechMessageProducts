// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/ViewModels/MemberInfoDetailViewModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class MemberInfoDetailViewModel、class OptionItem、class RelationGoalItem
// 主要成員：ContactId、FullName、Phone、Address、MembershipStatus、SpiritualIdentity、AvatarSource、MembershipStatusValue、SpiritualIdentityValue、MembershipStatusOptions
// 引用命名空間：System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Collections.Generic;

namespace ChurchReport.ViewModels
{
    public class MemberInfoDetailViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string MembershipStatus { get; set; }
        public string SpiritualIdentity { get; set; }
        public string AvatarSource { get; set; }

        // 會員身分(customertypecode) / 信仰狀態(new_spiriitual_identity) 改為下拉編輯所需：
        // *Value = 目前選中的 OptionSet 整數值(無值為 null)；*Options = 全部可選項(文字+值)。
        public int? MembershipStatusValue { get; set; }
        public int? SpiritualIdentityValue { get; set; }
        public IReadOnlyList<OptionItem> MembershipStatusOptions { get; set; } = new List<OptionItem>();
        public IReadOnlyList<OptionItem> SpiritualIdentityOptions { get; set; } = new List<OptionItem>();

        public IReadOnlyList<RelationGoalItem> RelationGoals { get; set; } = new List<RelationGoalItem>();
    }

    /// <summary>OptionSet 單一選項：以整數值送回後台，顯示文字給使用者看。</summary>
    public class OptionItem
    {
        public int Value { get; set; }
        public string Text { get; set; }
    }

    public class RelationGoalItem
    {
        public string Role { get; set; }
        public string TargetName { get; set; }
    }
}
