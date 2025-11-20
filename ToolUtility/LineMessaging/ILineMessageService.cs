using System;

namespace ToolUtilityNameSpace.LineMessaging
{
    public interface ILineMessageService
    {
        void CreatePushMessage(string userId, string subject, string message);
    }
}
