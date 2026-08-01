namespace GamingServicesCloud.DTO;

public enum PurchaseStatusCode
{
    Success = 0,

    // Client errors
    InvalidPurchaseId = 1000,
    InvalidRequest = 1001,
    Unauthorized = 1002,
    PlayerNotFound = 1003,
    InvalidReceipt = 1004,
    UnsupportedStore = 1005,

    // Business errors
    InsufficientCurrency = 2000,
    InventoryFull = 2001,
    PurchaseLimitReached = 2002,
    PurchaseUnavailable = 2003,
    DuplicateTransaction = 2004,

    // Server errors
    EconomyServiceUnavailable = 3000,
    ServerError = 3001,
    Timeout = 3002,
    Unknown = 9999
}

public struct PurchaseResponse
{
    public PurchaseStatusCode StatusCode { get; set; }

    public bool Success => StatusCode == PurchaseStatusCode.Success;

    public string? Message { get; set; }

    public PlayerEconomyData? EconomyData { get; set; }
}