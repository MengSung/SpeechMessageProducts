// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/BaseAuthRequest.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class BaseAuthRequest
// 主要成員：Execute、Action
// 引用命名空間：System、System.Collections.Generic、System.Net、System.Security.Cryptography、System.ServiceModel、System.ServiceModel.Channels、System.Text、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    abstract class BaseAuthRequest : BodyWriter
    {
        protected BaseAuthRequest() : base(isBuffered: true)
        {
        }

        protected abstract string Action { get; }

        public object Execute(string url, Authenticator auth)
        {
            // Add the request to the hash for later authentication
            var doc = new XmlDocument();
            using (var writer = doc.CreateNavigator().AppendChild())
            {
                WriteBodyContents(XmlDictionaryWriter.CreateDictionaryWriter(writer));
            }
            auth.AddToDigest(doc);

            // Create the SOAP message to send the request
            var message = Message.CreateMessage(MessageVersion.Soap12WSAddressing10, Action, this);
            message.Headers.MessageId = new UniqueId(Guid.NewGuid());
            message.Headers.ReplyTo = new EndpointAddress("http://www.w3.org/2005/08/addressing/anonymous");
            message.Headers.To = new Uri(url);

            var req = WebRequest.CreateHttp(url);
            req.Method = "POST";
            req.ContentType = "application/soap+xml; charset=utf-8";

            using (var reqStream = req.GetRequestStream())
            using (var xmlTextWriter = XmlWriter.Create(reqStream, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false,
                Encoding = new UTF8Encoding(false),
                CloseOutput = true
            }))
            using (var xmlWriter = XmlDictionaryWriter.CreateDictionaryWriter(xmlTextWriter))
            {
                message.WriteMessage(xmlWriter);
                xmlWriter.WriteEndDocument();
                xmlWriter.Flush();
            }

            try
            {
                using (var resp = req.GetResponse())
                using (var respStream = resp.GetResponseStream())
                {
                    var reader = XmlReader.Create(respStream, new XmlReaderSettings());
                    var responseMessage = Message.CreateMessage(reader, 0x10000, MessageVersion.Soap12WSAddressing10);
                    var responseAction = responseMessage.Headers.Action;

                    using (var bodyReader = responseMessage.GetReaderAtBodyContents())
                    {
                        // Read the response as the appropriate type
                        if (bodyReader.LocalName == "RequestSecurityTokenResponse")
                            return RequestSecurityTokenResponse.Read(bodyReader, auth, false);
                        else if (bodyReader.LocalName == "RequestSecurityTokenResponseCollection")
                            return RequestSecurityTokenResponseCollection.Read(bodyReader, auth);
                        else
                            throw new NotSupportedException("Unexpected response element " + bodyReader.LocalName);
                    }
                }
            }
            catch (WebException ex)
            {
                using (var errorStream = ex.Response.GetResponseStream())
                {
                    var reader = XmlReader.Create(errorStream, new XmlReaderSettings());
                    var responseMessage = Message.CreateMessage(reader, 0x10000, MessageVersion.Soap12WSAddressing10);
                    var responseAction = responseMessage.Headers.Action;

                    using (var bodyReader = responseMessage.GetReaderAtBodyContents())
                    {
                        throw FaultReader.ReadFault(bodyReader, responseAction);
                    }
                }
            }
        }
    }
}
