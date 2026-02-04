using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（LINE 身分綁定/註冊）
    /// </summary>
    public partial class AuthenticationController
    {
        #region LINE 身分綁定註冊

        [HttpGet]
        [Route("/Authentication/LineLiffView/{LineIdLoginViewPatameter?}")]
        [Route("/LineLiffView/{LineIdLoginViewPatameter?}")]
        public IActionResult LineLiffView(string LineIdLoginViewPatameter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LineIdLoginViewPatameter))
                {
                    return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = "缺少 LIFF 參數，請從 LINE 入口開啟。" });
                }

                var images = new List<string>
                {
                    Url.Content("~/assets/images/church-001.jpg"),
                    Url.Content("~/assets/images/church-002.jpg")
                };

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "LineLiffView");
            }
        }

        [HttpPost]
        [Route("/Authentication/ProcessLineBinding")]
        public async Task<IActionResult> ProcessLineBinding(LineBindingViewModel model)
        {
            try
            {
                SyncLineProfileToModel(model);

                var validationResult = ValidateLineBindingModel(model);
                if (validationResult != null)
                    return validationResult;

                IOrganizationService service = null;
                try
                {
                    service = GetConnection();

                    var existingBindingResult = await CheckExistingLineBinding(service, model.LineUserId);
                    if (existingBindingResult != null)
                        return existingBindingResult;

                    var matchedContact = await FindMatchingContactByNameAndMobile(service, model.FullName, model.Mobile);

                    if (matchedContact != null)
                    {
                        return await UpdateExistingContactWithLineBinding(service, matchedContact, model);
                    }

                    return await HandleNoMatchAndMaybeCreateAsync(service, model);
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    return HandleCrmServiceException(ex);
                }
                catch (TimeoutException ex)
                {
                    return HandleTimeoutException(ex);
                }
                finally
                {
                    ReleaseConnection(service);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLineBinding");
            }
        }

        #region ProcessLineBinding 輔助方法

        private void SyncLineProfileToModel(LineBindingViewModel model)
        {
            if (model == null) return;

            var src = InMemoryContext?.LineBindingViewModel;
            if (src == null) return;

            if (string.IsNullOrWhiteSpace(model.DisplayName) && !string.IsNullOrWhiteSpace(src.DisplayName))
                model.DisplayName = src.DisplayName;

            if (string.IsNullOrWhiteSpace(model.PictureUrl) && !string.IsNullOrWhiteSpace(src.PictureUrl))
                model.PictureUrl = src.PictureUrl;

            if (string.IsNullOrWhiteSpace(model.StatusMessage) && !string.IsNullOrWhiteSpace(src.StatusMessage))
                model.StatusMessage = src.StatusMessage;

            if (string.IsNullOrWhiteSpace(model.LineUserId) && !string.IsNullOrWhiteSpace(src.LineUserId))
                model.LineUserId = src.LineUserId;

            if (string.IsNullOrWhiteSpace(model.FullName) && !string.IsNullOrWhiteSpace(src.FullName))
                model.FullName = src.FullName;

            if (string.IsNullOrWhiteSpace(model.Mobile) && !string.IsNullOrWhiteSpace(src.Mobile))
                model.Mobile = src.Mobile;
        }

        private IActionResult ValidateLineBindingModel(LineBindingViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FullName))
                return Json(new { status = "0", message = "主要姓名必填" });

            if (string.IsNullOrWhiteSpace(model.Mobile))
                return Json(new { status = "0", message = "行動電話必填" });

            if (string.IsNullOrWhiteSpace(model.LineUserId))
                return Json(new { status = "0", message = "LINE User ID 遺失" });

            return null;
        }

        private Entity _placeholderLineContact = null;

        private async Task<IActionResult> CheckExistingLineBinding(IOrganizationService service, string lineUserId)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("new_lineid", ConditionOperator.Equal, lineUserId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                },
                TopCount = 1
            };

            var results = await Task.Run(() => service.RetrieveMultiple(query));

            if (results.Entities.Count > 0)
            {
                var found = results.Entities[0];
                var existingName = found.GetAttributeValue<string>("fullname");

                if (!string.IsNullOrWhiteSpace(existingName) && !existingName.Contains("(Line)"))
                {
                    return Json(new { status = "0", message = $"此 LINE 帳號已綁定至 {existingName}" });
                }

                if (!string.IsNullOrWhiteSpace(existingName) && existingName.Contains("(Line)"))
                {
                    _placeholderLineContact = found;
                }
            }

            return null;
        }

        private async Task<Entity> FindMatchingContactByNameAndMobile(IOrganizationService service, string fullName, string mobile)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "mobilephone"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, fullName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var results = await Task.Run(() => service.RetrieveMultiple(query));

            if (results.Entities.Count == 0)
                return null;

            var normalizedInputMobile = ExtractDigits(mobile);
            foreach (var contact in results.Entities)
            {
                var mobilePhone = contact.Contains("mobilephone") ? contact.GetAttributeValue<string>("mobilephone") : string.Empty;
                var normalizedMobilePhone = ExtractDigits(mobilePhone);

                if (!string.IsNullOrEmpty(normalizedInputMobile) && normalizedMobilePhone == normalizedInputMobile)
                    return contact;
            }

            foreach (var contact in results.Entities)
            {
                var mobilePhone = contact.Contains("mobilephone") ? contact.GetAttributeValue<string>("mobilephone") : string.Empty;
                if (string.IsNullOrEmpty(mobilePhone))
                    return contact;
            }

            return null;
        }

        private async Task<IActionResult> UpdateExistingContactWithLineBinding(IOrganizationService service, Entity contact, LineBindingViewModel model)
        {
            if (_placeholderLineContact != null && _placeholderLineContact.Id != contact.Id)
            {
                try
                {
                    var inactiveEntity = new Entity("contact") { Id = _placeholderLineContact.Id };
                    inactiveEntity["statecode"] = new OptionSetValue(1);
                    inactiveEntity["statuscode"] = new OptionSetValue(2);

                    await Task.Run(() => service.Update(inactiveEntity));
                }
                catch
                {
                }
                finally
                {
                    _placeholderLineContact = null;
                }
            }

            contact["new_lineid"] = model.LineUserId;

            if (!string.IsNullOrWhiteSpace(model.Mobile))
                contact["mobilephone"] = ExtractDigits(model.Mobile.Trim());

            if (!string.IsNullOrWhiteSpace(model.DisplayName))
                contact["new_line_displayname"] = model.DisplayName;

            if (!string.IsNullOrWhiteSpace(model.PictureUrl))
                contact["new_line_picture_url"] = model.PictureUrl;

            if (!string.IsNullOrWhiteSpace(model.StatusMessage))
                contact["new_line_status_message"] = model.StatusMessage;

            contact["new_lineid_backup"] = model.LineUserId;
            contact["new_line_type"] = "個人";
            contact["new_line_register"] = false;

            if (!string.IsNullOrWhiteSpace(model.OtherName))
                contact["new_other_name"] = model.OtherName;

            await Task.Run(() => service.Update(contact));

            return Json(new { status = "1", message = $"已成功綁定 LINE 至現有帳號:{model.FullName}" });
        }

        private async Task<IActionResult> CreateNewContactWithLineBinding(IOrganizationService service, LineBindingViewModel model)
        {
            var newContact = new Entity("contact");
            newContact["lastname"] = model.FullName;
            newContact["mobilephone"] = ExtractDigits(model.Mobile);
            newContact["new_lineid"] = model.LineUserId;

            if (!string.IsNullOrWhiteSpace(model.DisplayName))
                newContact["new_line_displayname"] = model.DisplayName;

            if (!string.IsNullOrWhiteSpace(model.PictureUrl))
                newContact["new_line_picture_url"] = model.PictureUrl;

            if (!string.IsNullOrWhiteSpace(model.StatusMessage))
                newContact["new_line_status_message"] = model.StatusMessage;

            newContact["new_lineid_backup"] = model.LineUserId;
            newContact["new_line_type"] = "個人";
            newContact["new_line_register"] = false;

            if (!string.IsNullOrWhiteSpace(model.OtherName))
                newContact["new_other_name"] = model.OtherName;

            var newContactId = await Task.Run(() => service.Create(newContact));

            if (_placeholderLineContact != null && _placeholderLineContact.Id != newContactId)
            {
                try
                {
                    var inactiveEntity = new Entity("contact") { Id = _placeholderLineContact.Id };
                    inactiveEntity["statecode"] = new OptionSetValue(1);
                    inactiveEntity["statuscode"] = new OptionSetValue(2);

                    await Task.Run(() => service.Update(inactiveEntity));
                }
                catch
                {
                }
                finally
                {
                    _placeholderLineContact = null;
                }
            }

            if (newContactId != Guid.Empty)
                return Json(new { status = "1", message = $"註冊成功！歡迎 {model.FullName} 加入好牧人" });

            return Json(new { status = "0", message = "註冊失敗，請稍後再試" });
        }

        private async Task<IActionResult> HandleNoMatchAndMaybeCreateAsync(IOrganizationService service, LineBindingViewModel model)
        {
            var nameCheckQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, model.FullName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var nameCheckResults = await Task.Run(() => service.RetrieveMultiple(nameCheckQuery));

            if (nameCheckResults.Entities.Count > 0)
            {
                return Json(new
                {
                    status = "0",
                    message = $"系統找到 {nameCheckResults.Entities.Count} 位名為「{model.FullName}」的聯絡人，但您輸入的手機號碼與系統中的紀錄不符。\n\n" +
                             "請確認：\n" +
                             "1. 您輸入的手機號碼是否正確\n" +
                             "2. 若您的手機號碼已更換，請聯絡系統管理員更新資料庫中的手機號碼\n" +
                             "3. 若您是新註冊會員，請使用不同的姓名以避免重複"
                });
            }

            return await CreateNewContactWithLineBinding(service, model);
        }

        private IActionResult HandleCrmServiceException(FaultException<OrganizationServiceFault> ex)
            => Json(new { status = "0", message = $"系統服務異常: {ex.Detail?.Message ?? ex.Message}" });

        private IActionResult HandleTimeoutException(TimeoutException ex)
            => Json(new { status = "0", message = "系統連接超時，請稍後再試" });

        private static string ExtractDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return Regex.Replace(input, "\\D", string.Empty);
        }

        #endregion

        #endregion
    }
}
