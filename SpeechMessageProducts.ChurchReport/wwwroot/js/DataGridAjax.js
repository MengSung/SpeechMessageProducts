/*
檔案整體說明

目的與定位：協調一般資料表格的AJAX載入與提交互動，將元件資料交對應端點並處理成功／失敗結果；表格row key須維持正式身分，不靠列順序去重。

主要功能與流程：
操作入口包含GetResult；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
function GetResult() {

            //debugger;
            var result = "[";

            var SmallGroup = $("#gridContainer").dxDataGrid("instance").getDataSource().items();

                $.each(SmallGroup, function (index1, aSmallGroupElement) {
                    var SmallGroupMember = aSmallGroupElement.items;

                    $.each(SmallGroupMember, function (index2, aMember) {
                        result += "{";
                        for (var prop in aMember)
                        {
                           //result += "&Members[" + index + "]." + prop + "=" + member[prop];
                           // alert("prop = " + prop + " Value = " + aMember[prop]);
                           result += "'" + prop + "':" + "'" + aMember[prop] + "'"  + ",";
                       }
                        result += "},";

                    });

                });

            result += "]";

            return result;

}
