using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace;

namespace ChurchReport.ViewModel
{
    public class GalleryViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
        public string FullName { get; set; } = ""; // 姓名
        public string Mobile { get; set; } = "";   // 手機
        public string Address { get; set; } = "";   // 地址
        public string NationId { get; set; } = ""; // 身分證字號
    }
    public class RegisterViewModel
    {
        public IEnumerable<string> Images { get; set; }
        public string FullName { get; set; }
        public string Mobile { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
    public class LineBindingViewModel
    {
        #region 成員資料

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public Entity m_Contact;

        public IEnumerable<string> Images { get; set; }
        public string DisplayName { get; set; }
        public string DisplayId { get; set; }
        public string GroupId { get; set; }
        public string RoomId { get; set; }
        public string LineUserId { get; set; }
        public string ViewType { get; set; }
        public string UserDisplayName { get; set; }
        public string FullName { get; set; }
        public string OtherName { get; set; }
        public string Mobile { get; set; }
        public string EncodeUrl { get; set; }
        public string BindingResult { get; set; }

        public string FaithStatus { get; set; } //信仰狀態
        public String GenderCode { get; set; }  // 性別
        public DateTime BirthDate { get; set; }   // 生日
        public string PersonalId { get; set; } //身分證字號

        public string PictureUrl { get; set; }
        public string StatusMessage { get; set; }

        public String Gender { get; set; }          // 性別
        public String Status { get; set; }          // 新人信仰狀態

        #endregion

        #region 工具區
        public void GetContactInfomation(String UserLineId, ref String FaithStatus, ref String GenderCode, ref DateTime BirthDate, ref String PersonalId )
        {
            m_Contact = this.m_ToolUtilityClass.RetrieveContactByLineId(UserLineId);
            if (m_Contact == null)
            {
                return;
            }
            else
            {
                FaithStatus = GetFaithStatus(m_Contact);

                GenderCode = GetGenderCode(m_Contact);

                BirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_Contact, "birthdate");

                PersonalId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "new_personal_id");
            }
        }

        public void UpdateContactInfomation(String FaithStatus, String GenderCode, DateTime BirthDate, String PersonalId )
        {
            if (m_Contact == null)
            {
                return;
            }
            else
            {
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref this.m_Contact, "new_spiriitual_identity", GetFaithStatusIndex(FaithStatus));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref this.m_Contact, "gendercode", GetGenderCodeIndex(GenderCode));
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref this.m_Contact, "birthdate", BirthDate.ToLocalTime());
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref this.m_Contact, "new_personal_id", PersonalId );

                this.m_ToolUtilityClass.UpdateEntity(ref this.m_Contact);
            }
        }

        public String GetFaithStatus(Entity aContact)
        {
            int FaithStatusIndex = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_spiriitual_identity");

            switch (FaithStatusIndex)
            {
                case 100000001:
                    return "基督徒";
                case 100000003:
                    return "未信主";
                case 100000002:
                    return "已決志";
                case 100000005:
                    return "慕道友";
                case 100000004:
                    return "-未知-";
                default:
                    return "基督徒";
            }

        }

        public int GetFaithStatusIndex(String FaithStatus)
        {
            switch (FaithStatus)
            {
                case "-未知-":
                    return 100000004;
                case "基督徒":
                    return 100000001;
                case "已決志":
                    return 100000002;
                case "未信主":
                    return 100000003;
                case "慕道友":
                    return 100000005;
                default:
                    return 100000001;
            }

        }

        public String GetGenderCode(Entity aContact)
        {
            int GenderCodeIndex = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");

            switch (GenderCodeIndex)
            {
                case 100000000:
                    return "未知";
                case 200000:
                    return "男性";
                case 200001:
                    return "女性";
                default:
                    return "未知";
            }

        }

        public int GetGenderCodeIndex(String GenderCode)
        {
            switch (GenderCode)
            {
                case "未知":
                    return 100000000;
                case "男性":
                    return 200000;
                case "女性":
                    return 200001;
                default:
                    return 100000000;
            }

        }

        #endregion

    }
}