// 檔案：SpeechMessageProducts.ChurchReport/Properties/AssemblyInfo.cs
// 此組件層級設定只把內部測試 seam 授權給對應測試組件。production consumer 無法透過
// public API 偽造 MemberInfo target-authorization evidence，確保 evidence 仍只能由
// ChurchReport assembly 內的 server-owned provider 建立與傳遞。

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]
