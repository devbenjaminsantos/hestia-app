namespace FinanceDashboard.Api.Models;

public sealed class TransactionImportReceipt
{
    public int UserId { get; set; }
    public required string BatchHash { get; set; }
}
