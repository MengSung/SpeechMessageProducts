// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/RequestSecurityToken.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class RequestSecurityToken
// 主要成員：OnWriteBodyContents、Token
// 引用命名空間：System、System.ServiceModel.Channels、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.ServiceModel.Channels;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    class RequestSecurityToken : BaseAuthRequest
    {
        private readonly string _context;

        public RequestSecurityToken(byte[] token)
        {
            _context = "uuid-" + Guid.NewGuid().ToString();
            Token = new BinaryExchange(token);
        }

        protected override string Action => "http://schemas.xmlsoap.org/ws/2005/02/trust/RST/Issue";

        public BinaryExchange Token { get; private set; }

        protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
        {
            writer.WriteStartElement("t", "RequestSecurityToken", Namespaces.WSTrust);
            writer.WriteAttributeString("Context", _context);

            writer.WriteStartElement("t", "TokenType", Namespaces.WSTrust);
            writer.WriteString("http://schemas.xmlsoap.org/ws/2005/02/sc/sct");
            writer.WriteEndElement(); // t:TokenType

            writer.WriteStartElement("t", "RequestType", Namespaces.WSTrust);
            writer.WriteString("http://schemas.xmlsoap.org/ws/2005/02/trust/Issue");
            writer.WriteEndElement(); // t:RequestType

            writer.WriteStartElement("t", "KeySize", Namespaces.WSTrust);
            writer.WriteString("256");
            writer.WriteEndElement(); // t:RequestType

            Token.WriteBodyContents(writer);

            writer.WriteEndElement(); // t:RequestSecurityToken
        }
    }
}
