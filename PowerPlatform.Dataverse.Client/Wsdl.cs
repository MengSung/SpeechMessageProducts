// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/Wsdl.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class Namespaces、class WsdlLoader、class Definitions、class Import、class PolicyItem、class MultiPolicy、class Policy、class ExactlyOne
// 主要成員：Load、Imports、Policies、Bindings、Services、Location、PolicyItems、Id、Policy、Issuer
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Net、System.Text、System.Xml.Serialization
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Serialization;

namespace PowerPlatform.Dataverse.Client.Wsdl
{
    /// <summary>
    /// Classes for parsing WSDL documents.
    /// System.ServiceModel.Description.ServiceEndpoint is not currently available in .NET Core - hopefully this will
    /// be added in the future - https://github.com/dotnet/wcf/issues/4464 - so this implements the specific parts
    /// we require.
    /// </summary>
    class Namespaces
    {
        public const string wsdl = "http://schemas.xmlsoap.org/wsdl/";
        public const string wsam = "http://www.w3.org/2007/05/addressing/metadata";
        public const string wsx = "http://schemas.xmlsoap.org/ws/2004/09/mex";
        public const string wsap = "http://schemas.xmlsoap.org/ws/2004/08/addressing/policy";
        public const string msc = "http://schemas.microsoft.com/ws/2005/12/wsdl/contract";
        public const string msxrm = "http://schemas.microsoft.com/xrm/2011/Contracts/Services";
        public const string wsp = "http://schemas.xmlsoap.org/ws/2004/09/policy";
        public const string xsd = "http://www.w3.org/2001/XMLSchema";
        public const string soap = "http://schemas.xmlsoap.org/wsdl/soap/";
        public const string wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        public const string soap12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
        public const string soapenc = "http://schemas.xmlsoap.org/soap/encoding/";
        public const string tns = "http://schemas.microsoft.com/xrm/2011/Contracts";
        public const string wsa10 = "http://www.w3.org/2005/08/addressing";
        public const string wsaw = "http://www.w3.org/2006/05/addressing/wsdl";
        public const string wsa = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
        public const string sp = "http://docs.oasis-open.org/ws-sx/ws-securitypolicy/200702";
        public const string o = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    }

    public static class WsdlLoader
    {
        public static IEnumerable<Definitions> Load(string url)
        {
            var loaded = new HashSet<string>();
            return Load(loaded, url);
        }

        private static IEnumerable<Definitions> Load(HashSet<string> loaded, string url)
        {
            if (!loaded.Add(url))
                yield break;

            var req = WebRequest.CreateHttp(url);
            using (var resp = req.GetResponse())
            using (var stream = resp.GetResponseStream())
            {
                var serializer = new XmlSerializer(typeof(Definitions));
                var wsdl = (Definitions)serializer.Deserialize(stream);

                yield return wsdl;

                if (wsdl.Imports != null)
                {
                    foreach (var import in wsdl.Imports)
                    {
                        foreach (var child in Load(loaded, import.Location))
                            yield return child;
                    }
                }
            }
        }
    }

    [XmlRoot("definitions", Namespace = Namespaces.wsdl)]
    public class Definitions
    {
        [XmlElement("import", Namespace = Namespaces.wsdl)]
        public Import[] Imports { get; set; }

        [XmlElement("Policy", Namespace = Namespaces.wsp)]
        public Policy[] Policies { get; set; }

        [XmlElement("binding", Namespace = Namespaces.wsdl)]
        public Binding[] Bindings { get; set; }

        [XmlElement("service", Namespace = Namespaces.wsdl)]
        public Service[] Services { get; set; }
    }

    public class Import
    {
        [XmlAttribute("location")]
        public string Location { get; set; }
    }

    public class PolicyItem
    {
    }

    public class MultiPolicy : PolicyItem
    {
        [XmlElement("ExactlyOne", Namespace = Namespaces.wsp, Type = typeof(ExactlyOne))]
        [XmlElement("All", Namespace = Namespaces.wsp, Type = typeof(All))]
        [XmlElement("EndorsingSupportingTokens", Namespace = Namespaces.sp, Type = typeof(EndorsingSupportingTokens))]
        [XmlElement("IssuedToken", Namespace = Namespaces.sp, Type = typeof(IssuedToken))]
        [XmlElement("SignedEncryptedSupportingTokens", Namespace = Namespaces.sp, Type = typeof(SignedEncryptedSupportingTokens))]
        [XmlElement("UsernameToken", Namespace = Namespaces.sp, Type = typeof(UsernameToken))]
        [XmlElement("Trust13", Namespace = Namespaces.sp, Type = typeof(Trust13))]
        [XmlElement("AuthenticationPolicy", Namespace = Namespaces.msxrm, Type = typeof(AuthenticationPolicy))]
        public PolicyItem[] PolicyItems { get; set; }

        public T FindPolicyItem<T>() where T : PolicyItem
        {
            if (PolicyItems == null)
                return null;

            var match = PolicyItems.OfType<T>().FirstOrDefault();

            if (match != null)
                return match;

            return PolicyItems
                .OfType<MultiPolicy>()
                .Select(child => child.FindPolicyItem<T>())
                .Where(m => m != null)
                .FirstOrDefault();
        }
    }

    public class Policy : MultiPolicy
    {
        [XmlAttribute("Id", Namespace = Namespaces.wsu)]
        public string Id { get; set; }
    }

    public class ExactlyOne : MultiPolicy
    {
    }

    public class All : MultiPolicy
    {
    }

    public class EndorsingSupportingTokens : PolicyItem
    {
        [XmlElement("Policy", Namespace = Namespaces.wsp)]
        public Policy Policy { get; set; }
    }

    public class IssuedToken : PolicyItem
    {
        public Issuer Issuer { get; set; }
    }

    public class Issuer
    {
        [XmlElement("Metadata", Namespace = Namespaces.wsa10)]
        public AddressMetadata Metadata { get; set; }
    }

    public class AddressMetadata
    {
        [XmlElement("Metadata", Namespace = Namespaces.wsx)]
        public SoapMetadata Metadata { get; set; }
    }

    public class SoapMetadata
    {
        [XmlElement("MetadataSection", Namespace = Namespaces.wsx)]
        public MetadataSection MetadataSection { get; set; }
    }

    public class MetadataSection
    {
        [XmlElement("MetadataReference", Namespace = Namespaces.wsx)]
        public MetadataReference MetadataReference { get; set; }
    }

    public class MetadataReference
    {
        [XmlElement("Address", Namespace = Namespaces.wsa10)]
        public string Address { get; set; }
    }

    public class SignedEncryptedSupportingTokens : PolicyItem
    {
        [XmlElement("Policy", Namespace = Namespaces.wsp)]
        public Policy Policy { get; set; }
    }

    public class UsernameToken : PolicyItem
    {
    }

    public class Trust13 : PolicyItem
    {
    }

    public class AuthenticationPolicy : PolicyItem
    {
        [XmlElement("Authentication", Namespace = Namespaces.msxrm)]
        public AuthenticationType Authentication { get; set; }
    }

    public enum AuthenticationType
    {
        ActiveDirectory,
        Federation
    }

    public class Binding
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("PolicyReference", Namespace = Namespaces.wsp)]
        public PolicyReference PolicyReference { get; set; }
    }

    public class PolicyReference
    {
        [XmlAttribute("URI")]
        public string Uri { get; set; }
    }

    public class Service
    {
        [XmlElement("port", Namespace = Namespaces.wsdl)]
        public Port[] Ports { get; set; }
    }

    public class Port
    {
        [XmlAttribute("binding")]
        public string Binding { get; set; }

        [XmlElement("address", Namespace = Namespaces.soap12)]
        public SoapAddress Address { get; set; }

        [XmlElement("EndpointReference", Namespace = Namespaces.wsa10)]
        public EndpointReference EndpointReference { get; set; }
    }

    public class SoapAddress
    {
        [XmlAttribute("location")]
        public string Location { get; set; }
    }

    public class EndpointReference
    {
        [XmlElement(Namespace = "http://schemas.xmlsoap.org/ws/2006/02/addressingidentity")]
        public Identity Identity { get; set; }
    }

    public class Identity
    {
        public string Upn { get; set; }

        public string Spn { get; set; }
    }
}
