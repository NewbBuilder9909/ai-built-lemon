namespace ProgrammePulse.Models.ViewModels;

public sealed record FilterOptionViewModel(string Value, string Label, bool IsSelected);

public sealed record ErpRecordsListViewModel(
    IReadOnlyList<ErpRecordRowViewModel> Rows,
    int TotalCount,
    string? SearchTerm,
    IReadOnlyList<FilterOptionViewModel> StatusFilterOptions,
    IReadOnlyList<FilterOptionViewModel> ValidationFilterOptions,
    string SortColumn,
    string SortDirection);
