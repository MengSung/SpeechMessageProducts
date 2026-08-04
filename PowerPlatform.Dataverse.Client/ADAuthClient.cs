// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthClient.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class ADAuthClient、class ExecuteRequestWriter
// 主要成員：Authenticate、Associate、Create、Delete、Disassociate、Execute、Retrieve、RetrieveMultiple、Update、OnWriteBodyContents
// 引用命名空間：PowerPlatform.Dataverse.Client.ADAuthHelpers、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Messages、Microsoft.Xrm.Sdk.Query、NSspi.Contexts、System.Buffers、System.Net.Security、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using PowerPlatform.Dataverse.Client.ADAuthHelpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using NSspi.Contexts;
#if NET7_0_OR_GREATER
using System.Buffers;
using System.Net.Security;
#else
#endif
using System;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// 使用 AD/SSPI 驗證的內部 Organization Service 實作。
    /// 此物件只屬於一個 <see cref="OnPremiseClient"/>；Dispose 後會清除可清除的驗證衍生
    /// 資料與帳密參考，避免連線池淘汰後讓 Token、密碼或身分狀態被保留在記憶體中。
    /// </summary>
    class ADAuthClient : IOrganizationService, IDisposable
    {
        private string _url;
        private string _domain;
        private string _username;
        private string _password;
        private string _upn;
        private DateTime _tokenExpires;
        private byte[] _proofToken;
        private SecurityContextToken _securityContextToken;
        private int _disposeState;

        /// <summary>
        /// Creates a new <see cref="ADAuthClient"/>
        /// </summary>
        /// <param name="url">The URL of the organization service</param>
        /// <param name="username">The username to authenticate as</param>
        /// <param name="password">The password to authenticate as</param>
        /// <param name="upn">The UPN the server process is running under</param>
        public ADAuthClient(string url, string username, string password, string upn)
        {
#if !NET7_0_OR_GREATER
            if (Environment.OSVersion.Platform == System.PlatformID.Unix)
                throw new PlatformNotSupportedException("Windows authentication is only available on Windows clients or when using .NET 7");
#endif

            _url = url;
            _upn = upn;
            Timeout = TimeSpan.FromSeconds(30);

            if (!String.IsNullOrEmpty(username))
            {
                // Split username into domain + username
                var domain = "";
                var parts = username.Split('\\');

                if (parts.Length == 2)
                {
                    domain = parts[0];
                    username = parts[1];
                }
                else if (parts.Length == 1)
                {
                    parts = username.Split('@');

                    if (parts.Length == 2)
                    {
                        domain = parts[1];
                        username = parts[0];
                    }
                }

                _domain = domain;
                _username = username;
                _password = password;
            }
        }

        /// <summary>
        /// Returns or sets the timeout for executing requests
        /// </summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Returns or sets the SDK version that will be reported to the server
        /// </summary>
        public string SdkClientVersion { get; set; }

        /// <summary>
        /// Returns or sets the impersonated user ID
        /// </summary>
        public Guid CallerId { get; set; }

        /// <summary>
        /// 向 AD/SSPI 端點取得並驗證目前 Client 專屬的短期安全權杖。
        /// </summary>
        /// <remarks>
        /// NegotiateAuthentication 僅在本次驗證呼叫的 using 範圍存活；權杖資料只保留到其到期
        /// 或 Client Dispose，後者會清除 proof token 與帳密參考，避免跨連線或跨使用者重用。
        /// </remarks>
        private void Authenticate()
        {
            ThrowIfDisposed();

            if (_tokenExpires > DateTime.UtcNow.AddSeconds(10))
                return;

#if NET7_0_OR_GREATER
            NetworkCredential cred;

            if (String.IsNullOrEmpty(_username))
                cred = CredentialCache.DefaultNetworkCredentials;
            else
                cred = new NetworkCredential(_username, _password, _domain);

            using var context = new NegotiateAuthentication(new NegotiateAuthenticationClientOptions
            {
                AllowedImpersonationLevel = System.Security.Principal.TokenImpersonationLevel.Identification,
                Credential = cred,
                RequiredProtectionLevel = ProtectionLevel.EncryptAndSign,
                TargetName = _upn
            });
            var token = context.GetOutgoingBlob(Array.Empty<byte>(), out var state);

            if (state != NegotiateAuthenticationStatusCode.ContinueNeeded)
            {
                if (state == NegotiateAuthenticationStatusCode.Unsupported && Environment.OSVersion.Platform == PlatformID.Unix)
                    throw new ApplicationException("Error authenticating with the server: " + state + ". Ensure you have the gss-ntlmssp package installed.");
                else
                    throw new ApplicationException("Error authenticating with the server: " + state);
            }
#else
            // Set up the SSPI context
            NSspi.Credentials.Credential cred;

            if (String.IsNullOrEmpty(_username))
                cred = new NSspi.Credentials.CurrentCredential(NSspi.PackageNames.Negotiate, NSspi.Credentials.CredentialUse.Outbound);
            else
                cred = new NSspi.Credentials.PasswordCredential(_domain, _username, _password, NSspi.PackageNames.Negotiate, NSspi.Credentials.CredentialUse.Outbound);

            var context = new ClientContext(cred, _upn, ContextAttrib.ReplayDetect | ContextAttrib.SequenceDetect | ContextAttrib.Confidentiality | ContextAttrib.InitIdentify);
            var state = context.Init(null, out var token);

            if (state != NSspi.SecurityStatus.ContinueNeeded)
                throw new ApplicationException("Error authenticating with the server: " + state);
#endif

            // Keep a hash of all the RSTs and RSTRs that have been sent so we can validate the authenticator
            // at the end.
            var auth = new Authenticator();

            var rst = new RequestSecurityToken(token);
            var resp = rst.Execute(_url, auth);

            var finalResponse = resp as RequestSecurityTokenResponseCollection;

            // Keep exchanging tokens until we get a full RSTR
            while (finalResponse == null)
            {
                if (resp is RequestSecurityTokenResponse r)
                {
#if NET7_0_OR_GREATER
                    token = context.GetOutgoingBlob(r.BinaryExchange.Token, out state);

                    if (state != NegotiateAuthenticationStatusCode.Completed && state != NegotiateAuthenticationStatusCode.ContinueNeeded)
                        throw new ApplicationException("Error authenticating with the server: " + state);
#else
                    state = context.Init(r.BinaryExchange.Token, out token);

                    if (state != NSspi.SecurityStatus.OK && state != NSspi.SecurityStatus.ContinueNeeded)
                        throw new ApplicationException("Error authenticating with the server: " + state);
#endif

                    resp = new RequestSecurityTokenResponse(r.Context, token).Execute(_url, auth);
                    finalResponse = resp as RequestSecurityTokenResponseCollection;
                }
            }

            var wrappedToken = finalResponse.Responses[0].RequestedProofToken.CipherValue;
            _tokenExpires = finalResponse.Responses[0].Lifetime.Expires;
            _securityContextToken = finalResponse.Responses[0].RequestedSecurityToken;

#if NET7_0_OR_GREATER
            if (state != NegotiateAuthenticationStatusCode.Completed)
                token = context.GetOutgoingBlob(finalResponse.Responses[0].BinaryExchange.Token, out state);

            if (state != NegotiateAuthenticationStatusCode.Completed)
                throw new ApplicationException("Error authenticating with the server: " + state);

            var unwrappedTokenWriter = new ArrayBufferWriter<byte>(wrappedToken.Length);
            state = context.Unwrap(wrappedToken, unwrappedTokenWriter, out _);

            if (state != NegotiateAuthenticationStatusCode.Completed)
                throw new ApplicationException("Error authenticating with the server: " + state);

            _proofToken = unwrappedTokenWriter.WrittenSpan.ToArray();
#else
            if (state != NSspi.SecurityStatus.OK)
                state = context.Init(finalResponse.Responses[0].BinaryExchange.Token, out _);

            if (state != NSspi.SecurityStatus.OK)
                throw new ApplicationException("Error authenticating with the server: " + state);

            _proofToken = context.Decrypt(wrappedToken, true);
#endif

            // Check the authenticator is valid
            auth.Validate(_proofToken, finalResponse.Responses[1].Authenticator.Token);
        }

        /// <summary>
        /// 釋放 AD 驗證快取並清除此實體仍可控制的敏感資料參考。
        /// 字串本身不可在 .NET 中原地覆寫，因此以移除參考縮短其可達生命週期；位元組型
        /// proof token 則會先歸零。這個 Client 不建立長駐背景工作，重複 Dispose 為安全 no-op。
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            if (_proofToken != null)
            {
                CryptographicOperations.ZeroMemory(_proofToken);
                _proofToken = null;
            }

            _securityContextToken = null;
            _tokenExpires = default;
            _url = null;
            _domain = null;
            _username = null;
            _password = null;
            _upn = null;
            SdkClientVersion = null;
            CallerId = Guid.Empty;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 防止 Client 在釋放後重新取得或重用 AD 工作階段，維持每個連線實體的單次生命週期。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (System.Threading.Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(ADAuthClient));
            }
        }

        /// <inheritdoc/>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Execute(new AssociateRequest
            {
                Target = new EntityReference(entityName, entityId),
                Relationship = relationship,
                RelatedEntities = relatedEntities
            });
        }

        /// <inheritdoc/>
        public Guid Create(Entity entity)
        {
            var resp = (CreateResponse) Execute(new CreateRequest { Target = entity });
            return resp.id;
        }

        /// <inheritdoc/>
        public void Delete(string entityName, Guid id)
        {
            Execute(new DeleteRequest { Target = new EntityReference(entityName, id) });
        }

        /// <inheritdoc/>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Execute(new DisassociateRequest
            {
                Target = new EntityReference(entityName, entityId),
                Relationship = relationship,
                RelatedEntities = relatedEntities
            });
        }

        /// <inheritdoc/>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Authenticate();

            var message = Message.CreateMessage(MessageVersion.Soap12WSAddressing10, "http://schemas.microsoft.com/xrm/2011/Contracts/Services/IOrganizationService/Execute", new ExecuteRequestWriter(request));
            message.Headers.MessageId = new UniqueId(Guid.NewGuid());
            message.Headers.ReplyTo = new System.ServiceModel.EndpointAddress("http://www.w3.org/2005/08/addressing/anonymous");
            message.Headers.To = new Uri(_url);
            message.Headers.Add(MessageHeader.CreateHeader("SdkClientVersion", Namespaces.Xrm2011Contracts, SdkClientVersion));
            message.Headers.Add(MessageHeader.CreateHeader("UserType", Namespaces.Xrm2011Contracts, "CrmUser"));
            message.Headers.Add(new SecurityHeader(_securityContextToken, _proofToken));

            if (CallerId != Guid.Empty)
                message.Headers.Add(MessageHeader.CreateHeader("CallerId", Namespaces.Xrm2011Contracts, CallerId));

            var req = WebRequest.CreateHttp(_url);
            req.Method = "POST";
            req.ContentType = "application/soap+xml; charset=utf-8";
            req.Timeout = (int) Timeout.TotalMilliseconds;

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
                    var action = responseMessage.Headers.Action;

                    using (var bodyReader = responseMessage.GetReaderAtBodyContents())
                    {
                        bodyReader.ReadStartElement("ExecuteResponse", Namespaces.Xrm2011Services);

                        var serializer = new DataContractSerializer(typeof(OrganizationResponse), "ExecuteResult", Namespaces.Xrm2011Services);
                        var response = (OrganizationResponse) serializer.ReadObject(bodyReader, true, new KnownTypesResolver());

                        bodyReader.ReadEndElement(); // ExecuteRepsonse

                        return response;
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
                        if (bodyReader.LocalName == "Fault" && bodyReader.NamespaceURI == Namespaces.Soap)
                            throw FaultReader.ReadFault(bodyReader, responseAction);

                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            var resp = (RetrieveResponse) Execute(new RetrieveRequest { Target = new EntityReference(entityName, id), ColumnSet = columnSet });
            return resp.Entity;
        }

        /// <inheritdoc/>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var resp = (RetrieveMultipleResponse)Execute(new RetrieveMultipleRequest { Query = query });
            return resp.EntityCollection;
        }

        /// <inheritdoc/>
        public void Update(Entity entity)
        {
            Execute(new UpdateRequest { Target = entity });
        }

        private class ExecuteRequestWriter : BodyWriter
        {
            private readonly OrganizationRequest _request;

            public ExecuteRequestWriter(OrganizationRequest request) : base(isBuffered: true)
            {
                _request = request;
            }

            protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
            {
                writer.WriteStartElement("Execute", Namespaces.Xrm2011Services);

                var serializer = new DataContractSerializer(typeof(OrganizationRequest), "request", Namespaces.Xrm2011Services);
                serializer.WriteObject(writer, _request, new KnownTypesResolver());

                writer.WriteEndElement(); // Execute
            }
        }
    }
}
