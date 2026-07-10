using Line.Messaging;

namespace LineMessagingProcessor
{
    public static class LineMessagingClientFactory
    {
        public static LineMessagingClient CreateOwnedClient(string channelAccessToken)
        {
#pragma warning disable CS0618
            return new LineMessagingClient(channelAccessToken ?? string.Empty);
#pragma warning restore CS0618
        }
    }
}
