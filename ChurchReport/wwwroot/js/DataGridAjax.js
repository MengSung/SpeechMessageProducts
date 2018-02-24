
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



