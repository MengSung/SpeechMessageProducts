using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.CollectionOperations
{
    public class CollectionQueryService : ICollectionQueryService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public CollectionQueryService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(_organizationService));
        }

        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            return _organizationService.RetrieveMultiple(query);
        }

        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                #region // Create the ConditionExpression.
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = "new_list_group_present_weekly_report";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(aListEntityId);

                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday);
                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday.ToShortDateString());
                ConditionExpression DateTimeAfterConditionPrincipal = new ConditionExpression();
                DateTimeAfterConditionPrincipal.AttributeName = "new_sunday_date";
                DateTimeAfterConditionPrincipal.Operator = ConditionOperator.OnOrAfter;
                DateTimeAfterConditionPrincipal.Values.Add(aSunday.AddMonths(-2));


                ConditionExpression DateTimeBeforeConditionPrincipal = new ConditionExpression();
                DateTimeBeforeConditionPrincipal.AttributeName = "new_sunday_date";
                DateTimeBeforeConditionPrincipal.Operator = ConditionOperator.OnOrBefore;
                DateTimeBeforeConditionPrincipal.Values.Add(aSunday);

                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                filter.Conditions.Add(DateTimeAfterConditionPrincipal);
                filter.Conditions.Add(DateTimeBeforeConditionPrincipal);
                #endregion

                #region// Create an instance of the query expression class.
                OrderExpression OrderByDate = new OrderExpression();
                OrderByDate.AttributeName = "new_sunday_date";
                //OrderByDate.OrderType = OrderType.Descending;
                OrderByDate.OrderType = OrderType.Ascending;

                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = "new_group_present_weekly_report";
                query.ColumnSet.AllColumns = true;
                query.Criteria = filter;
                query.Orders.Add(OrderByDate);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;

                request = (RetrieveMultipleResponse)this._organizationService.Execute(retrieve);
                #endregion

                return request.EntityCollection;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
    }
}
