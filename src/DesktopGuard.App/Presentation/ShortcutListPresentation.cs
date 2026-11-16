using DesktopGuard.App.Application;

namespace DesktopGuard.App.Presentation;

internal sealed class ShortcutListPresentation
{
    private IReadOnlyList<ShortcutListPresentationRow> _rows = [];

    public ShortcutListFilter Filter { get; private set; }

    public int TotalCount => _rows.Count;

    public string SearchText { get; private set; } = string.Empty;

    public ShortcutListSortColumn SortColumn { get; private set; } = ShortcutListSortColumn.Name;

    public bool SortDescending { get; private set; }

    public void SetRows(IEnumerable<ShortcutListPresentationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.ToArray();
    }

    public void SetFilter(ShortcutListFilter filter)
    {
        Filter = filter;
    }

    public void SetSearchText(string? searchText)
    {
        SearchText = searchText?.Trim() ?? string.Empty;
    }

    public void SortBy(ShortcutListSortColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
            return;
        }

        SortColumn = column;
        SortDescending = false;
    }

    public int CountFor(ShortcutListFilter filter)
    {
        return FilterBySearch(_rows).Count(row => MatchesFilter(row, filter));
    }

    public IReadOnlyList<ShortcutListPresentationRow> GetVisibleRows()
    {
        List<ShortcutListPresentationRow> rows = FilterBySearch(_rows)
            .Where(row => MatchesFilter(row, Filter))
            .ToList();
        StringComparer comparer = StringComparer.CurrentCultureIgnoreCase;
        rows.Sort((left, right) =>
        {
            int comparison = SortColumn switch
            {
                ShortcutListSortColumn.Location => comparer.Compare(left.Location, right.Location),
                ShortcutListSortColumn.Status => comparer.Compare(left.Status, right.Status),
                _ => comparer.Compare(left.Entry.Name, right.Entry.Name),
            };

            if (SortDescending)
            {
                comparison = comparison < 0 ? 1 : comparison > 0 ? -1 : 0;
            }

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = comparer.Compare(left.Entry.Name, right.Entry.Name);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Entry.RootKind.CompareTo(right.Entry.RootKind);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = comparer.Compare(left.Location, right.Location);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = comparer.Compare(left.Status, right.Status);
            if (comparison != 0)
            {
                return comparison;
            }

            return Nullable.Compare(left.Entry.Shortcut?.Id, right.Entry.Shortcut?.Id);
        });

        return rows;
    }

    private IEnumerable<ShortcutListPresentationRow> FilterBySearch(IEnumerable<ShortcutListPresentationRow> rows)
    {
        if (SearchText.Length == 0)
        {
            return rows;
        }

        return rows.Where(row => row.Entry.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool MatchesFilter(ShortcutListPresentationRow row, ShortcutListFilter filter)
    {
        return filter switch
        {
            ShortcutListFilter.NeedsReview => row.NeedsReview,
            ShortcutListFilter.Approved => row.Approved,
            ShortcutListFilter.Recovery => row.Recovery,
            _ => true,
        };
    }
}
