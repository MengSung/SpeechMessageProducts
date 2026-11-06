/*
 * 檔案整體說明
 *
 * 目的與定位：定義既有串接服務請求的輸入欄位，讓呼叫端與接收端維持相同傳輸形狀；資料載體不負責網路傳送、授權或重試，敏感欄位不可整份記錄至日誌。
 *
 * 主要功能與流程：
 * 本檔宣告 ServiceRequest。
 * 資料／選項欄位包含 ServiceRequest、service_name、cmd，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    /// <summary>
    /// 串接服務請求欄位
    /// </summary>
    public class ServiceRequest
    {
        public string service_name { get; set; }
        public string cmd { get; set; }
    }
}
