/*
 * 檔案整體說明
 *
 * 目的與定位：集中宣告「全教會」與「牧養名單」兩種會友存取範圍的穩定代號，供resolver、scope guard及控制器一致比較；常數本身不證明任何使用者擁有該權限。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoAccess。
 * 資料／選項欄位包含 MemberInfoAccess、Church、ShepherdList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs、
 * SpeechMessageProducts.ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml 追查目前工作樹中的合作／使用位置。
 */
namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoAccess
    {
        public const string Church = "全教會";
        public const string ShepherdList = "牧養名單";
    }
}
