// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Trace/BSUTextWriterTraceListener.cs
// 所屬區塊：追蹤與診斷相關工具程式。
// 檔案責任：此檔案提供 BSUTextWriterTraceListener 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class BugslayerTextWriterTraceListener
// 主要成員：Fail
// 引用命名空間：System、System.IO、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
/*----------------------------------------------------------------------
Debugging Applications for Microsoft .NET and Microsoft Windows
Copyright ?1997-2003 John Robbins -- All rights reserved.
----------------------------------------------------------------------*/
using System ;
using System.IO ;
using System.Diagnostics ;

namespace TraceNameSpace
{
/// <summary>
/// It's sad, but the default
/// <seealso cref="TextWriterTraceListener"/> does not write out
/// the full stack trace on assertions.  Why, I'll never know.
/// Use this class as a drop in replacement as it will do the
/// stack trace you expect.
/// </summary>
public class BugslayerTextWriterTraceListener :
	            TextWriterTraceListener
{
    // I want to override a single method, but noooo, I have to
    // mimic every base class ctor.  Sheez.

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class with
    /// <see cref="TextWriter "/> as the output recipient.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    public BugslayerTextWriterTraceListener ( ) : base ( ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class using the
    /// stream as the recipient of the debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="stream">
    /// A that represents the stream the
    ///  writes to.
    /// </param>
    public BugslayerTextWriterTraceListener ( Stream stream )
            : base ( stream ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class using the
    /// file as the recipient of the debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="fileName">
    /// The name of the file the
    ///  writes to.
    /// </param>
    public BugslayerTextWriterTraceListener( string fileName )
            : base ( fileName ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class using the
    /// writer as the recipient of the debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="writer">
    /// A  that receives the output from the
    /// </param>
    public BugslayerTextWriterTraceListener ( TextWriter writer )
            : base ( writer ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class with the
    /// specified name, using the stream as the recipient of the
    /// debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="stream">
    /// A  that represents the stream the
    ///  writes to.
    /// </param>
    /// <param name="name">
    /// The name of the new instance.
    /// </param>
    public BugslayerTextWriterTraceListener ( Stream stream ,
                                              string name    )
            : base ( stream , name ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class with the
    /// specified name, using the file as the recipient of the
    /// debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="fileName">
    /// The name of the file the
    ///  writes to.
    /// </param>
    /// <param name="name">
    /// The name of the new instance.
    /// </param>
    public BugslayerTextWriterTraceListener ( string fileName ,
                                              string name      )
            : base ( fileName , name ) { }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BugslayerTextWriterTraceListener"/> class with the
    /// specified writer, using the file as the recipient of the
    /// debugging and tracing output.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="writer">
    /// A  that receives the output from the
    /// .
    /// </param>
    /// <param name="name">
    /// The name of the new instance.
    /// </param>
    public BugslayerTextWriterTraceListener ( TextWriter writer ,
                                              string     name    )
            : base ( writer , name ) { }

    /// <summary>
    /// Overrides the <see cref="TextWriterTraceListener "/> so that
    /// the stack trace is written to the text file.  An assertion
    /// without a stack trace is pretty worthless.  While there are
    /// two Fail methods, the
    /// <see cref="System.Diagnostics.TextWriterTraceListener"/>.Fail
    /// version simply calls this version.
    /// </summary>
    /// <remarks>
    /// See <see cref="TextWriterTraceListener"/>
    /// </remarks>
    /// <param name="message">
    /// A message to emit.
    /// </param>
    /// <param name="detailMessage">
    /// A detailed message to emit.
    /// </param>
    public override void Fail ( string? message       ,
                                string? detailMessage  )
    {
        Writer.WriteLine ( "---- DEBUG ASSERTION FAILED ----" ) ;
        Writer.WriteLine ( "---- Assert Short Message ----" ) ;
        if ( null != message )
        {
            Writer.WriteLine ( message ) ;
        }
        Writer.WriteLine ( "---- Assert Long Message ----" ) ;
        if ( null != detailMessage )
        {
            Writer.WriteLine ( detailMessage ) ;
        }

        // There's four levels of stack between here and the user's
        // code.
        BugslayerStackTrace bst = new BugslayerStackTrace ( 4 ) ;
        Writer.WriteLine ( bst.ToString ( ) ) ;
    }
}   // End of BugslayerTextWriterTraceListener class


}   // End of Wintellect namespace.
