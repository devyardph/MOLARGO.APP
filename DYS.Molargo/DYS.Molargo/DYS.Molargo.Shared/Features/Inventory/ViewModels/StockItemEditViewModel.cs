using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Shared.Features.Inventory.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Inventory.ViewModels;

/// <summary>Where the stock-item form is in its sequence.</summary>
public enum StockEditStage
{
    Editing = 0,
    Saved = 1,

    /// <summary>Asking whether to archive.</summary>
    ConfirmingArchive = 2,

    Archived = 3,
}

/// <summary>
/// Creating or editing one stock item — the prototype's own screen for it.
/// </summary>
/// <remarks>
/// One view model for both, taking the item's id or <see cref="Guid.Empty"/> for a new
/// one. The form is the same form; splitting it would mean two of every field.
/// </remarks>
public sealed class StockItemEditViewModel : BaseViewModel<Guid>
{
    private readonly IInventoryService _inventory;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;

    private Guid _id;
    private bool _openedAsNew = true;
    private bool _notFound;
    private StockItem _item = new();
    private IReadOnlyList<Supplier> _suppliers = [];
    private IReadOnlyList<StockCategory> _categories = [];
    private IReadOnlyList<StockMovement> _movements = [];
    private StockEditStage _stage = StockEditStage.Editing;

    private string? _openingBatch;
    private DateOnly? _openingExpiry;

    private readonly Dictionary<string, string> _errors = [];

    public StockItemEditViewModel(
        IInventoryService inventory,
        ISessionService session,
        IAppNavigator navigator)
    {
        _inventory = inventory;
        _session = session;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        SaveCommand = new MvxAsyncCommand(SaveAsync);
        BackCommand = new MvxCommand(() => _navigator.ToInventory());
        AskArchiveCommand = new MvxCommand(() => SetStage(StockEditStage.ConfirmingArchive));
        CancelArchiveCommand = new MvxCommand(() => SetStage(StockEditStage.Editing));
        ConfirmArchiveCommand = new MvxAsyncCommand(ArchiveAsync);
        SelectSupplierCommand = new MvxCommand<Guid>(SelectSupplier);
        SelectCategoryCommand = new MvxCommand<string>(SelectCategory);
        ToggleBatchCommand = new MvxCommand(ToggleBatch);
    }

    /// <summary>
    /// The categories the chips offer.
    /// </summary>
    /// <remarks>
    /// Read from the practice's own list under Inventory → Suppliers, not compiled in.
    /// The fixed array this replaced was right that free text splits one group into two
    /// spellings, and wrong about who decides: it made the vendor the authority on how a
    /// practice files its shelves.
    ///
    /// Active ones only. A retired category stays on the items already filed under it —
    /// see the stray-chip case below — but is not offered for new ones.
    /// </remarks>
    public IReadOnlyList<string> Categories =>
        _categories.Where(row => row.IsActive).Select(row => row.Name).ToList();

    public IMvxAsyncCommand SaveCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand AskArchiveCommand { get; }

    public IMvxCommand CancelArchiveCommand { get; }

    public IMvxAsyncCommand ConfirmArchiveCommand { get; }

    public IMvxCommand<Guid> SelectSupplierCommand { get; }

    public IMvxCommand<string> SelectCategoryCommand { get; }

    public IMvxCommand ToggleBatchCommand { get; }

    public override void Prepare(Guid parameter)
    {
        _id = parameter;
        _openedAsNew = parameter == Guid.Empty;
    }

    public override Task Initialize() => LoadAsync();

    public bool NotFound => _notFound;

    public StockItem Item => _item;

    public IReadOnlyList<Supplier> Suppliers => _suppliers;

    public IReadOnlyList<StockMovement> Movements => _movements;

    public StockEditStage Stage => _stage;

    public bool IsEditing => _stage == StockEditStage.Editing;

    public bool IsExisting => !_openedAsNew;

    public string Title => _openedAsNew ? "New stock item" : "Edit stock item";

    public string SavedMessage => _openedAsNew
        ? $"{_item.Name} added to stock."
        : $"{_item.Name} updated.";

    // ---- bound fields ----------------------------------------------------

    public string Name
    {
        get => _item.Name;
        set { _item.Name = value; RaisePropertyChanged(); }
    }

    public string? Sku
    {
        get => _item.Sku;
        set { _item.Sku = value; RaisePropertyChanged(); }
    }

    public string? Category
    {
        get => _item.Category;
        set { _item.Category = value; RaisePropertyChanged(); }
    }

    public string? Unit
    {
        get => _item.UnitOfMeasure;
        set { _item.UnitOfMeasure = value; RaisePropertyChanged(); }
    }

    public Guid? SupplierId => _item.SupplierId;

    public string? SupplierItemCode
    {
        get => _item.SupplierItemCode;
        set { _item.SupplierItemCode = value; RaisePropertyChanged(); }
    }

    /// <summary>
    /// The count. Editable only when creating.
    /// </summary>
    /// <remarks>
    /// Once an item exists its balance is the sum of its movements, so typing over it here
    /// would silently contradict the ledger. Correcting a count afterwards is a stocktake
    /// adjustment on the stock list, which leaves a record of who changed it and why.
    /// </remarks>
    public decimal OnHand
    {
        get => _item.QuantityOnHand;
        set { _item.QuantityOnHand = value; RaisePropertyChanged(); }
    }

    public bool CanEditOnHand => _openedAsNew;

    public decimal ReorderLevel
    {
        get => _item.ReorderLevel;
        set { _item.ReorderLevel = value; RaisePropertyChanged(); }
    }

    public decimal ReorderQuantity
    {
        get => _item.ReorderQuantity;
        set { _item.ReorderQuantity = value; RaisePropertyChanged(); }
    }

    public decimal? UnitCost
    {
        get => _item.UnitCost;
        set { _item.UnitCost = value; RaisePropertyChanged(); }
    }

    public bool RequiresBatchTracking => _item.RequiresBatchTracking;

    /// <summary>
    /// The batch on the shelf at the opening count. New items only.
    /// </summary>
    /// <remarks>
    /// Recorded against the opening movement, not the item. Later batches arrive by being
    /// received against a purchase order, which is where the delivery's own batch and
    /// expiry are entered — a batch field on the item would be overwritten by the next
    /// delivery and stop explaining what is actually on the shelf.
    /// </remarks>
    public string? OpeningBatch
    {
        get => _openingBatch;
        set { _openingBatch = value; RaisePropertyChanged(); }
    }

    public DateOnly? OpeningExpiry
    {
        get => _openingExpiry;
        set { _openingExpiry = value; RaisePropertyChanged(); }
    }

    /// <summary>
    /// Whether the batch fields show at all.
    /// </summary>
    /// <remarks>
    /// Only while creating. On an existing item the batches are whatever the movements
    /// say, and an editable box here would look like it changed them.
    /// </remarks>
    public bool ShowOpeningBatch => _openedAsNew && _item.RequiresBatchTracking;

    public IReadOnlyDictionary<string, string> Errors => _errors;

    public string? ErrorFor(string field) => _errors.GetValueOrDefault(field);

    // ---- behaviour -------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _suppliers = await _inventory.GetSuppliersAsync().ConfigureAwait(false);
        _categories = await _inventory.GetCategoriesAsync().ConfigureAwait(false);

        if (_openedAsNew)
        {
            _item = new StockItem
            {
                PracticeLocationId = _session.LocationId,
                UnitOfMeasure = "each",
                ReorderLevel = 2,
                ReorderQuantity = 5,
            };
        }
        else
        {
            var existing = await _inventory.GetItemAsync(_id).ConfigureAwait(false);

            if (existing is null)
            {
                _notFound = true;
                RaiseAll();
                return;
            }

            _item = existing;

            _movements = await _inventory.GetMovementsAsync(_id).ConfigureAwait(false);
        }

        RaiseAll();
    });

    private void SelectSupplier(Guid supplierId)
    {
        // Tapping the chosen supplier again clears it, which is the only way back to "no
        // supplier" once one is picked.
        _item.SupplierId = supplierId == Guid.Empty || _item.SupplierId == supplierId
            ? null
            : supplierId;

        RaisePropertyChanged(nameof(SupplierId));
    }

    private void SelectCategory(string? category)
    {
        _item.Category = string.IsNullOrWhiteSpace(category)
            || string.Equals(_item.Category, category, StringComparison.Ordinal)
                ? null
                : category;

        RaisePropertyChanged(nameof(Category));
    }

    private void ToggleBatch()
    {
        _item.RequiresBatchTracking = !_item.RequiresBatchTracking;

        RaisePropertyChanged(nameof(RequiresBatchTracking));
        RaisePropertyChanged(nameof(ShowOpeningBatch));
    }

    private Task SaveAsync() => RunGuardedAsync(async () =>
    {
        _errors.Clear();

        if (string.IsNullOrWhiteSpace(_item.Name))
        {
            _errors[nameof(Name)] = "Give the item a name.";
        }

        if (_item.ReorderLevel < 0m || _item.ReorderQuantity < 0m)
        {
            _errors[nameof(ReorderLevel)] = "Reorder figures cannot be negative.";
        }

        if (_openedAsNew && _item.QuantityOnHand < 0m)
        {
            _errors[nameof(OnHand)] = "An opening count cannot be negative.";
        }

        // The same guard MoveStockAsync applies to a receipt, applied to the opening count.
        // A batch-tracked item whose first movement has no batch breaks the recall chain
        // for everything it is used on before the next delivery.
        if (ShowOpeningBatch && _item.QuantityOnHand > 0m
            && string.IsNullOrWhiteSpace(_openingBatch))
        {
            _errors[nameof(OpeningBatch)] =
                "Batch-tracked. Record the batch that is on the shelf now.";
        }

        if (_errors.Count > 0)
        {
            RaiseAll();
            return;
        }

        _item.PracticeLocationId = _session.LocationId;
        _item.Name = _item.Name.Trim();

        // The refusal is read, not discarded. The service can now decline this — stock
        // costs and suppliers need the inventory permission — and ignoring the return
        // would have shown "Saved." over a save that did not happen.
        var refusal = await _inventory
            .SaveItemAsync(_item, _openingBatch, _openingExpiry)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _stage = StockEditStage.Saved;

        RaiseAll();
    });

    private Task ArchiveAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _inventory.ArchiveItemAsync(_item.Id).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _stage = StockEditStage.Archived;

        RaiseAll();
    });

    private void SetStage(StockEditStage stage)
    {
        _stage = stage;

        RaisePropertyChanged(nameof(Stage));
        RaisePropertyChanged(nameof(IsEditing));
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(NotFound), nameof(Item), nameof(Suppliers), nameof(Categories),
            nameof(Movements),
            nameof(Stage), nameof(IsEditing), nameof(IsExisting), nameof(Title),
            nameof(SavedMessage), nameof(Name), nameof(Sku), nameof(Category),
            nameof(Unit), nameof(SupplierId), nameof(SupplierItemCode), nameof(OnHand),
            nameof(CanEditOnHand), nameof(ReorderLevel), nameof(ReorderQuantity),
            nameof(UnitCost), nameof(RequiresBatchTracking), nameof(ShowOpeningBatch),
            nameof(OpeningBatch), nameof(OpeningExpiry), nameof(Errors),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
