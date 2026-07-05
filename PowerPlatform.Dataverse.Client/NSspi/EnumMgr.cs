// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/EnumMgr.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class EnumStringAttribute、class EnumMgr
// 主要成員：ToText、Text
// 引用命名空間：System、System.Reflection
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Reflection;

namespace NSspi
{
    /// <summary>
    /// Tags an enumeration member with a string that can be programmatically accessed.
    /// </summary>
    [AttributeUsage( AttributeTargets.Field )]
    public class EnumStringAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EnumStringAttribute"/> class.
        /// </summary>
        /// <param name="text">The string to associate with the enumeration member.</param>
        public EnumStringAttribute( string text )
        {
            this.Text = text;
        }

        /// <summary>
        /// Gets the string associated with the enumeration member.
        /// </summary>
        public string Text { get; private set; }
    }

    /// <summary>
    /// Converts betwen enumeration members and the strings associated to the members through the
    /// <see cref="EnumStringAttribute"/> type.
    /// </summary>
    public class EnumMgr
    {
        /// <summary>
        /// Gets the text associated with the given enumeration member through a <see cref="EnumStringAttribute"/>.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string ToText( Enum value )
        {
            FieldInfo field = value.GetType().GetField( value.ToString() );

            EnumStringAttribute[] attribs = (EnumStringAttribute[])field.GetCustomAttributes( typeof( EnumStringAttribute ), false );

            if( attribs == null || attribs.Length == 0 )
            {
                return null;
            }
            else
            {
                return attribs[0].Text;
            }
        }

        /// <summary>
        /// Returns the enumeration member that is tagged with the given text using the <see cref="EnumStringAttribute"/> type.
        /// </summary>
        /// <typeparam name="T">The enumeration type to inspect.</typeparam>
        /// <param name="text"></param>
        /// <returns></returns>
        public static T FromText<T>( string text )
        {
            FieldInfo[] fields = typeof( T ).GetFields();

            EnumStringAttribute[] attribs;

            foreach( FieldInfo field in fields )
            {
                attribs = (EnumStringAttribute[])field.GetCustomAttributes( typeof( EnumStringAttribute ), false );

                foreach( EnumStringAttribute attrib in attribs )
                {
                    if( attrib.Text == text )
                    {
                        return (T)field.GetValue( null );
                    }
                }
            }

            throw new ArgumentException( "Could not find a matching enumeration value for the text '" + text + "'." );
        }
    }
}