using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Comms.Services;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Comms.ViewModels;

/// <summary>Which pane of the comms screen is showing.</summary>
public enum CommsTab
{
    Templates = 0,
    Campaigns = 1,
    Automated = 2,
    Reviews = 3,
    Inbox = 4,
    Preferences = 5,
}

/// <summary>
/// Message templates, campaign audiences, the portal inbox and communication consent.
/// </summary>
public sealed class CommsViewModel : BaseViewModel, IDisposable
{
    private readonly ICommsService _comms;
    private readonly ISessionService _session;
    private readonly IAppNavigator _navigator;

    private CommsTab _tab = CommsTab.Templates;
    private string? _lastAction;

    private IReadOnlyList<TemplateRow> _templates = [];
    private MessageTemplate? _editing;
    private TemplatePreview? _preview;

    private string _segmentKey = CommsService.Segments[0].Key;
    private CommunicationChannel _campaignChannel = CommunicationChannel.Sms;
    private CampaignAudience? _audience;
    private bool _showExcluded;

    private IReadOnlyList<InboxThread> _inbox = [];
    private Guid? _openThreadId;
    private string? _replyDraft;

    private IReadOnlyList<ConsentRow> _consent = [];
    private string? _consentSearch;
    private Guid? _editingConsentId;
    private bool _draftReminderConsent;
    private bool _draftMarketingConsent;
    private string? _draftConsentSource;
    private CommsActivity? _activity;

    public CommsViewModel(
        ICommsService comms,
        ISessionService session,
        IAppNavigator navigator)
    {
        _comms = comms;
        _session = session;
        _navigator = navigator;

        // Built once, in the constructor — never rebuilt per render.
        SelectTabCommand = new MvxAsyncCommand<CommsTab>(SelectTabAsync);

        SelectTemplateCommand = new MvxAsyncCommand<Guid>(SelectTemplateAsync);
        InsertMergeFieldCommand = new MvxAsyncCommand<string>(InsertMergeFieldAsync);
        RefreshPreviewCommand = new MvxAsyncCommand(RefreshPreviewAsync);
        SaveTemplateCommand = new MvxAsyncCommand(SaveTemplateAsync);
        SetTemplateChannelCommand = new MvxAsyncCommand<CommunicationChannel>(SetTemplateChannelAsync);
        ToggleTemplateActiveCommand = new MvxAsyncCommand<Guid>(ToggleTemplateActiveAsync);

        SetSegmentCommand = new MvxAsyncCommand<string>(SetSegmentAsync);
        SetCampaignChannelCommand = new MvxAsyncCommand<CommunicationChannel>(SetCampaignChannelAsync);
        ToggleExcludedCommand = new MvxCommand(ToggleExcluded);

        OpenThreadCommand = new MvxCommand<Guid>(SelectThread);
        SendReplyCommand = new MvxAsyncCommand(SendReplyAsync);

        SearchConsentCommand = new MvxAsyncCommand(LoadAsync);
        StartConsentEditCommand = new MvxCommand<ConsentRow>(row => StartConsentEdit(row!));
        CancelConsentEditCommand = new MvxCommand(() => StartConsentEdit(null));
        ToggleDraftReminderCommand = new MvxCommand(ToggleDraftReminder);
        ToggleDraftMarketingCommand = new MvxCommand(ToggleDraftMarketing);
        SetConsentSourceCommand = new MvxCommand<string>(SetConsentSource);
        ApplyConsentCommand = new MvxAsyncCommand(ApplyConsentAsync);

        OpenPatientCommand = new MvxCommand<Guid>(id => _navigator.ToPatientRecord(id));

        _session.Changed += OnSessionChanged;
    }

    public IMvxAsyncCommand<CommsTab> SelectTabCommand { get; }

    public IMvxAsyncCommand<Guid> SelectTemplateCommand { get; }

    public IMvxAsyncCommand<string> InsertMergeFieldCommand { get; }

    public IMvxAsyncCommand RefreshPreviewCommand { get; }

    public IMvxAsyncCommand SaveTemplateCommand { get; }

    public IMvxAsyncCommand<CommunicationChannel> SetTemplateChannelCommand { get; }

    public IMvxAsyncCommand<Guid> ToggleTemplateActiveCommand { get; }

    public IMvxAsyncCommand<string> SetSegmentCommand { get; }

    public IMvxAsyncCommand<CommunicationChannel> SetCampaignChannelCommand { get; }

    public IMvxCommand ToggleExcludedCommand { get; }

    public IMvxCommand<Guid> OpenThreadCommand { get; }

    public IMvxAsyncCommand SendReplyCommand { get; }

    public IMvxAsyncCommand SearchConsentCommand { get; }

    public IMvxCommand<ConsentRow> StartConsentEditCommand { get; }

    public IMvxCommand CancelConsentEditCommand { get; }

    public IMvxCommand ToggleDraftReminderCommand { get; }

    public IMvxCommand ToggleDraftMarketingCommand { get; }

    public IMvxCommand<string> SetConsentSourceCommand { get; }

    public IMvxAsyncCommand ApplyConsentCommand { get; }

    public IMvxCommand<Guid> OpenPatientCommand { get; }

    public override Task Initialize() => LoadAsync();

    public CommsTab Tab => _tab;

    public string? LastAction => _lastAction;

    // ---- templates -------------------------------------------------------

    public IReadOnlyList<TemplateRow> Templates => _templates;

    public MessageTemplate? Editing => _editing;

    public bool HasTemplate => _editing is not null;

    public TemplatePreview? Preview => _preview;

    /// <summary>
    /// The merge fields the chips offer, for whichever template is open.
    /// </summary>
    /// <remarks>
    /// No longer static, and no longer always the patient list. A staff notice — a password
    /// reset — resolves an entirely different set of tokens, and offering the patient
    /// chips on one would let somebody drop {{PatientFirstName}} into an email to a
    /// colleague, where nothing can resolve it and the literal braces go out.
    /// </remarks>
    public IReadOnlyList<MergeFields.Field> MergeFieldCatalogue =>
        MergeFields.For(_editing?.Purpose ?? MessagePurpose.AppointmentReminder);

    /// <summary>Templates that carry a trigger — the automated list.</summary>
    public IReadOnlyList<TemplateRow> Automated => _templates
        .Where(row => row.Trigger != MessageTrigger.Manual)
        .ToList();

    public int ActiveAutomatedCount => Automated.Count(row => row.IsActive);

    public string? TemplateName
    {
        get => _editing?.Name;
        set
        {
            if (_editing is null) return;

            _editing.Name = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string? TemplateSubject
    {
        get => _editing?.Subject;
        set
        {
            if (_editing is null) return;

            _editing.Subject = value;
            RaisePropertyChanged();
        }
    }

    public string? TemplateBody
    {
        get => _editing?.Body;
        set
        {
            if (_editing is null) return;

            _editing.Body = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// How long the rendered SMS is, and how many segments that bills as.
    /// </summary>
    /// <remarks>
    /// Counted on the rendered text, not the template: <c>{{PatientFirstName}}</c> is
    /// twenty characters in the body and seven in the message, and a template judged on
    /// its own length silently bills as two segments for every send.
    /// </remarks>
    public int PreviewLength => _preview?.Body.Length ?? 0;

    public int SmsSegments => PreviewLength == 0
        ? 0
        : (PreviewLength + SmsSegmentLength - 1) / SmsSegmentLength;

    /// <summary>Characters in one GSM-7 SMS part.</summary>
    public const int SmsSegmentLength = 160;

    public bool IsSmsTemplate => _editing?.Channel == CommunicationChannel.Sms;

    // ---- campaigns -------------------------------------------------------

    public static IReadOnlyList<SegmentDefinition> Segments => CommsService.Segments;

    public string SegmentKey => _segmentKey;

    public SegmentDefinition? Segment => CommsService.Segments
        .FirstOrDefault(segment => segment.Key == _segmentKey);

    public CommunicationChannel CampaignChannel => _campaignChannel;

    public CampaignAudience? Audience => _audience;

    public bool ShowExcluded => _showExcluded;

    /// <summary>
    /// The channels a bulk send could use.
    /// </summary>
    /// <remarks>
    /// SMS and email only. Letters and phone calls are recorded by hand on the patient's
    /// record, and offering them here would imply the practice can bulk-post from a
    /// button.
    /// </remarks>
    public static readonly CommunicationChannel[] CampaignChannels =
    [
        CommunicationChannel.Sms,
        CommunicationChannel.Email,
    ];

    // ---- inbox -----------------------------------------------------------

    public IReadOnlyList<InboxThread> Inbox => _inbox;

    public int AwaitingReplyCount => _inbox.Count(thread => thread.AwaitingReply);

    public InboxThread? OpenThread => _inbox
        .FirstOrDefault(thread => thread.PatientId == _openThreadId);

    public bool IsThreadOpen(Guid patientId) => _openThreadId == patientId;

    public string? ReplyDraft
    {
        get => _replyDraft;
        set => SetProperty(ref _replyDraft, value);
    }

    // ---- consent ---------------------------------------------------------

    public IReadOnlyList<ConsentRow> Consent => _consent;

    public CommsActivity? Activity => _activity;

    public int OptedOutCount => _consent.Count(row => !row.MarketingConsent);

    public int SourceMissingCount => _consent.Count(row => row.SourceMissing);

    public string? ConsentSearch
    {
        get => _consentSearch;
        set => SetProperty(ref _consentSearch, value);
    }

    public bool IsEditingConsent(Guid patientId) => _editingConsentId == patientId;

    public bool DraftReminderConsent => _draftReminderConsent;

    public bool DraftMarketingConsent => _draftMarketingConsent;

    public string? DraftConsentSource
    {
        get => _draftConsentSource;
        set => SetProperty(ref _draftConsentSource, value);
    }

    /// <summary>The provenances the chips offer, matching the design's wording.</summary>
    public static readonly string[] ConsentSources =
    [
        "Signed form · tablet",
        "Portal settings",
        "Phone request · logged",
        "In person · front desk",
    ];

    // ---- loading ---------------------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(LoadTabAsync);

    private async Task SelectTabAsync(CommsTab tab)
    {
        _tab = tab;
        _lastAction = null;

        await RaisePropertyChanged(nameof(Tab)).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    private async Task LoadTabAsync()
    {
        switch (_tab)
        {
            case CommsTab.Templates:
            case CommsTab.Automated:
                _templates = await _comms.GetTemplatesAsync().ConfigureAwait(false);

                // Templates and Automated are two views of one list, so the editor stays
                // open across a tab switch rather than being reloaded from scratch.
                if (_editing is null && _templates.Count > 0 && _tab == CommsTab.Templates)
                {
                    await LoadTemplateAsync(_templates[0].TemplateId).ConfigureAwait(false);
                }

                break;

            case CommsTab.Campaigns:
                _audience = await _comms
                    .GetAudienceAsync(_session.LocationId, _segmentKey, _campaignChannel)
                    .ConfigureAwait(false);
                break;

            case CommsTab.Inbox:
                _inbox = await _comms.GetInboxAsync().ConfigureAwait(false);

                _openThreadId ??= _inbox.Count > 0 ? _inbox[0].PatientId : null;
                break;

            case CommsTab.Preferences:
                _consent = await _comms
                    .GetConsentAsync(_session.LocationId, _consentSearch)
                    .ConfigureAwait(false);

                _activity = await _comms
                    .GetActivityAsync(_session.LocationId)
                    .ConfigureAwait(false);
                break;
        }

        RaiseAll();
    }

    // ---- templates -------------------------------------------------------

    private Task SelectTemplateAsync(Guid templateId) =>
        RunGuardedAsync(() => LoadTemplateAsync(templateId));

    /// <summary>
    /// Unguarded, so it can be called from inside an already-guarded command.
    /// </summary>
    /// <remarks>
    /// <c>RunGuardedAsync</c> returns immediately when it is already busy, so a guarded
    /// method calling another guarded method is a silent no-op. The wrapper above is the
    /// only guarded entry point.
    /// </remarks>
    private async Task LoadTemplateAsync(Guid templateId)
    {
        _editing = await _comms.GetTemplateAsync(templateId).ConfigureAwait(false);
        _lastAction = null;

        await RenderPreviewAsync().ConfigureAwait(false);

        RaiseAll();
    }

    private async Task RenderPreviewAsync()
    {
        if (_editing is null)
        {
            _preview = null;
            return;
        }

        _preview = await _comms
            .PreviewAsync(
                _session.LocationId,
                _editing.Channel,
                _editing.Subject,
                _editing.Body)
            .ConfigureAwait(false);
    }

    private Task RefreshPreviewAsync() => RunGuardedAsync(async () =>
    {
        await RenderPreviewAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task InsertMergeFieldAsync(string? token) => RunGuardedAsync(async () =>
    {
        if (_editing is null || string.IsNullOrWhiteSpace(token)) return;

        // Appended rather than inserted at the caret. Reading the caret needs JS interop,
        // which is unavailable during prerender on the web head — and a token dropped at
        // the end is easier to move than one dropped in the wrong place.
        _editing.Body = _editing.Body.TrimEnd() + " " + MergeFields.Placeholder(token);

        await RenderPreviewAsync().ConfigureAwait(false);

        RaiseAll();
    });

    private Task SetTemplateChannelAsync(CommunicationChannel channel) =>
        RunGuardedAsync(async () =>
        {
            if (_editing is null) return;

            _editing.Channel = channel;

            await RenderPreviewAsync().ConfigureAwait(false);

            RaiseAll();
        });

    private Task SaveTemplateAsync() => RunGuardedAsync(async () =>
    {
        if (_editing is null) return;

        var refusal = await _comms.SaveTemplateAsync(_editing).ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = $"\"{_editing.Name}\" saved. Nothing was sent.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task ToggleTemplateActiveAsync(Guid templateId) => RunGuardedAsync(async () =>
    {
        var row = _templates.FirstOrDefault(entry => entry.TemplateId == templateId);
        if (row is null) return;

        var refusal = await _comms
            .SetTemplateActiveAsync(templateId, !row.IsActive)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = row.IsActive
            ? $"\"{row.Name}\" turned off."
            : $"\"{row.Name}\" turned on — but nothing dispatches it yet.";

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- campaigns -------------------------------------------------------

    private Task SetSegmentAsync(string? key) => RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _segmentKey = key;
        _showExcluded = false;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private Task SetCampaignChannelAsync(CommunicationChannel channel) =>
        RunGuardedAsync(async () =>
        {
            _campaignChannel = channel;

            await LoadTabAsync().ConfigureAwait(false);
        });

    private void ToggleExcluded()
    {
        _showExcluded = !_showExcluded;

        RaisePropertyChanged(nameof(ShowExcluded));
    }

    // ---- inbox -----------------------------------------------------------

    private void SelectThread(Guid patientId)
    {
        _openThreadId = patientId;
        _replyDraft = null;
        _lastAction = null;

        RaiseAll();
    }

    private Task SendReplyAsync() => RunGuardedAsync(async () =>
    {
        if (_openThreadId is not { } patientId) return;

        var refusal = await _comms
            .ReplyAsync(patientId, _replyDraft ?? string.Empty, _session.ProviderId)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Reply recorded on the patient's record. It was not delivered — "
            + "there is no portal to send it to.";

        _replyDraft = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    // ---- consent ---------------------------------------------------------

    private void StartConsentEdit(ConsentRow? row)
    {
        _editingConsentId = row?.PatientId;
        _draftReminderConsent = row?.ReminderConsent ?? true;
        _draftMarketingConsent = row?.MarketingConsent ?? false;
        _draftConsentSource = row?.ConsentSource;
        _lastAction = null;

        RaiseAll();
    }

    private void ToggleDraftReminder()
    {
        _draftReminderConsent = !_draftReminderConsent;

        RaisePropertyChanged(nameof(DraftReminderConsent));
    }

    private void ToggleDraftMarketing()
    {
        _draftMarketingConsent = !_draftMarketingConsent;

        RaisePropertyChanged(nameof(DraftMarketingConsent));
    }

    /// <summary>
    /// Picks a provenance, or clears it by tapping the chosen one again.
    /// </summary>
    /// <remarks>
    /// Clearing matters. Assigning the chip's value directly left no way back to "no
    /// source", so the guard that refuses to grant consent without one could not be
    /// reached — the form silently carried whatever source the patient already had.
    /// </remarks>
    private void SetConsentSource(string? source)
    {
        _draftConsentSource = string.IsNullOrWhiteSpace(source)
            || string.Equals(_draftConsentSource, source, StringComparison.Ordinal)
                ? null
                : source;

        RaisePropertyChanged(nameof(DraftConsentSource));
    }

    private Task ApplyConsentAsync() => RunGuardedAsync(async () =>
    {
        if (_editingConsentId is not { } patientId) return;

        var refusal = await _comms
            .SetConsentAsync(
                patientId,
                _draftReminderConsent,
                _draftMarketingConsent,
                _draftConsentSource)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _lastAction = "Consent updated, stamped with today's date and its source.";
        _editingConsentId = null;

        await LoadTabAsync().ConfigureAwait(false);
    });

    private void OnSessionChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _session.Changed -= OnSessionChanged;

    /// <summary>The words staff use for a channel, not the enum's.</summary>
    public static string ChannelLabel(CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.Sms => "SMS",
        CommunicationChannel.Email => "Email",
        CommunicationChannel.Phone => "Phone",
        CommunicationChannel.Letter => "Letter",
        CommunicationChannel.PatientPortal => "Portal",
        CommunicationChannel.InPerson => "In person",
        _ => channel.ToString(),
    };

    public static string PurposeLabel(MessagePurpose purpose) => purpose switch
    {
        MessagePurpose.AppointmentReminder => "Appointment reminder",
        MessagePurpose.Recall => "Recall",
        MessagePurpose.Aftercare => "Aftercare",
        MessagePurpose.AccountNotice => "Account notice",
        MessagePurpose.TreatmentFollowUp => "Treatment follow-up",
        MessagePurpose.Marketing => "Marketing",
        MessagePurpose.SecurityNotice => "Security notice",
        _ => "General",
    };

    /// <summary>
    /// Everything the screen reads. Listed explicitly rather than raising a blanket change,
    /// which would re-render the body being typed in.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Tab), nameof(LastAction), nameof(Templates), nameof(Editing),
            nameof(HasTemplate), nameof(Preview), nameof(Automated),
            nameof(ActiveAutomatedCount), nameof(TemplateName), nameof(TemplateSubject),
            nameof(TemplateBody), nameof(PreviewLength), nameof(SmsSegments),
            nameof(IsSmsTemplate), nameof(SegmentKey), nameof(Segment),
            nameof(CampaignChannel), nameof(Audience), nameof(ShowExcluded),
            nameof(Inbox), nameof(AwaitingReplyCount), nameof(OpenThread),
            nameof(ReplyDraft), nameof(Consent), nameof(Activity), nameof(OptedOutCount),
            nameof(SourceMissingCount), nameof(ConsentSearch), nameof(DraftReminderConsent),
            nameof(DraftMarketingConsent), nameof(DraftConsentSource),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
