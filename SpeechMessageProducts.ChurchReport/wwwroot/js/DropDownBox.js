/*
檔案整體說明

目的與定位：提供下拉選擇器與內嵌表格／樹元件的同步事件，讓選取值與顯示文字保持一致；選取值只屬目前畫面，不能作伺服器授權依據。

主要功能與流程：
操作入口包含syncTreeViewSelection、treeBox_valueChanged、gridBox_valueChanged；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
function syncTreeViewSelection(treeView, value) {
    //debugger;

    //alert("syncTreeViewSelection");

    //if (!value) {
    //    treeView.unselectAll();
    //    return;
    //}
    //
    //value.forEach(function (key) {
    //    treeView.selectItem(key);
    //});

    var DropDownValue = $("#DropDownBox").dxDropDownBox("instance").value;
    //alert("DropDown = " + DropDownValue);

    value = "耶和華";
}

function treeBox_valueChanged(e) {
    //debugger;

    //alert("treeBox_valueChanged");

    var $treeView = e.component.content().find(".dx-treeview");

    if ($treeView.length) {
        syncTreeViewSelection($treeView.dxTreeView("instance"), e.value);
    }
}

function gridBox_valueChanged(e) {
    var $dataGrid = $("#embedded-datagrid");

    if ($dataGrid.length) {
        var dataGrid = $dataGrid.dxDataGrid("instance");
        dataGrid.selectRows(e.value, false);
    }
}
