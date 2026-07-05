// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/CombinedHash.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CombinedHash
// 主要成員：Read、Token
// 引用命名空間：System、System.Collections.Generic、System.Text、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    class CombinedHash
    {
        private CombinedHash(byte[] token)
        {
            Token = token;
        }

        public byte[] Token { get; private set; }

        public static CombinedHash Read(XmlDictionaryReader reader)
        {
            reader.ReadStartElement(nameof(CombinedHash), Namespaces.WSTrust);
            var token = reader.ReadString();
            reader.ReadEndElement(); // t:CombinedHash

            return new CombinedHash(Convert.FromBase64String(token));
        }
    }
}
