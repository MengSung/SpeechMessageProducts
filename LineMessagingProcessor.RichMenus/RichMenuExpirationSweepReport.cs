namespace LineMessagingProcessor.RichMenus;

public sealed class RichMenuExpirationSweepReport
{
    public RichMenuExpirationSweepReport(int scannedCount, int restoredCount)
    {
        ScannedCount = scannedCount;
        RestoredCount = restoredCount;
    }

    public int ScannedCount { get; }

    public int RestoredCount { get; }
}
