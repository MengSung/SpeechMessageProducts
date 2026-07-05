// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/FaultReader.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class FaultReader
// 主要成員：ReadFault
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Runtime.Serialization、System.ServiceModel、System.Text、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    static class FaultReader
    {
        public static Exception ReadFault(XmlDictionaryReader bodyReader, string action)
        {
            bodyReader.ReadStartElement("Fault", Namespaces.Soap);

            bodyReader.ReadStartElement("Code", Namespaces.Soap);
            bodyReader.ReadStartElement("Value", Namespaces.Soap);
            var faultCode = bodyReader.ReadString();
            var faultCodeParts = faultCode.Split(':');
            var faultCodeName = faultCodeParts[0];
            var faultCodeNS = "";
            if (faultCodeParts.Length > 1)
            {
                faultCodeNS = bodyReader.LookupNamespace(faultCodeParts[0]);
                faultCodeName = faultCodeParts[1];
            }
            bodyReader.ReadEndElement(); // Value

            FaultCode subCode = null;

            if (bodyReader.NodeType == XmlNodeType.Element && bodyReader.LocalName == "Subcode" && bodyReader.NamespaceURI == Namespaces.Soap)
            {
                bodyReader.ReadStartElement("Subcode", Namespaces.Soap);
                bodyReader.ReadStartElement("Value", Namespaces.Soap);
                var faultSubCode = bodyReader.ReadString();
                var faultSubCodeParts = faultSubCode.Split(':');
                var faultSubCodeName = faultSubCodeParts[0];
                var faultSubCodeNS = "";
                if (faultSubCodeParts.Length > 1)
                {
                    faultSubCodeNS = bodyReader.LookupNamespace(faultSubCodeParts[0]);
                    faultSubCodeName = faultSubCodeParts[1];
                }
                bodyReader.ReadEndElement(); // Value
                bodyReader.ReadEndElement(); // Subcode

                subCode = new FaultCode(faultSubCodeName, faultSubCodeNS);
            }

            bodyReader.ReadEndElement(); // Code

            bodyReader.ReadStartElement("Reason", Namespaces.Soap);
            bodyReader.ReadStartElement("Text", Namespaces.Soap);
            var reason = bodyReader.ReadString();
            bodyReader.ReadEndElement(); // Text
            bodyReader.ReadEndElement(); // Reason

            if (bodyReader.NodeType == XmlNodeType.Element && bodyReader.LocalName == "Detail" && bodyReader.NamespaceURI == Namespaces.Soap)
            {
                bodyReader.ReadStartElement("Detail", Namespaces.Soap);

                if (bodyReader.NodeType == XmlNodeType.Element && bodyReader.LocalName == "OrganizationServiceFault" && bodyReader.NamespaceURI == Namespaces.Xrm2011Contracts)
                {
                    var serializer = new DataContractSerializer(typeof(OrganizationServiceFault));
                    var detail = (OrganizationServiceFault)serializer.ReadObject(bodyReader);

                    return new FaultException<OrganizationServiceFault>(detail, new FaultReason(reason), new FaultCode(faultCodeName, faultCodeNS, subCode), action);
                }
                else
                {
                    bodyReader.ReadSubtree();
                }

                bodyReader.ReadEndElement(); // Detail
            }

            bodyReader.ReadEndElement(); // Fault

            return new FaultException(new FaultReason(reason), new FaultCode(faultCodeName, faultCodeNS, subCode), action);
        }
    }
}
