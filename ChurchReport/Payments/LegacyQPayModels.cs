namespace ChurchReport.Payments;

public sealed class CreOrder
{
    public string OrderNo { get; set; } = string.Empty;
    public string ShopNo { get; set; } = string.Empty;
    public string TSNo { get; set; } = string.Empty;
    public string PayType { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Param1 { get; set; } = string.Empty;
    public string Param2 { get; set; } = string.Empty;
    public string Param3 { get; set; } = string.Empty;
    public CreOrderATMParamRes ATMParam { get; set; }
    public CreOrderCardParamRes CardParam { get; set; }
    public CreOrderMobileParamRes MobileParam { get; set; }
}

public sealed class CreOrderATMParamRes
{
    public string AtmPayNo { get; set; } = string.Empty;
    public string WebAtmURL { get; set; } = string.Empty;
    public string OtpURL { get; set; } = string.Empty;
}

public sealed class CreOrderCardParamRes
{
    public string CardPayURL { get; set; } = string.Empty;
}

public sealed class CreOrderMobileParamRes
{
    public string MobilePayURL { get; set; } = string.Empty;
}

public sealed class OrderMaintain
{
    public string OrderNo { get; set; } = string.Empty;
    public string ShopNo { get; set; } = string.Empty;
    public string TSNo { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
