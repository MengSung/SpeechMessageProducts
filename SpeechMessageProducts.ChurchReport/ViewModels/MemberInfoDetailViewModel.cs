/*
 * 檔案整體說明
 *
 * 目的與定位：定義會友詳細彈窗的基本資料、唯讀性別／生日、選項與關係目標形狀，供控制器映射及Razor繫結；顯示資料增加不代表更新端點可以接受相同欄位。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoDetailViewModel、OptionItem、RelationGoalItem。
 * 資料／選項欄位包含 MemberInfoDetailViewModel、ContactId、FullName、Phone、Address、Gender、BirthDate、MembershipStatus、
 * SpiritualIdentity、AvatarSource、MembershipStatusValue、SpiritualIdentityValue、MembershipStatusOptions、
 * SpiritualIdentityOptions、RelationGoals、OptionItem，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace ChurchReport.ViewModels
{
    public class MemberInfoDetailViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }
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
