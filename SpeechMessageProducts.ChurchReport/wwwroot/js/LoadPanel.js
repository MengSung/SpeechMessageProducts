/*
檔案整體說明

目的與定位：提供共用載入面板的顯示與隱藏控制，讓資料操作可明確結束等待狀態；新增非同步入口須在成功、失敗及取消時都完成目前操作的面板收尾。

主要功能與流程：
操作入口包含getLoadPanelInstance、button_click、loadPanel_shown、loadPanel_hidden、SaveSmallGroup；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
function getLoadPanelInstance() {
    return $("#loadPanel").dxLoadPanel("instance");
}

function button_click() {
    getLoadPanelInstance().show();
}

function loadPanel_shown(e) {
    //    setTimeout(function () {
    //        e.component.hide();
    //    }, 3000);
}

function loadPanel_hidden() {
}

function SaveSmallGroup(arg) {

    //DevExpress.ui.notify(Model.members[0].PrayItem, "success", 2000);

    getLoadPanelInstance().show();
}
