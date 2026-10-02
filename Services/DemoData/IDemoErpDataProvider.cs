using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.DemoData;

public interface IDemoErpDataProvider
{
    /// <summary>Fictional ERP records for demonstration purposes only.</summary>
    IReadOnlyList<RawErpRecord> GetRecords();
}
