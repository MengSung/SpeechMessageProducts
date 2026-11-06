/*
 * 檔案整體說明
 *
 * 目的與定位：提供會友清單的最小列投影，帶正式ContactId、姓名、在籍狀態、電話與小組名；只應由已授權查詢產生，不讓前端列位置或姓名取代正式身分。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoListRowViewModel。
 * 資料／選項欄位包含 MemberInfoListRowViewModel、ContactId、FullName、MembershipStatus、Phone、SmallGroupName，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
namespace ChurchReport.ViewModels
{
    public class MemberInfoListRowViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string MembershipStatus { get; set; }
        public string Phone { get; set; }
        public string SmallGroupName { get; set; }
    }
}
