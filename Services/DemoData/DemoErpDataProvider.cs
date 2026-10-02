using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.DemoData;

/// <summary>
/// Fixed, in-memory set of fictional ERP records for the Operations Hub demo.
/// No real customers, no real data, no external calls — this exists purely so the
/// transformation pipeline has realistic messiness (missing fields, bad dates,
/// unparseable amounts, unrecognised statuses) to demonstrate handling.
/// </summary>
public sealed class DemoErpDataProvider : IDemoErpDataProvider
{
    private static readonly IReadOnlyList<RawErpRecord> Records =
    [
        new() { SourceErpId = "ERP-1001", CustomerName = "Aurora Retail Group", WorkType = "Fulfilment", RawStatus = "OPEN", RawAmount = "1250.00", RawSourceDate = "2026-06-01T09:15:00-04:00" },
        new() { SourceErpId = "ERP-1002", CustomerName = "  Bramwell & Sons  ", WorkType = "Consulting", RawStatus = "wip", RawAmount = "$3,400.50", RawSourceDate = "2026-06-02T11:00:00Z" },
        new() { SourceErpId = "ERP-1003", CustomerName = "Cobalt Manufacturing", WorkType = "Onboarding", RawStatus = "done", RawAmount = "875.25", RawSourceDate = "2026-06-03T08:45:00-04:00" },
        new() { SourceErpId = "ERP-1004", CustomerName = null, WorkType = "Support", RawStatus = "OPEN", RawAmount = "500.00", RawSourceDate = "2026-06-04T10:00:00Z" },
        new() { SourceErpId = "ERP-1005", CustomerName = "Driftwood Logistics", WorkType = null, RawStatus = "WIP", RawAmount = "2200.00", RawSourceDate = "2026-06-05T13:30:00-04:00" },
        new() { SourceErpId = "ERP-1006", CustomerName = "Elmswood Partners", WorkType = "Consulting", RawStatus = "CANCELLED", RawAmount = "0.00", RawSourceDate = "2026-06-06T09:00:00Z" },
        new() { SourceErpId = "ERP-1007", CustomerName = "Fenwick Traders", WorkType = "Fulfilment", RawStatus = "DONE", RawAmount = "not-a-number", RawSourceDate = "2026-06-07T14:00:00Z" },
        new() { SourceErpId = "ERP-1008", CustomerName = "Greymont Holdings", WorkType = "Onboarding", RawStatus = "OPEN", RawAmount = "1999.99", RawSourceDate = "not-a-date" },
        new() { SourceErpId = "ERP-1009", CustomerName = "Harborline Foods", WorkType = "Support", RawStatus = "wip", RawAmount = "640.00", RawSourceDate = "2026-06-09T07:20:00", SourceTimeZoneId = "Pacific Standard Time" },
        new() { SourceErpId = "ERP-1010", CustomerName = "Ironvale   Systems", WorkType = "Consulting", RawStatus = "DONE", RawAmount = "12500.00", RawSourceDate = "2026-06-10T16:10:00-04:00" },
        new() { SourceErpId = "ERP-1011", CustomerName = "Juniper & Co", WorkType = "Fulfilment", RawStatus = null, RawAmount = "300.00", RawSourceDate = "2026-06-11T09:00:00Z" },
        new() { SourceErpId = "ERP-1012", CustomerName = "Kestrel Analytics", WorkType = "Onboarding", RawStatus = null, RawAmount = "410.00", RawSourceDate = "2026-06-12T09:00:00Z" },
        new() { SourceErpId = "ERP-1013", CustomerName = "Larkspur Media", WorkType = "Support", RawStatus = "OPEN", RawAmount = null, RawSourceDate = "2026-06-13T12:00:00Z" },
        new() { SourceErpId = "ERP-1014", CustomerName = "Millbrook Energy", WorkType = "Consulting", RawStatus = "WIP", RawAmount = "5100.00", RawSourceDate = null },
        new() { SourceErpId = "ERP-1015", CustomerName = "Northgate Realty", WorkType = "Fulfilment", RawStatus = "DONE", RawAmount = "-150.00", RawSourceDate = "2026-06-15T09:00:00Z" },
        new() { SourceErpId = "ERP-1016", CustomerName = "Oakhollow Studios", WorkType = "Onboarding", RawStatus = "OPEN", RawAmount = "780.00", RawSourceDate = "2026-06-16T09:00:00Z" },
        new() { SourceErpId = "ERP-1017", CustomerName = "Pinegrove Metals", WorkType = "Support", RawStatus = "WIP", RawAmount = "990.00", RawSourceDate = "2026-06-17T09:00:00Z" },
        new() { SourceErpId = "ERP-1018", CustomerName = "Quarryside Ltd", WorkType = "Consulting", RawStatus = "DONE", RawAmount = "3300.00", RawSourceDate = "2026-06-18T09:00:00Z" },
        new() { SourceErpId = "ERP-1019", CustomerName = "Ridgemont Freight", WorkType = "Fulfilment", RawStatus = "OPEN", RawAmount = "1420.00", RawSourceDate = "2026-06-19T09:00:00Z" },
        new() { SourceErpId = "ERP-1020", CustomerName = "Silverline Health", WorkType = "Onboarding", RawStatus = "WIP", RawAmount = "2750.00", RawSourceDate = "2026-06-20T09:00:00Z" },
    ];

    public IReadOnlyList<RawErpRecord> GetRecords() => Records;
}
