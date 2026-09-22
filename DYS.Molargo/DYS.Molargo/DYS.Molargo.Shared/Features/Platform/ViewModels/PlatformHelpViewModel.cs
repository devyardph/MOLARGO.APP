using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Help.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Platform.ViewModels;

/// <summary>
/// The vendor's side of the help knowledge base: writing the articles every practice reads.
/// </summary>
/// <remarks>
/// Its own screen under Platform rather than a mode on the practice's Help page. The two
/// audiences are different people with different rights, and a page that grew an Edit button
/// for one account in a thousand would be a page every other account has to ignore.
/// </remarks>
public sealed class PlatformHelpViewModel : BaseViewModel
{
    /// <summary>Rows a page, matching every other list in the app.</summary>
    public const int PageSize = 17;

    private readonly IHelpService _help;
    private readonly ISessionService _session;

    private HelpPage _page = new([], 0, 0, PageSize);
    private string? _search;
    private string? _lastAction;

    private HelpArticle? _editing;
    private bool _isNew;

    public PlatformHelpViewModel(IHelpService help, ISessionService session)
    {
        _help = help;
        _session = session;

        NewArticleCommand = new MvxCommand(NewArticle);
        EditCommand = new MvxAsyncCommand<Guid>(EditAsync);
        CloseCommand = new MvxCommand(Close);
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        DeleteCommand = new MvxAsyncCommand<Guid>(DeleteAsync);
        PreviousPageCommand = new MvxAsyncCommand(() => ReloadAsync(Page - 1));
        NextPageCommand = new MvxAsyncCommand(() => ReloadAsync(Page + 1));
        GoToPageCommand = new MvxAsyncCommand<int>(ReloadAsync);
    }

    public IMvxCommand NewArticleCommand { get; }

    public IMvxAsyncCommand<Guid> EditCommand { get; }

    public IMvxCommand CloseCommand { get; }

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxAsyncCommand<Guid> DeleteCommand { get; }

    public IMvxAsyncCommand PreviousPageCommand { get; }

    public IMvxAsyncCommand NextPageCommand { get; }

    public IMvxAsyncCommand<int> GoToPageCommand { get; }

    public override Task Initialize() => RunGuardedAsync(() => ReloadAsync(0));

    /// <summary>Vendor only, like every other screen under Platform.</summary>
    public bool IsPermitted => _session.IsSuperAdmin;

    public string ActingAs => _session.UserDisplayName ?? "nobody";

    public IReadOnlyList<HelpArticle> Articles => _page.Rows;

    public int Total => _page.Total;

    public int Page => _page.Page;

    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public string? LastAction => _lastAction;

    public bool IsFiltered => !string.IsNullOrWhiteSpace(_search);

    public string EmptyMessage => IsFiltered
        ? $"Nothing matches \"{_search?.Trim()}\"."
        : "No articles yet.";

    /// <summary>What is being searched for, applied as it is typed.</summary>
    public string? Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value)) return;

            _ = ReloadAsync(0);
        }
    }

    // ---- the editor ------------------------------------------------------

    public bool IsEditing => _editing is not null;

    public bool IsNewArticle => _isNew;

    public string EditorTitle => _isNew ? "New article" : "Edit article";

    public string? ArticleTitle
    {
        get => _editing?.Title;
        set
        {
            if (_editing is null) return;

            _editing.Title = value ?? string.Empty;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanSave));
        }
    }

    /// <summary>
    /// The address, which is not regenerated from the title.
    /// </summary>
    /// <remarks>
    /// Left blank on a new article and filled in from the title by the service. On an
    /// existing one it is shown and editable but not derived — an article that has been
    /// published has a slug people may have linked to, and rewording the title must not
    /// break those links.
    /// </remarks>
    public string? Slug
    {
        get => _editing?.Slug;
        set
        {
            if (_editing is null) return;

            _editing.Slug = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? Category
    {
        get => _editing?.Category;
        set
        {
            if (_editing is null) return;

            _editing.Category = value ?? string.Empty;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanSave));
        }
    }

    public string? Summary
    {
        get => _editing?.Summary;
        set
        {
            if (_editing is null) return;

            _editing.Summary = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? Body
    {
        get => _editing?.Body;
        set
        {
            if (_editing is null) return;

            _editing.Body = value ?? string.Empty;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanSave));
        }
    }

    public string? Keywords
    {
        get => _editing?.Keywords;
        set
        {
            if (_editing is null) return;

            _editing.Keywords = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public int DisplayOrder
    {
        get => _editing?.DisplayOrder ?? 0;
        set
        {
            if (_editing is null) return;

            _editing.DisplayOrder = value;
            RaisePropertyChanged();
        }
    }

    public bool IsPublished => _editing?.IsPublished ?? false;

    public bool CanSave =>
        !IsBusy
        && _editing is { } article
        && !string.IsNullOrWhiteSpace(article.Title)
        && !string.IsNullOrWhiteSpace(article.Category)
        && !string.IsNullOrWhiteSpace(article.Body);

    /// <summary>The categories already in use, offered so new ones are deliberate.</summary>
    public IReadOnlyList<string> KnownCategories { get; private set; } = [];

    private void NewArticle()
    {
        _editing = new HelpArticle { IsPublished = false };
        _isNew = true;

        ErrorMessage = null;
        RaiseAll();
    }

    private async Task EditAsync(Guid articleId)
    {
        _editing = await _help.GetAsync(articleId).ConfigureAwait(false);
        _isNew = false;

        ErrorMessage = null;
        RaiseAll();
    }

    private void Close()
    {
        _editing = null;
        _isNew = false;

        ErrorMessage = null;
        RaiseAll();
    }

    /// <summary>Switches published on or off without leaving the editor.</summary>
    public void TogglePublished()
    {
        if (_editing is null) return;

        _editing.IsPublished = !_editing.IsPublished;

        _ = RaisePropertyChanged(nameof(IsPublished));
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is not { } article) return;

        var refusal = await _help.SaveAsync(article).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"\"{article.Title}\" saved.";
        _editing = null;
        _isNew = false;

        ErrorMessage = null;
        await ReloadAsync(Page).ConfigureAwait(false);
    });

    private Task DeleteAsync(Guid articleId) => RunGuardedAsync(async () =>
    {
        var refusal = await _help.DeleteAsync(articleId).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        // The editor can be holding the row that just went. Cleared rather than left
        // pointing at it, or Save would rewrite a deleted article back into existence.
        if (_editing?.Id == articleId) Close();

        _lastAction = "Article removed.";

        await ReloadAsync(Page).ConfigureAwait(false);
    });

    /// <remarks>
    /// Not routed through the guarded loader, which does not nest — every command above is
    /// already inside one.
    /// </remarks>
    private async Task ReloadAsync(int page)
    {
        _page = await _help.GetAllAsync(_search, page, PageSize).ConfigureAwait(false);

        KnownCategories = await _help
            .GetCategoriesAsync(publishedOnly: false)
            .ConfigureAwait(false);

        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(IsPermitted), nameof(ActingAs), nameof(Articles), nameof(Total),
            nameof(Page), nameof(PageCount), nameof(LastAction), nameof(IsFiltered),
            nameof(EmptyMessage), nameof(Search), nameof(IsEditing), nameof(IsNewArticle),
            nameof(EditorTitle), nameof(ArticleTitle), nameof(Slug), nameof(Category),
            nameof(Summary), nameof(Body), nameof(Keywords), nameof(DisplayOrder),
            nameof(IsPublished), nameof(CanSave), nameof(KnownCategories), nameof(HasError),
        })
        {
            _ = RaisePropertyChanged(name);
        }
    }
}
