namespace ProgrammePulse.Models.ContractOps;

public sealed record InvoiceLine
{
    public required Guid InvoiceLineKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid InvoiceKey { get; init; }

    public required string Description { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? UnitRate { get; init; }

    public required decimal LineTotal { get; init; }
}
