
function GetResult() {

            debugger;

            //alert("要上傳資料囉!")
            //Request.arguments = "Hello";
            //request.data = "Hello";
            //data: "Hello";
            //aResult: "ZZZZ";
            //return "耶和華";

            //$.ajax({
            //    url: '@Url.Action("SaveSmallGroup", "Home")',
            //    data: { aResult: "Hello" }
            //    type: 'POST', //POST if you want to save, GET if you want to fetch data from server
            //
            //
            //
            //});

            var result = "[";

            //var Section = $("#gridContainer").dxDataGrid("instance").getDataSource().items();
            var SmallGroup = $("#gridContainer").dxDataGrid("instance").getDataSource().items();

            //$.each( Section, function (index, aSection) {

                //var SmallGroup = aSection.items;

                $.each(SmallGroup, function (index1, aSmallGroupElement) {
                    var SmallGroupMember = aSmallGroupElement.items;

                    $.each(SmallGroupMember, function (index2, aMember) {
                        result += "{";
                        for (var prop in aMember) {

                           //result += "&Members[" + index + "]." + prop + "=" + member[prop];
                           // alert("prop = " + prop + " Value = " + aMember[prop]);
                            result += "'" + prop + "':" + "'" + aMember[prop] + "'"  + ",";
                       }
                        result += "},";

                    });

                });

                //for (var prop in member) {
                //    result += "&Members[" + index + "]." + prop + "=" + member[prop];
                //    alert("Result = " + result)
                //}
            //});

            result += "]";
            //var result = "";
            //$.each(members, function (index, member) {
            //    for (var prop in member) {
            //        result += "&Members[" + index + "]." + prop + "=" + member[prop];
            //        alert("Result = " + result)
            //    }
            //})
            ////request.data += result;
            //
            ////request.data = "HELLO";
            //
            //data.aResult = "Hello";

            return result;

}



