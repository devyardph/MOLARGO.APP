using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Help.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Help.ViewModels;

/// <summary>
/// The help knowledge base as a practice reads it: articles by category, searched and paged.
/// </summary>
/// <remarks>
/// Reads published articles only. The vendor writes them under Platform → Help, and an
/// unpublished one is invisible here — an author needs somewhere to leave a half-written
/// article that is not in front of every customer.
/// </remarks>
public sealed class HelpViewModel : BaseViewModel
{
    /// <summary>
    /// Articles a page.
    /// </summary>
    /// <remarks>
    /// Twelve, not the seventeen the record lists use. A help entry is three lines rather
    /// than one, so seventeen of them is a page somebody scrolls past rather than reads.
    /// </remarks>
    public const int PageSize = 12;

    /// <summary>The tab meaning "no category chosen".</summary>
    public const string AllCategories = "Everything";

    private readonly IHelpService _help;

    private HelpPage _page = new([], 0, 0, PageSize);
    private IReadOnlyList<string> _categories = [];
    private string? _search;
    private string _category = AllCategories;
    private HelpArticle? _open;

    public HelpViewModel(IHelpService help)
    {
        _help = help;

        OpenCommand = new MvxAsyncCommand<Guid>(OpenAsync);
        CloseCommand = new MvxCommand(Close);
        SetCategoryCommand = new MvxAsyncCommand<string>(category => SetCategoryAsync(category!));
        PreviousPageCommand = new MvxAsyncCommand(() => ReloadAsync(Page - 1));
        NextPageCommand = new MvxAsyncCommand(() => ReloadAsync(Page + 1));
        GoToPageCommand = new MvxAsyncCommand<int>(ReloadAsync);
    }

    public IMvxAsyncCommand<Guid> OpenCommand { get; }

    public IMvxCommand CloseCommand { get; }

    public IMvxAsyncCommand<string> SetCategoryCommand { get; }

    public IMvxAsyncCommand PreviousPageCommand { get; }

    public IMvxAsyncCommand NextPageCommand { get; }

    public IMvxAsyncCommand<int> GoToPageCommand { get; }

    public override Task Initialize() => RunGuardedAsync(async () =>
    {
        _categories = await _help.GetCategoriesAsync().ConfigureAwait(false);

        await ReloadAsync(0).ConfigureAwait(false);
    });

    /// <summary>What is being searched for, applied as it is typed.</summary>
    public string? Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value)) return;

            // Back to the first page, for the reason every other list here does it: a
            // search from page three lands past the end of a shorter result.
            _ = ReloadAsync(0);
        }
    }

    public string Category => _category;

    /// <summary>The tabs: everything, then each category in reading order.</summary>
    public IReadOnlyList<string> CategoryOptions =>
        new[] { AllCategories }.Concat(_categories).ToList();

    public IReadOnlyList<HelpArticle> Articles => _page.Rows;

    public int Total => _page.Total;

    public int Page => _page.Page;

    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public bool IsFiltered =>
        !string.IsNullOrWhiteSpace(_search)
        || !string.Equals(_category, AllCategories, StringComparison.Ordinal);

    /// <summary>"Nothing matched" reads differently from "nothing here".</summary>
    public string EmptyMessage =>
        string.IsNullOrWhiteSpace(_search)
            ? $"Nothing under {_category} yet."
            : $"Nothing matches \"{_search.Trim()}\". Try a word a patient would use — "
                + "\"no-show\", \"sick note\", \"reminder\".";

    /// <summary>The article being read, if any.</summary>
    public HelpArticle? OpenArticle => _open;

    public bool IsReading => _open is not null;

    private async Task SetCategoryAsync(string category)
    {
        if (!SetProperty(ref _category, category, nameof(Category))) return;

        await ReloadAsync(0).ConfigureAwait(false);
    }

    private async Task OpenAsync(Guid articleId)
    {
        _open = await _help.GetAsync(articleId).ConfigureAwait(false);

        await RaisePropertyChanged(nameof(OpenArticle)).ConfigureAwait(false);
        await RaisePropertyChanged(nameof(IsReading)).ConfigureAwait(false);
    }

    private void Close()
    {
        _open = null;

        _ = RaisePropertyChanged(nameof(OpenArticle));
        _ = RaisePropertyChanged(nameof(IsReading));
    }

    /// <remarks>
    /// Not routed through the guarded loader, which does not nest — typing in the search box
    /// while a page load is in flight would otherwise drop the keystroke and leave the box
    /// showing a term the list was never filtered by.
    /// </remarks>
    private async Task ReloadAsync(int page)
    {
        var category = string.Equals(_category, AllCategories, StringComparison.Ordinal)
            ? null
            : _category;

        _page = await _help
            .GetPublishedAsync(_search, category, page, PageSize)
            .ConfigureAwait(false);

        foreach (var name in new[]
        {
            nameof(Articles), nameof(Total), nameof(Page), nameof(PageCount),
            nameof(IsFiltered), nameof(EmptyMessage), nameof(CategoryOptions),
        })
        {
            await RaisePropertyChanged(name).ConfigureAwait(false);
        }
    }
}
