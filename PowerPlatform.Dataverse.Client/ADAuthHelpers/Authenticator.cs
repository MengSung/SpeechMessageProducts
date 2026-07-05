// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ADAuthHelpers/Authenticator.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class Authenticator
// 主要成員：AddToDigest、Validate、CalculatePSHA1
// 引用命名空間：System、System.Collections.Generic、System.IO、System.Linq、System.Security.Cryptography、System.Security.Cryptography.Xml、System.Text、System.Xml
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace PowerPlatform.Dataverse.Client.ADAuthHelpers
{
    /// <summary>
    /// Generates and validates the authenticator hash
    /// </summary>
    class Authenticator
    {
        private readonly SHA1 _hash;

        public Authenticator()
        {
            _hash = SHA1.Create();
        }

        /// <summary>
        /// Adds an XML document to the hash to be authenticated
        /// </summary>
        /// <param name="xmlDoc">The <see cref="XmlDocument"/> to include in the authentication</param>
        public void AddToDigest(XmlDocument xmlDoc)
        {
            // Canonicalize the RST/RSTR
            var trans = new XmlDsigExcC14NTransform();
            trans.LoadInput(xmlDoc);

            // Add the canonicalised version to the hash
            using (var stream = (Stream)trans.GetOutput(typeof(Stream)))
            using (var reader = new StreamReader(stream))
            {
                var text = reader.ReadToEnd();
                stream.Position = 0;

                var buf = new byte[1024];

                while (true)
                {
                    var read = stream.Read(buf, 0, buf.Length);

                    if (read == 0)
                        break;

                    _hash.TransformBlock(buf, 0, read, buf, 0);
                }
            }
        }

        /// <summary>
        /// Checks if the provided authenticator is valid
        /// </summary>
        /// <param name="proofToken">The key issued by the server</param>
        /// <param name="actualAuthenticator">The authenticator provided by the server</param>
        public void Validate(byte[] proofToken, byte[] actualAuthenticator)
        {
            _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var expectedAuthenticator = CalculatePSHA1(proofToken, Encoding.UTF8.GetBytes("AUTH-HASH").Concat(_hash.Hash).ToArray(), 256);

            if (actualAuthenticator.Length != expectedAuthenticator.Length)
                throw new ApplicationException("Invalid authenticator");

            for (var i = 0; i < actualAuthenticator.Length; i++)
            {
                if (actualAuthenticator[i] != expectedAuthenticator[i])
                    throw new ApplicationException("Invalid authenticator");
            }
        }

        // https://social.microsoft.com/Forums/en-US/c485d98b-6e0b-49e7-ab34-8ecf8d694d31/signing-soap-message-request-via-adfs?forum=crmdevelopment#6cee9fa8-dc24-4524-a5a2-c3d17e05d50e
        private byte[] CalculatePSHA1(byte[] client, byte[] server, int sizeBits)
        {
            var sizeBytes = sizeBits / 8;

            using (var hash = new HMACSHA1())
            {
                hash.Key = client;
                var bufferSize = hash.HashSize / 8 + server.Length;
                var i = 0;

                byte[] b1 = server;
                byte[] b2 = new byte[bufferSize];
                byte[] temp = null;
                byte[] psha = new byte[sizeBytes];

                while (i < sizeBytes)
                {
                    hash.Initialize();
                    b1 = hash.ComputeHash(b1);
                    b1.CopyTo(b2, 0);
                    server.CopyTo(b2, hash.HashSize / 8);

                    temp = hash.ComputeHash(b2);

                    for (var j = 0; j < temp.Length; j++)
                    {
                        if (i < sizeBytes)
                        {
                            psha[i] = temp[j];
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                return psha;
            }
        }
    }
}
