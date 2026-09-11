using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Auth.Services;
using DYS.Molargo.Shared.ViewModels;
using MvvmCross.Commands;

namespace DYS.Molargo.Shared.Features.Auth.ViewModels;

/// <summary>
/// The three-step practice signup, plus the panel shown once it is done.
/// </summary>
/// <remarks>
/// The design's three progress dots are these first three. <see cref="Done"/> is not a
/// fourth step and gets no dot: it is the outcome, and it carries the clinic code and
/// username the practice has no other way to learn.
/// </remarks>
public enum SignupStep
{
    Practice = 0,
    About = 1,
    Secure = 2,
    Done = 3,
}

/// <summary>Self-service practice signup, as the reference lays it out.</summary>
public sealed class CreatePracticeViewModel : BaseViewModel
{
    private readonly IRegistrationService _registration;

    private SignupStep _step = SignupStep.Practice;
    private IReadOnlyList<string> _countries = [];

    private string? _practiceName;
    private PracticeSize _size = PracticeSize.Single;
    private string? _country;

    private string? _fullName;
    private string? _email;
    private ProviderRole _role = ProviderRole.Dentist;

    private string? _password;
    private bool _showPassword;
    private bool _acceptedTerms;

    private PracticeCreated? _result;

    public CreatePracticeViewModel(IRegistrationService registration)
    {
        _registration = registration;

        NextCommand = new MvxCommand(Next);
        BackCommand = new MvxCommand(Back);
        SetSizeCommand = new MvxCommand<PracticeSize>(SetSize);
        SetRoleCommand = new MvxCommand<ProviderRole>(SetRole);
        ToggleTermsCommand = new MvxCommand(ToggleTerms);
        ToggleShowPasswordCommand = new MvxCommand(ToggleShowPassword);
        CreateCommand = new MvxAsyncCommand(CreateAsync);
    }

    public IMvxCommand NextCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand<PracticeSize> SetSizeCommand { get; }

    public IMvxCommand<ProviderRole> SetRoleCommand { get; }

    public IMvxCommand ToggleTermsCommand { get; }

    public IMvxCommand ToggleShowPasswordCommand { get; }

    public IMvxAsyncCommand CreateCommand { get; }

    public override Task Initialize() => LoadAsync();

    public SignupStep Step => _step;

    public bool IsStep(SignupStep step) => _step == step;

    /// <summary>The three dots. Absent once the practice exists.</summary>
    public static readonly SignupStep[] Dots =
        [SignupStep.Practice, SignupStep.About, SignupStep.Secure];

    public bool ShowsDots => _step != SignupStep.Done;

    /// <summary>Whether a dot is the step showing, or one already passed.</summary>
    public bool DotIsReached(SignupStep step) => (int)step <= (int)_step;

    /// <summary>How long the trial runs, read from the service that grants it.</summary>
    /// <remarks>
    /// Not typed into the screen as "30". The promise on the page and the date written to
    /// the record have to be the same number, and two copies of it would eventually not be.
    /// </remarks>
    public static int TrialDays => RegistrationService.TrialDays;

    // ---- step one: the practice ------------------------------------------

    public string? PracticeName
    {
        get => _practiceName;
        set => SetProperty(ref _practiceName, value);
    }

    public PracticeSize Size => _size;

    public bool IsSize(PracticeSize size) => _size == size;

    public static readonly PracticeSize[] Sizes = Enum.GetValues<PracticeSize>();

    public static string SizeLabel(PracticeSize size) => size switch
    {
        PracticeSize.Single => "Single location",
        PracticeSize.SmallGroup => "2–5 locations",
        _ => "6+ group",
    };

    public IReadOnlyList<string> Countries => _countries;

    public string? Country
    {
        get => _country;
        set => SetProperty(ref _country, value);
    }

    /// <summary>
    /// The country's name, for a picker a practice owner reads.
    /// </summary>
    /// <remarks>
    /// Only the countries the vendor has priced appear, so this list is short by
    /// construction rather than by being trimmed. An unrecognised code falls back to
    /// itself, which is honest — better a bare "SG" than a wrong country name.
    /// </remarks>
    public static string CountryName(string code) => code switch
    {
        "AU" => "Australia",
        "NZ" => "New Zealand",
        "GB" => "United Kingdom",
        "PH" => "Philippines",
        _ => code,
    };

    public bool CanLeavePractice =>
        !string.IsNullOrWhiteSpace(_practiceName) && !string.IsNullOrWhiteSpace(_country);

    // ---- step two: about you ---------------------------------------------

    public string? FullName
    {
        get => _fullName;
        set => SetProperty(ref _fullName, value);
    }

    public string? Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public ProviderRole Role => _role;

    public bool IsRole(ProviderRole role) => _role == role;

    /// <summary>
    /// The three roles the signup offers.
    /// </summary>
    /// <remarks>
    /// Not every role — a practice's full list belongs on the Users screen once they are
    /// in. These are the three people who actually sign a practice up, and the choice
    /// matters because only the first two can author a clinical record.
    /// </remarks>
    public static readonly ProviderRole[] Roles =
        [ProviderRole.Dentist, ProviderRole.Specialist, ProviderRole.Administration];

    public static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Dentist => "Dentist",
        ProviderRole.Specialist => "Specialist",
        _ => "Practice manager",
    };

    /// <summary>
    /// True where the chosen role cannot author clinical records.
    /// </summary>
    /// <remarks>
    /// Surfaced on the step, not after. A practice manager signing up alone ends with a
    /// clinic nobody can write a note or a script in, and finding that out on the first
    /// morning is worse than reading it here.
    /// </remarks>
    public bool RoleIsNonClinical => !Domain.ProviderRoles.IsClinical(_role);

    public bool CanLeaveAbout =>
        !string.IsNullOrWhiteSpace(_fullName) && !string.IsNullOrWhiteSpace(_email);

    // ---- step three: secure it -------------------------------------------

    public string? Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool ShowPassword => _showPassword;

    public bool AcceptedTerms => _acceptedTerms;

    public bool CanCreate =>
        !IsBusy
        && CanLeavePractice
        && CanLeaveAbout
        && !string.IsNullOrWhiteSpace(_password)
        && _acceptedTerms;

    // ---- done ------------------------------------------------------------

    public PracticeCreated? Result => _result;

    public string? ClinicCode => _result?.ClinicCode;

    public string? Username => _result?.Username;

    public string? PlanName => _result?.PlanName;

    public DateOnly? TrialEndsOn => _result?.TrialEndsOn;

    // ---- moving between steps --------------------------------------------

    private Task LoadAsync() => RunGuardedAsync(async () =>
    {
        _countries = await _registration.GetCountriesAsync().ConfigureAwait(false);

        // Australia first where it is priced, since that is the reference's default and
        // the vendor's home market — otherwise whatever exists.
        _country ??= _countries.Contains("AU") ? "AU" : _countries.FirstOrDefault();

        RaiseAll();
    });

    private void Next()
    {
        _step = _step switch
        {
            SignupStep.Practice when CanLeavePractice => SignupStep.About,
            SignupStep.About when CanLeaveAbout => SignupStep.Secure,
            _ => _step,
        };

        ErrorMessage = null;

        RaiseAll();
    }

    private void Back()
    {
        _step = _step switch
        {
            SignupStep.Secure => SignupStep.About,
            SignupStep.About => SignupStep.Practice,
            _ => _step,
        };

        ErrorMessage = null;

        RaiseAll();
    }

    private void SetSize(PracticeSize size)
    {
        _size = size;

        RaisePropertyChanged(nameof(Size));
    }

    private void SetRole(ProviderRole role)
    {
        _role = role;

        RaisePropertyChanged(nameof(Role));
        RaisePropertyChanged(nameof(RoleIsNonClinical));
    }

    private void ToggleTerms()
    {
        _acceptedTerms = !_acceptedTerms;

        RaisePropertyChanged(nameof(AcceptedTerms));
        RaisePropertyChanged(nameof(CanCreate));
    }

    private void ToggleShowPassword()
    {
        _showPassword = !_showPassword;

        RaisePropertyChanged(nameof(ShowPassword));
    }

    private Task CreateAsync() => RunGuardedAsync(async () =>
    {
        var result = await _registration
            .CreatePracticeAsync(new PracticeSignup(
                _practiceName ?? string.Empty,
                _size,
                _country ?? string.Empty,
                _fullName ?? string.Empty,
                _email ?? string.Empty,
                _role,
                _password ?? string.Empty,
                _acceptedTerms))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            ErrorMessage = result.Refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _result = result;
        _step = SignupStep.Done;

        // Dropped the moment it is no longer needed. A password sitting in a bound
        // property outlives the render that used it for no reason.
        _password = null;

        RaiseAll();
    });

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Step), nameof(ShowsDots), nameof(PracticeName), nameof(Size),
            nameof(Countries), nameof(Country), nameof(CanLeavePractice),
            nameof(FullName), nameof(Email), nameof(Role), nameof(RoleIsNonClinical),
            nameof(CanLeaveAbout), nameof(Password), nameof(ShowPassword),
            nameof(AcceptedTerms), nameof(CanCreate), nameof(Result), nameof(ClinicCode),
            nameof(Username), nameof(PlanName), nameof(TrialEndsOn), nameof(HasError),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
