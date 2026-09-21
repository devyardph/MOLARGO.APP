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

    /// <summary>
    /// Reading back the code sent to the address from step two.
    /// </summary>
    /// <remarks>
    /// After the password rather than beside the address, so somebody is sent to their
    /// inbox once and at the end. Put on the About step it would interrupt a form halfway
    /// through, and a person who leaves to fetch a code mid-form often does not come back.
    /// </remarks>
    Verify = 3,

    Done = 4,
}

/// <summary>Self-service practice signup, as the reference lays it out.</summary>
public sealed class CreatePracticeViewModel : BaseViewModel
{
    private readonly IRegistrationService _registration;

    private SignupStep _step = SignupStep.Practice;
    private string? _mobile;
    private string? _emailCode;
    private string? _codeNotice;
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
        SendCodeCommand = new MvxAsyncCommand(SendCodeAsync);
        ResendCodeCommand = new MvxAsyncCommand(ResendAsync);
    }

    public IMvxCommand NextCommand { get; }

    public IMvxCommand BackCommand { get; }

    public IMvxCommand<PracticeSize> SetSizeCommand { get; }

    public IMvxCommand<ProviderRole> SetRoleCommand { get; }

    public IMvxCommand ToggleTermsCommand { get; }

    public IMvxCommand ToggleShowPasswordCommand { get; }

    /// <summary>Emails the code and moves to the step that reads it back.</summary>
    public IMvxAsyncCommand SendCodeCommand { get; }

    public IMvxAsyncCommand ResendCodeCommand { get; }

    public IMvxAsyncCommand CreateCommand { get; }

    public override Task Initialize() => LoadAsync();

    public SignupStep Step => _step;

    public bool IsStep(SignupStep step) => _step == step;

    /// <summary>
    /// The owner's mobile, which becomes the practice's contact number.
    /// </summary>
    /// <remarks>
    /// Asked for, not required. A practice that will not give a number can still sign up —
    /// and a required field is one filled with anything that passes, which is worse than an
    /// empty one because it looks like a number somebody can ring.
    /// </remarks>
    public string? Mobile
    {
        get => _mobile;
        set => SetProperty(ref _mobile, value);
    }

    /// <summary>The six digits being typed back.</summary>
    public string? EmailCode
    {
        get => _emailCode;
        set
        {
            // Digits only, six at most. A pasted code often arrives with a space or the
            // word "code" attached, and refusing that is refusing the commonest way people
            // move six digits from a phone to a keyboard.
            var digits = new string((value ?? string.Empty)
                .Where(char.IsAsciiDigit)
                .Take(6)
                .ToArray());

            if (!SetProperty(ref _emailCode, digits)) return;

            RaisePropertyChanged(nameof(CanVerify));
        }
    }

    /// <summary>What came of sending the last code, for the screen to repeat.</summary>
    public string? CodeNotice => _codeNotice;

    public bool CanVerify => !IsBusy && _emailCode is { Length: 6 };

    /// <summary>The address the code went to, as the screen quotes it back.</summary>
    public string VerifyingAddress => (_email ?? string.Empty).Trim();

    /// <summary>The three dots. Absent once the practice exists.</summary>
    public static readonly SignupStep[] Dots =
        [SignupStep.Practice, SignupStep.About, SignupStep.Secure, SignupStep.Verify];

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
    /// The roles the signup offers.
    /// </summary>
    /// <remarks>
    /// Not every role — a practice's full list belongs on the Users screen once they are
    /// in. Clinical only: whoever signs a practice up becomes its owner, and an owner who
    /// cannot author a record leaves a clinic where nobody can write a note until somebody
    /// else is added.
    ///
    /// There was a third, labelled "Practice manager". It was
    /// <see cref="ProviderRole.Administration"/> under a name used nowhere else — Admin
    /// calls the same role "Reception / admin" and Reports calls it "Administration" — so
    /// one role wore three names depending on which screen somebody was looking at.
    /// Non-clinical staff are added on the Users screen, under the name the rest of the
    /// app uses.
    /// </remarks>
    public static readonly ProviderRole[] Roles =
        [ProviderRole.Dentist, ProviderRole.Specialist];

    public static string RoleLabel(ProviderRole role) => role switch
    {
        ProviderRole.Specialist => "Specialist",

        // Dentist, and anything else that ever reaches here. The array above is what the
        // screen offers; a default of "Dentist" is the safe read of an unexpected value,
        // where inventing a label for it is how a role ends up with a name of its own.
        _ => "Dentist",
    };

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
            SignupStep.Verify => SignupStep.Secure,
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

    /// <summary>
    /// Sends the code and moves to the step that reads it back.
    /// </summary>
    /// <remarks>
    /// Takes the place of the old "Create practice" press. Nothing is created here — the
    /// point of the code is that an address nobody reads leaves no tenant behind, which only
    /// holds if the check comes first.
    /// </remarks>
    private Task SendCodeAsync() => RunGuardedAsync(async () =>
    {
        if (!_acceptedTerms)
        {
            ErrorMessage = "Accept the terms to create the practice.";
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        var refusal = await _registration
            .SendSignupCodeAsync(_email ?? string.Empty)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _emailCode = null;
        _codeNotice = $"Code sent to {VerifyingAddress}.";
        _step = SignupStep.Verify;

        ErrorMessage = null;
        RaiseAll();
    });

    /// <summary>Sends another code without leaving the step.</summary>
    private Task ResendAsync() => RunGuardedAsync(async () =>
    {
        var refusal = await _registration
            .SendSignupCodeAsync(_email ?? string.Empty)
            .ConfigureAwait(false);

        if (refusal is { Length: > 0 })
        {
            ErrorMessage = refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _emailCode = null;
        _codeNotice = $"A new code is on its way to {VerifyingAddress}. The previous one no "
            + "longer works.";

        ErrorMessage = null;
        RaiseAll();
    });

    private Task CreateAsync() => RunGuardedAsync(async () =>
    {
        var result = await _registration
            .CreatePracticeAsync(new PracticeSignup(
                _practiceName ?? string.Empty,
                _size,
                _country ?? string.Empty,
                _fullName ?? string.Empty,
                _email ?? string.Empty,
                _mobile,
                _role,
                _password ?? string.Empty,
                _acceptedTerms),
                _emailCode ?? string.Empty)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            ErrorMessage = result.Refusal;
            await RaisePropertyChanged(nameof(HasError)).ConfigureAwait(false);
            return;
        }

        _result = result;
        _step = SignupStep.Done;
        _emailCode = null;
        _codeNotice = null;

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
            nameof(Mobile), nameof(EmailCode), nameof(CodeNotice), nameof(CanVerify),
            nameof(VerifyingAddress),
            nameof(Countries), nameof(Country), nameof(CanLeavePractice),
            nameof(FullName), nameof(Email), nameof(Role),
            nameof(CanLeaveAbout), nameof(Password), nameof(ShowPassword),
            nameof(AcceptedTerms), nameof(CanCreate), nameof(Result), nameof(ClinicCode),
            nameof(Username), nameof(PlanName), nameof(TrialEndsOn), nameof(HasError),
        })
        {
            RaisePropertyChanged(name);
        }
    }
}
