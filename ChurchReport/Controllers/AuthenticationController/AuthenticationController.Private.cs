using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（私有輔助方法）
    /// </summary>
    public partial class AuthenticationController
    {
        #region 私有輔助方法

        private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}");

                string contactIdString = "";

                if (viewModel.Account != "")
                {
                    IOrganizationService service = null;
                    try
                    {
                        service = GetConnection();

                        var query = new QueryExpression("contact")
                        {
                            ColumnSet = new ColumnSet("contactid", "new_app_pass"),
                            Criteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression("new_app_acount", ConditionOperator.Equal, viewModel.Account),
                                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                                }
                            },
                            TopCount = 1
                        };

                        var results = service.RetrieveMultiple(query);

                        if (results.Entities.Count == 0)
                            return (false, "", "帳號錯誤");

                        var contact = results.Entities[0];
                        var storedPassword = contact.Contains("new_app_pass")
                            ? contact.GetAttributeValue<string>("new_app_pass")
                            : null;

                        if (string.IsNullOrEmpty(storedPassword))
                            return (false, "", "系統沒有設定密碼");

                        if (storedPassword != viewModel.Password)
                            return (false, "", "密碼錯誤");

                        contactIdString = contact.Id.ToString();
                    }
                    catch (FaultException<OrganizationServiceFault> ex)
                    {
                        return (false, "", $"系統服務異常: {ex.Detail?.Message ?? ex.Message}");
                    }
                    catch (TimeoutException)
                    {
                        return (false, "", "系統連接超時，請稍後再試");
                    }
                    finally
                    {
                        ReleaseConnection(service);
                    }
                }
                else
                {
                    contactIdString = "透過Line Id 登入";
                }

                return (true, contactIdString, "");
            }
            catch (Exception ex)
            {
                return (false, "", $"驗證過程發生錯誤: {ex.Message}");
            }
        }

        private async Task<(Entity loginContact, string fullName)> RetrieveUserData(string contactIdString, GalleryViewModel viewModel)
        {
            Entity loginContact = null;
            string fullName = "";

            IOrganizationService service = null;
            try
            {
                service = GetConnection();

                if (contactIdString != "透過Line Id 登入")
                {
                    loginContact = service.Retrieve("contact", new Guid(contactIdString), new ColumnSet(true));
                    fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";
                }
                else
                {
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet(true),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal, InMemoryContext.LineBindingViewModel.LineUserId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };

                    var results = service.RetrieveMultiple(query);

                    if (results.Entities.Count > 0)
                    {
                        loginContact = results.Entities[0];
                        fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";

                        viewModel.Account = "LineIdLogin";
                        viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new Exception($"取得使用者資料失敗: {ex.Detail?.Message ?? ex.Message}", ex);
            }
            catch (TimeoutException ex)
            {
                throw new Exception("取得使用者資料超時，請稍後再試", ex);
            }
            finally
            {
                ReleaseConnection(service);
            }

            return (loginContact, fullName);
        }

        private void InitializeUserSession(Entity loginContact, GalleryViewModel viewModel)
        {
            InMemoryContext.AppointmentsListManager.m_Account = viewModel.Account;
            InMemoryContext.AppointmentsListManager.m_Password = viewModel.Password;
            InMemoryContext.AppointmentsListManager.m_LoginContact = loginContact;

            InMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;
        }

        private void SetupSystemData(Entity loginContact, GalleryViewModel viewModel)
        {
            IOrganizationService service = null;
            try
            {
                service = GetConnection();

                InMemoryContext.ListManager.SetupListManager(
                    viewModel.Account,
                    viewModel.Password,
                    DateTime.Now,
                    service);

                try
                {
                    InMemoryContext.AppointmentsListManager.SetupAppointmentList();
                }
                catch
                {
                }

                try
                {
                    if (loginContact != null)
                    {
                        InMemoryContext.QpayManager.LoginType = "網頁登入";
                        InMemoryContext.QpayManager.SetQpayModel(loginContact);
                    }
                }
                catch
                {
                }

                try
                {
                    InMemoryContext.FeeList.SetupLessonList(viewModel.Account, viewModel.Password);
                }
                catch
                {
                }
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        private string DetermineDisplayViewType()
        {
            try
            {
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();

                if (string.IsNullOrEmpty(displayViewType))
                {
                    if (InMemoryContext.ListManager.LoginType == "小組長")
                        displayViewType = "IntegrateView";
                    else
                        displayViewType = "MultiGroupView";
                }

                if (displayViewType == "IntegrateView")
                {
                    try
                    {
                        InMemoryContext.ListManager.SetupIntegrateData(InMemoryContext.ListManager.ActiveListId);
                    }
                    catch
                    {
                    }
                }

                if (InMemoryContext.ListManager.LoginType != "小組長" &&
                    InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    displayViewType = "HappyGroupView";
                }

                return displayViewType;
            }
            catch
            {
                return "IntegrateView";
            }
        }

        private void SetupViewBagParameters(string displayViewType)
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;

            SetupFeeDataListCount();
            SetMultiGroupLayoutParameter();
        }

        private IActionResult CreateLoginResponse(string displayViewType, string fullName, GalleryViewModel viewModel)
        {
            return Json(new
            {
                DisplayViewType = displayViewType,
                ActiveListId = InMemoryContext.ListManager.ActiveListId,
                message = "歡迎" + fullName + "登入成功!",
                fullname = fullName,
                account = viewModel.Account,
                password = viewModel.Password
            });
        }

        #endregion
    }
}
