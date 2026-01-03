using ChurchReport.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - CRUD 操作
    /// </summary>
    public partial class SmallGroupController
    {
        #region CRUD 操作

        /// <summary>
        /// 新增出席記錄
        /// </summary>
        [HttpPost]
        public IActionResult InsertPresentRecord(string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_SmallGroupData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertPresentRecord");
            }
        }

        /// <summary>
        /// 更新小組出席記錄（並行更新兩個資料集）
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateSmallGroupPresentRecord(
            string key, 
            string values,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                var task1 = Task.Run(() => 
                    dataList.m_SmallGroupData.UpdateMember(key, values), 
                    cancellationToken);
                
                var task2 = Task.Run(() => 
                    dataList.m_AllMemeberData.UpdateMember(key, values), 
                    cancellationToken);

                await Task.WhenAll(task1, task2).ConfigureAwait(false);

                return Ok();
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateSmallGroupPresentRecord");
            }
        }

        /// <summary>
        /// 刪除出席記錄
        /// </summary>
        [HttpDelete]
        public IActionResult DeletePresentRecord(string key)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                Member deletedMember = dataList.m_AllMemeberData.DeleteMember(key);

                if (deletedMember != null)
                {
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        deletedMember
                    );
                }

                dataList.m_SmallGroupData.DeleteMember(key);
                dataList.m_NewPersonFollowUpData.DeleteMember(key);
                dataList.m_HappyGroup.DeleteMember(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeletePresentRecord");
            }
        }

        #endregion
    }
}
