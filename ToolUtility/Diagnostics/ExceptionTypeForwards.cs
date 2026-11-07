/*
檔案整體說明

目的與定位：保留已編譯呼叫端對ToolUtility例外型別的組件身分相容性。P2真正將三個
診斷型別搬到SpeechMessage.Hosting，本檔只宣告CLR型別轉送，不保留第二套實作。

主要功能與流程：
載入舊ToolUtility型別參照時，由TypeForwardedTo指向Hosting中的相同完整型別名稱。
新的來源同樣直接取得Hosting型別，因此Report、options、queue與static registration
在程序中只有一份；本檔不執行I/O、不建立服務、不接收或發布任何使用者資料。

使用邊界與維護：
Hosting不得反向參考ToolUtility，以避免組件載入循環。不可複製原型別、修改namespace
或移除轉送來掩蓋搬移。由Hosting組件歸屬、R4相依圖及ChurchReport既有診斷測試驗證；
資源仍由原Host／DI owner確定釋放，轉送屬宣告資料，沒有獨立Dispose責任。
*/
using System.Runtime.CompilerServices;
using ToolUtilityNameSpace.Diagnostics;

[assembly: TypeForwardedTo(typeof(ExceptionDiagnostics))]
[assembly: TypeForwardedTo(typeof(ExceptionOutputOptions))]
[assembly: TypeForwardedTo(typeof(ExceptionReporting))]
