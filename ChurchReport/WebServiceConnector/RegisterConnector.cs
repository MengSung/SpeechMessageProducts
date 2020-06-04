using ChurchReport.Models.CrmTransmitModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class RegisterConnector
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();
        #endregion

        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";


        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
                                       // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
                                       // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
                                       //int TRACE_LEVEL = 5;
                                       //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #endregion
        #region 下載資料時所需要的參數
        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        #endregion
        #region 處理註冊區
        public String Register(String FullName, String Mobile, String Account, String Password, String ConfirmPassword)
        {
            if ( Password != ConfirmPassword )
            {
                return "密碼與確認密碼不一致!";
            }

            EntityCollection aListEntityCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(FullName);

            if(aListEntityCollection.Entities.Count == 0 )
            {
                return "姓名不在系統中";
            }

            bool NoMobileFlag = true;
            foreach (Entity aContact in aListEntityCollection.Entities)
            {
                String ContactMobile = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "mobilephone");

                ContactMobile = DigitsOnly.Replace(ContactMobile, "");
                Mobile = DigitsOnly.Replace(Mobile, "");
                if(ContactMobile != "")
                {
                    NoMobileFlag = false;
                }
                if( Mobile == ContactMobile )
                {
                    // 找到姓名相同且行動相同的人

                    // 是否是小組長或是區長，發展到個人版也可以回報
                    FindListCollection(aContact);

                    if ( m_Lists.Entities.Count >= 0 )
                    {
                        #region 是小組長或是區長，發展到個人版也可以回報

                        // 是否有相同帳號
                        Entity Contact = this.m_ToolUtilityClass.DoesAccountExist(Account);
                        if ( Contact != null )
                        {
                            // 有相同的帳號，需要再確認是否與登入者是同一個人
                            // 是否是同一個人
                            if (aContact.Id == Contact.Id)
                            {
                                // 是同一個人
                                // 是則改密碼
                                this.m_ToolUtilityClass.SetEntityStringAttribute(aContact, "new_app_pass", Password);
                                this.m_ToolUtilityClass.UpdateEntity(aContact);
                                return "註冊成功，更改密碼";
                            }
                            else
                            {
                                // 否則回傳錯誤
                                return "已經有此帳號存在!";
                            }
                        }
                        else
                        {
                            // 沒有相同的帳號，可以設定帳號及密碼
                            this.m_ToolUtilityClass.SetEntityStringAttribute(aContact, "new_app_acount", Account);
                            this.m_ToolUtilityClass.SetEntityStringAttribute(aContact, "new_app_pass", Password);
                            this.m_ToolUtilityClass.UpdateEntity(aContact);
                            return "註冊成功";

                        }
                        #endregion
                    }
                    else
                    {
                        return "沒有要點名的名單!";
                    }
                }
            }

            if (NoMobileFlag == true)
            {
                return "沒有行動電話!";
            }
            else
            {
                return "行動電話不一致!";
            }
        }
        #endregion
        #region 工具區
        private void FindListCollection( Entity aContact )
        {
            try
            {
                // 先尋找族系名單
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", aContact.Id.ToString(), "new_contact_race_leager_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 小組長小組名單集合
                    EntityCollection aFamilyLeaderListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", aContact.Id.ToString(), "new_contact_family_leader_list", "list");

                    // 合併小組名單至族系名單，單扣除掉重複的
                    // 然後放在小組名單裡面
                    EntityCollection aMergeCollection = MergeCollection(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);

                    // 過濾掉需要點名的名單才進來
                    FilterAppNamedListEntity(aMergeCollection);

                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                }

                // 找到小組長小組名單集合 
                aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", aContact.Id.ToString(), "new_contact_family_leader_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 過濾掉需要點名的名單才進來
                    FilterAppNamedListEntity(aListEntityCollection);
                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                }

                // 找到小家長小組名單集合 
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_familyhead_list", "list");
                //if (aListEntityCollection.Entities.Count > 0)
                //{
                //    FilterAppNamedListEntity(aListEntityCollection);
                //    return;
                //}

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private EntityCollection MergeCollection(ref EntityCollection aListEntityCollection, ref EntityCollection aFamilyLeaderListEntityCollection)
        {
            try
            {
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                foreach (Entity RaceListEntity in aListEntityCollection.Entities)
                {
                    // 一個一個處理族系名單
                    bool Flag = false;
                    foreach (Entity FamilyLeaderListEntity in aFamilyLeaderListEntityCollection.Entities)
                    {
                        if (RaceListEntity.Id == FamilyLeaderListEntity.Id)
                        {
                            // 在小組名單裡已經有了，就跳出迴圈，不再找了
                            Flag = true;
                            break;
                        }
                    }

                    if (Flag == false)
                    {
                        // 這個小組名單並沒有在族系名單之中
                        //aListEntityCollection.Entities.Add(FamilyLeaderListEntity);

                        // 這個族系名單並沒有在小組名單之中
                        aFamilyLeaderListEntityCollection.Entities.Add(RaceListEntity);
                    }
                    else { }
                }

                return aFamilyLeaderListEntityCollection;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void FilterAppNamedListEntity(EntityCollection aListEntityCollection)
        {
            try
            {
                // 過濾掉需要點名的名單才進來
                if (this.m_Lists != null && this.m_Lists.Entities != null)
                {
                    this.m_Lists.Entities.Clear();
                }

                foreach (Entity ListEntity in aListEntityCollection.Entities)
                {
                    if (ListEntity.Attributes.Contains("new_app_named"))
                    {
                        bool AppNamed = (bool)ListEntity.Attributes["new_app_named"];

                        if (AppNamed == true)
                        {
                            this.m_Lists.Entities.Add(ListEntity);
                        }
                    }
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void FilterAppNamedListEntity(String aIdentity, EntityCollection aListEntityCollection)
        {
            try
            {
                // 過濾掉需要點名的名單才進來
                if (this.m_Lists != null && this.m_Lists.Entities != null)
                {
                    this.m_Lists.Entities.Clear();
                }

                foreach (Entity ListEntity in aListEntityCollection.Entities)
                {
                    if (ListEntity.Attributes.Contains("new_app_named"))
                    {
                        bool AppNamed = (bool)ListEntity.Attributes["new_app_named"];

                        if (AppNamed == true)
                        {
                            if (aIdentity == "族長")
                            {
                                //  族長   = new_contact_race_leager_list
                                //  小組長 = new_contact_family_leader_list
                                //  永和禮拜堂，因為永和禮拜堂沒有小家長
                                //Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_familyhead_list");
                                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_contact_family_leader_list");

                                String ListName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                                // 過濾掉需要點名的名單才進來，若是族長則名單裡就應該沒有"小家長"、"小組長"
                                //if (FamilyLeaderId == Guid.Empty && GroupLeaderId == Guid.Empty)
                                if (GroupLeaderId == Guid.Empty || GroupLeaderId == m_ContactId)
                                {
                                    if (!ListName.Contains("門徒")) // 不包含"門徒"名單
                                    {
                                        this.m_Lists.Entities.Add(ListEntity);
                                    }
                                }

                                // 需要回報給族系族長/區長的名單
                                if (!ListName.Contains("門徒")) // 不包含"門徒"名單
                                {
                                    this.m_PresentLists.Entities.Add(ListEntity);
                                }

                            }
                            else if (aIdentity == "小組長")
                            {
                                //Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_familyhead_list");
                                //
                                //// 過濾掉需要點名的名單才進來，若是小組長則名單裡就應該沒有"小家長"
                                //if (FamilyLeaderId == Guid.Empty )
                                //{
                                //    this.m_Lists.Entities.Add(ListEntity);
                                //}


                                this.m_Lists.Entities.Add(ListEntity);
                                // 需要回報給族系族長/區長的名單
                                this.m_PresentLists.Entities.Add(ListEntity);
                            }
                            else if (aIdentity == "小家長")
                            {
                                this.m_Lists.Entities.Add(ListEntity);
                                // 需要回報給族系族長/區長的名單
                                this.m_PresentLists.Entities.Add(ListEntity);
                            }
                            else { }
                        }
                    }
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        #endregion

    }
}
