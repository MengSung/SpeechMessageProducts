/*
 * 檔案整體說明
 *
 * 目的與定位：提供裝備畫面的根資料，將登入工作識別與多個小組的學員裝備資料組合成樹；根物件及子清單屬目前操作或登入範圍，不提供全域共享資料源。
 *
 * 主要功能與流程：
 * 本檔宣告 EquipmentRootClass。
 * 資料／選項欄位包含 EquipmentRootClass、LoginUserId、EquipmenSmallGroupList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/EquipmentDataManager.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class EquipmentRootClass
    {
        // 小組名單登入的使用者 Entity Id
        public string LoginUserId { set; get; }

        // 一個人會有一個以上的小組
        public List<EquipmenSmallGroup> EquipmenSmallGroupList { set; get; } // 小組清單
    }
}
