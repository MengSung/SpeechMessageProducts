/*
檔案整體說明

目的與定位：處理樹狀選單的節點選取及相關元件同步，讓名單／小組階層的工作識別保持一致；樹節點顯示及可選性不代替端點的資料範圍驗證。

主要功能與流程：
操作入口包含OnItemSelectionChanged、OptionChanged、ContentReady；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
function OnItemSelectionChanged(args) {
    //alert("AAA");
    var value = args.component.getSelectedNodesKeys();
    //alert("value =" + value);
    //alert("args.component = " + args.component);
    component.option("value", value);
}

function OptionChanged(args) {
    //alert("BBB");
}


function ContentReady(args) {
    //debugger;
    //alert("ContentReady 001");
    var Option = component.option("value");
    //alert("Option = " + Option);
    syncTreeViewSelection(args.component, component.option("value"));
}
