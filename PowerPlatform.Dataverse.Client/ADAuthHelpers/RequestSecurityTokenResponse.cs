// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/RequestSecurityTokenResponse.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class RequestSecurityTokenResponse
// 主要成員：OnWriteBodyContents、Read、Context、TokenType、RequestedSecurityToken、RequestedAttachedReference、RequestedUnattachedReference、RequestedProofToken、Lifetime、KeySize
// 引用命名空間：System、System.Collections.Generic、System.Security.Cryptography、System.ServiceModel.Channels、System.Text、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    class RequestSecurityTokenResponse : BaseAuthRequest
    {
        public RequestSecurityTokenResponse(string context, byte[] token)
        {
            if (String.IsNullOrEmpty(context))
                throw new ArgumentNullException(nameof(context));

            if (token == null)
                throw new ArgumentNullException(nameof(token));

            Context = context;
            BinaryExchange = new BinaryExchange(token);
        }

        private RequestSecurityTokenResponse()
        {
        }

        protected override string Action => "http://schemas.xmlsoap.org/ws/2005/02/trust/RSTR/Issue";

        public string Context { get; private set; }

        public string TokenType { get; private set; }

        public SecurityContextToken RequestedSecurityToken { get; private set; }

        public SecurityTokenReference RequestedAttachedReference { get; private set; }

        public SecurityTokenReference RequestedUnattachedReference { get; private set; }

        public EncryptedKey RequestedProofToken { get; private set; }

        public Lifetime Lifetime { get; private set; }

        public int? KeySize { get; private set; }

        public BinaryExchange BinaryExchange { get; private set; }

        public CombinedHash Authenticator { get; private set; }

        protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
        {
            writer.WriteStartElement("t", nameof(RequestSecurityTokenResponse), Namespaces.WSTrust);
            writer.WriteAttributeString(nameof(Context), Context);

            BinaryExchange.WriteBodyContents(writer);

            writer.WriteEndElement(); // t:RequestSecurityTokenResponse
        }

        public static RequestSecurityTokenResponse Read(XmlDictionaryReader reader, Authenticator auth, bool isFinal)
        {
            if (reader.LocalName != nameof(RequestSecurityTokenResponse) || reader.NamespaceURI != Namespaces.WSTrust)
                throw new InvalidOperationException();

            if (auth != null)
            {
                // Add the response to the hash
                // For the final response, exclude the RequestedSecurityToken and RequestedProofToken elements
                var subtree = reader.ReadSubtree();
                var doc = new XmlDocument();
                doc.Load(subtree);
                reader.ReadEndElement();

                if (isFinal)
                {
                    var clone = (XmlDocument) doc.Clone();
                    var rst = clone.SelectSingleNode("//*[local-name()='RequestedSecurityToken']");
                    var rpt = clone.SelectSingleNode("//*[local-name()='RequestedProofToken']");

                    rst.ParentNode.RemoveChild(rst);
                    rpt.ParentNode.RemoveChild(rpt);

                    auth.AddToDigest(clone);
                }
                else
                {
                    auth.AddToDigest(doc);
                }

                reader = XmlDictionaryReader.CreateDictionaryReader(new XmlNodeReader(doc));
                reader.MoveToContent();
            }

            var rstr = new RequestSecurityTokenResponse();
            rstr.Context = reader.GetAttribute(nameof(Context));
            reader.ReadStartElement(nameof(RequestSecurityTokenResponse), Namespaces.WSTrust);

            while (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.NamespaceURI != Namespaces.WSTrust)
                {
                    reader.ReadSubtree();
                    continue;
                }

                switch (reader.LocalName)
                {
                    case nameof(TokenType):
                        reader.ReadStartElement();
                        rstr.TokenType = reader.ReadString();
                        reader.ReadEndElement();
                        break;

                    case nameof(RequestedSecurityToken):
                        reader.ReadStartElement();
                        rstr.RequestedSecurityToken = SecurityContextToken.Read(reader);
                        reader.ReadEndElement();
                        break;

                    case nameof(RequestedAttachedReference):
                        reader.ReadStartElement();
                        rstr.RequestedAttachedReference = SecurityTokenReference.Read(reader);
                        reader.ReadEndElement();
                        break;

                    case nameof(RequestedUnattachedReference):
                        reader.ReadStartElement();
                        rstr.RequestedUnattachedReference = SecurityTokenReference.Read(reader);
                        reader.ReadEndElement();
                        break;

                    case nameof(RequestedProofToken):
                        reader.ReadStartElement();
                        rstr.RequestedProofToken = EncryptedKey.Read(reader);
                        reader.ReadEndElement();
                        break;

                    case nameof(Lifetime):
                        reader.ReadStartElement();
                        rstr.Lifetime = Lifetime.Read(reader);
                        reader.ReadEndElement();
                        break;

                    case nameof(KeySize):
                        reader.ReadStartElement();
                        rstr.KeySize = reader.ReadContentAsInt();
                        reader.ReadEndElement();
                        break;

                    case nameof(BinaryExchange):
                        rstr.BinaryExchange = BinaryExchange.Read(reader);
                        break;

                    case nameof(Authenticator):
                        reader.ReadStartElement();
                        rstr.Authenticator = CombinedHash.Read(reader);
                        reader.ReadEndElement();
                        break;

                    default:
                        reader.ReadSubtree();
                        break;
                }
            }

            reader.ReadEndElement(); // t:RequestSecurityTokenResponse
            return rstr;
        }
    }
}
