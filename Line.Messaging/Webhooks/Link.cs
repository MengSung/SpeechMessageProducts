/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載帳號連結的結果及nonce。
 *
 * 主要功能與流程：
 * 本檔宣告 Link。
 * 資料／選項欄位包含 Link、Result、Nonce，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 ToolUtility.Tests/DataOperations/RelationshipQueryCharacterizationTests.cs、
 * Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/Messages/Flex/ParameterTypes/ButtonStyle.cs、Line.Messaging/Webhooks/AccountLinkEvent.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Link Object
    /// </summary>
    public class Link
    {
        /// <summary>
        /// One of the following values to indicate whether the link was successful or not.
        /// <list type="bullet">
        /// <item>
        /// <description>ok: Indicates the link was successful.</description>
        /// </item>
        /// <item>
        /// <description>failed: Indicates the link failed for any reason, such as due to a user impersonation.</description>
        /// </item>
        /// </list>
        /// </summary>
        public LinkResult Result { get; }

        /// <summary>
        /// Specified nonce when verifying the user ID
        /// <para>https://developers.line.me/en/docs/messaging-api/linking-accounts#verifying-user-id</para>
        /// </summary>
        public string Nonce { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="result">
        /// One of the following values to indicate whether the link was successful or not.
        /// <list type="bullet">
        /// <item>
        /// <description>ok: Indicates the link was successful.</description>
        /// </item>
        /// <item>
        /// <description>failed: Indicates the link failed for any reason, such as due to a user impersonation.</description>
        /// </item>
        /// </list>
        /// </param>
        /// <param name="nonce">
        /// Specified nonce when verifying the user ID
        /// <para>https://developers.line.me/en/docs/messaging-api/linking-accounts#verifying-user-id</para>
        /// </param>
        public Link(string result, string nonce)
        {
            Result = (result == "ok") ? LinkResult.OK : LinkResult.Failed;
            Nonce = nonce;
        }

    }
}
