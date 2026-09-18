namespace DYS.Molargo.Domain;

/// <summary>One time zone, as a site's picker offers it.</summary>
/// <param name="Id">
/// The IANA id, which is what gets stored — "Australia/Sydney", never "AUS Eastern
/// Standard Time".
/// </param>
/// <param name="Label">"(UTC+10:00) Australia/Sydney", for the list.</param>
/// <param name="Region">"Australia", for grouping the list.</param>
public sealed record TimeZoneOption(string Id, string Label, string Region);

/// <summary>
/// Every time zone the machine knows, for a practice's sites.
/// </summary>
/// <remarks>
/// <para>
/// The list this replaced held seven Australian zones, on the reasoning that a practice's
/// sites are all in one country — true, and it silently decided which country that was. A
/// clinic in Auckland or Manila could not name its own zone, and the diary would have shown
/// every appointment in Sydney time.
/// </para>
/// <para>
/// Stored as IANA on every platform, including Windows. The device heads run on iOS,
/// Android and macOS where IANA is the only form, and the column already holds
/// "Australia/Sydney"; a Windows id written by the web head would be a value the tablet
/// could not resolve, and the failure would be a diary an hour out rather than an error.
/// .NET resolves IANA ids on Windows too, so one form works everywhere.
/// </para>
/// <para>
/// Offsets are the zone's <em>current</em> rule, not a fixed number: the label is built
/// from <see cref="TimeZoneInfo.GetUtcOffset(DateTimeOffset)"/> at load, so a zone on
/// summer time reads as the hour it is actually keeping today. It is a sorting and reading
/// aid, not a stored fact — nothing depends on it, which is what makes it safe for it to
/// change twice a year.
/// </para>
/// </remarks>
public static class PracticeTimeZone
{
    /// <summary>
    /// Built once. Resolving four hundred ids is measured in milliseconds, but it is the
    /// same answer every time and this list is read on every visit to the Sites screen.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<TimeZoneOption>> Catalogue = new(Build);

    public static IReadOnlyList<TimeZoneOption> Options => Catalogue.Value;

    /// <summary>The regions the list groups under, in the order they should appear.</summary>
    /// <remarks>
    /// <para>
    /// Derived from the zones themselves rather than a fixed list of continents, so a
    /// region only appears when the machine actually has zones in it — and a platform that
    /// knows a region this code has never heard of still shows it.
    /// </para>
    /// <para>
    /// Lazy, not a field initialiser. As one it read <c>Catalogue.Value</c> while the class
    /// was still initialising, and static field initialisers run in declaration order — so
    /// <c>Build</c> ran before <c>IanaIds</c> below it had been assigned and walked a null
    /// array. The failure was a TypeInitializationException on the Sites screen and nowhere
    /// near the array that caused it.
    /// </para>
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<string>> RegionList = new(() =>
        Catalogue.Value
            .Select(zone => zone.Region)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(region => region, StringComparer.Ordinal)
            .ToList());

    public static IReadOnlyList<string> Regions => RegionList.Value;

    public static IEnumerable<TimeZoneOption> In(string region) =>
        Catalogue.Value.Where(zone =>
            string.Equals(zone.Region, region, StringComparison.Ordinal));

    /// <summary>True where the id names a zone this machine can resolve.</summary>
    public static bool IsKnown(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            // The id names a zone whose data is corrupt. Still not usable.
            return false;
        }
    }

    /// <summary>The label for an id, or the id itself where it is not in the list.</summary>
    /// <remarks>
    /// Falls back rather than blanking. A site carrying a zone this machine does not know
    /// — a database copied from another platform, a zone since renamed — should read as
    /// the id it holds, not as nothing.
    /// </remarks>
    public static string LabelOf(string? id) =>
        id is { Length: > 0 }
            ? Catalogue.Value.FirstOrDefault(zone =>
                string.Equals(zone.Id, id, StringComparison.Ordinal))?.Label ?? id
            : string.Empty;

    /// <summary>
    /// The IANA zone ids, as the candidate set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Listed rather than enumerated, because there is no way to enumerate them on every
    /// platform this runs on. <c>TimeZoneInfo.GetSystemTimeZones</c> returns what the host
    /// keeps: on iOS, Android and macOS that is the IANA database, and on Windows it is the
    /// ~140 Windows zones, each mapping to one representative IANA id. Built from the
    /// system list, the web head offered 139 zones with no Asia/Manila among them — Windows
    /// files the Philippines under Singapore Standard Time — so a Manila clinic could not
    /// name its own zone on the web and could on a tablet, from the same database.
    /// </para>
    /// <para>
    /// Candidates, not answers: every id is resolved against the machine in
    /// <see cref="Build"/> and dropped where it will not resolve. The list can therefore
    /// only ever be a superset — a host missing a zone offers one fewer, never one its own
    /// diary would then fail to read.
    /// </para>
    /// </remarks>
    private static readonly string[] IanaIds =
    [
        // Africa
        "Africa/Abidjan", "Africa/Accra", "Africa/Addis_Ababa", "Africa/Algiers",
        "Africa/Asmera", "Africa/Bamako", "Africa/Bangui", "Africa/Banjul", "Africa/Bissau",
        "Africa/Blantyre", "Africa/Brazzaville", "Africa/Bujumbura", "Africa/Cairo",
        "Africa/Casablanca", "Africa/Ceuta", "Africa/Conakry", "Africa/Dakar",
        "Africa/Dar_es_Salaam", "Africa/Djibouti", "Africa/Douala", "Africa/El_Aaiun",
        "Africa/Freetown", "Africa/Gaborone", "Africa/Harare", "Africa/Johannesburg",
        "Africa/Juba", "Africa/Kampala", "Africa/Khartoum", "Africa/Kigali", "Africa/Kinshasa",
        "Africa/Lagos", "Africa/Libreville", "Africa/Lome", "Africa/Luanda",
        "Africa/Lubumbashi", "Africa/Lusaka", "Africa/Malabo", "Africa/Maputo",
        "Africa/Maseru", "Africa/Mbabane", "Africa/Mogadishu", "Africa/Monrovia",
        "Africa/Nairobi", "Africa/Ndjamena", "Africa/Niamey", "Africa/Nouakchott",
        "Africa/Ouagadougou", "Africa/Porto-Novo", "Africa/Sao_Tome", "Africa/Tripoli",
        "Africa/Tunis", "Africa/Windhoek",

        // America
        "America/Adak", "America/Anchorage", "America/Anguilla", "America/Antigua",
        "America/Araguaina", "America/Argentina/La_Rioja", "America/Argentina/Rio_Gallegos",
        "America/Argentina/Salta", "America/Argentina/San_Juan", "America/Argentina/San_Luis",
        "America/Argentina/Tucuman", "America/Argentina/Ushuaia", "America/Aruba",
        "America/Asuncion", "America/Bahia", "America/Bahia_Banderas", "America/Barbados",
        "America/Belem", "America/Belize", "America/Blanc-Sablon", "America/Boa_Vista",
        "America/Bogota", "America/Boise", "America/Buenos_Aires", "America/Cambridge_Bay",
        "America/Campo_Grande", "America/Cancun", "America/Caracas", "America/Catamarca",
        "America/Cayenne", "America/Cayman", "America/Chicago", "America/Chihuahua",
        "America/Ciudad_Juarez", "America/Coral_Harbour", "America/Cordoba",
        "America/Costa_Rica", "America/Coyhaique", "America/Creston", "America/Cuiaba",
        "America/Curacao", "America/Danmarkshavn", "America/Dawson", "America/Dawson_Creek",
        "America/Denver", "America/Detroit", "America/Dominica", "America/Edmonton",
        "America/Eirunepe", "America/El_Salvador", "America/Fort_Nelson", "America/Fortaleza",
        "America/Glace_Bay", "America/Godthab", "America/Goose_Bay", "America/Grand_Turk",
        "America/Grenada", "America/Guadeloupe", "America/Guatemala", "America/Guayaquil",
        "America/Guyana", "America/Halifax", "America/Havana", "America/Hermosillo",
        "America/Indiana/Knox", "America/Indiana/Marengo", "America/Indiana/Petersburg",
        "America/Indiana/Tell_City", "America/Indiana/Vevay", "America/Indiana/Vincennes",
        "America/Indiana/Winamac", "America/Indianapolis", "America/Inuvik", "America/Iqaluit",
        "America/Jamaica", "America/Jujuy", "America/Juneau", "America/Kentucky/Monticello",
        "America/Kralendijk", "America/La_Paz", "America/Lima", "America/Los_Angeles",
        "America/Louisville", "America/Lower_Princes", "America/Maceio", "America/Managua",
        "America/Manaus", "America/Marigot", "America/Martinique", "America/Matamoros",
        "America/Mazatlan", "America/Mendoza", "America/Menominee", "America/Merida",
        "America/Metlakatla", "America/Mexico_City", "America/Miquelon", "America/Moncton",
        "America/Monterrey", "America/Montevideo", "America/Montserrat", "America/Nassau",
        "America/New_York", "America/Nome", "America/Noronha", "America/North_Dakota/Beulah",
        "America/North_Dakota/Center", "America/North_Dakota/New_Salem", "America/Ojinaga",
        "America/Panama", "America/Paramaribo", "America/Phoenix", "America/Port-au-Prince",
        "America/Port_of_Spain", "America/Porto_Velho", "America/Puerto_Rico",
        "America/Punta_Arenas", "America/Rankin_Inlet", "America/Recife", "America/Regina",
        "America/Resolute", "America/Rio_Branco", "America/Santarem", "America/Santiago",
        "America/Santo_Domingo", "America/Sao_Paulo", "America/Scoresbysund", "America/Sitka",
        "America/St_Barthelemy", "America/St_Johns", "America/St_Kitts", "America/St_Lucia",
        "America/St_Thomas", "America/St_Vincent", "America/Swift_Current",
        "America/Tegucigalpa", "America/Thule", "America/Tijuana", "America/Toronto",
        "America/Tortola", "America/Vancouver", "America/Whitehorse", "America/Winnipeg",
        "America/Yakutat",

        // Antarctica
        "Antarctica/Casey", "Antarctica/Davis", "Antarctica/DumontDUrville",
        "Antarctica/Macquarie", "Antarctica/Mawson", "Antarctica/McMurdo", "Antarctica/Palmer",
        "Antarctica/Rothera", "Antarctica/Syowa", "Antarctica/Troll", "Antarctica/Vostok",

        // Arctic
        "Arctic/Longyearbyen",

        // Asia
        "Asia/Aden", "Asia/Almaty", "Asia/Amman", "Asia/Anadyr", "Asia/Aqtau", "Asia/Aqtobe",
        "Asia/Ashgabat", "Asia/Atyrau", "Asia/Baghdad", "Asia/Bahrain", "Asia/Baku",
        "Asia/Bangkok", "Asia/Barnaul", "Asia/Beirut", "Asia/Bishkek", "Asia/Brunei",
        "Asia/Calcutta", "Asia/Chita", "Asia/Colombo", "Asia/Damascus", "Asia/Dhaka",
        "Asia/Dili", "Asia/Dubai", "Asia/Dushanbe", "Asia/Famagusta", "Asia/Gaza",
        "Asia/Hebron", "Asia/Hong_Kong", "Asia/Hovd", "Asia/Irkutsk", "Asia/Jakarta",
        "Asia/Jayapura", "Asia/Jerusalem", "Asia/Kabul", "Asia/Kamchatka", "Asia/Karachi",
        "Asia/Katmandu", "Asia/Khandyga", "Asia/Krasnoyarsk", "Asia/Kuala_Lumpur",
        "Asia/Kuching", "Asia/Kuwait", "Asia/Macau", "Asia/Magadan", "Asia/Makassar",
        "Asia/Manila", "Asia/Muscat", "Asia/Nicosia", "Asia/Novokuznetsk", "Asia/Novosibirsk",
        "Asia/Omsk", "Asia/Oral", "Asia/Phnom_Penh", "Asia/Pontianak", "Asia/Pyongyang",
        "Asia/Qatar", "Asia/Qostanay", "Asia/Qyzylorda", "Asia/Rangoon", "Asia/Riyadh",
        "Asia/Saigon", "Asia/Sakhalin", "Asia/Samarkand", "Asia/Seoul", "Asia/Shanghai",
        "Asia/Singapore", "Asia/Srednekolymsk", "Asia/Taipei", "Asia/Tashkent", "Asia/Tbilisi",
        "Asia/Tehran", "Asia/Thimphu", "Asia/Tokyo", "Asia/Tomsk", "Asia/Ulaanbaatar",
        "Asia/Urumqi", "Asia/Ust-Nera", "Asia/Vientiane", "Asia/Vladivostok", "Asia/Yakutsk",
        "Asia/Yekaterinburg", "Asia/Yerevan",

        // Atlantic
        "Atlantic/Azores", "Atlantic/Bermuda", "Atlantic/Canary", "Atlantic/Cape_Verde",
        "Atlantic/Faeroe", "Atlantic/Madeira", "Atlantic/Reykjavik", "Atlantic/South_Georgia",
        "Atlantic/St_Helena", "Atlantic/Stanley",

        // Australia
        "Australia/Adelaide", "Australia/Brisbane", "Australia/Broken_Hill",
        "Australia/Darwin", "Australia/Eucla", "Australia/Hobart", "Australia/Lindeman",
        "Australia/Lord_Howe", "Australia/Melbourne", "Australia/Perth", "Australia/Sydney",

        // Europe
        "Europe/Amsterdam", "Europe/Andorra", "Europe/Astrakhan", "Europe/Athens",
        "Europe/Belgrade", "Europe/Berlin", "Europe/Bratislava", "Europe/Brussels",
        "Europe/Bucharest", "Europe/Budapest", "Europe/Busingen", "Europe/Chisinau",
        "Europe/Copenhagen", "Europe/Dublin", "Europe/Gibraltar", "Europe/Guernsey",
        "Europe/Helsinki", "Europe/Isle_of_Man", "Europe/Istanbul", "Europe/Jersey",
        "Europe/Kaliningrad", "Europe/Kiev", "Europe/Kirov", "Europe/Lisbon",
        "Europe/Ljubljana", "Europe/London", "Europe/Luxembourg", "Europe/Madrid",
        "Europe/Malta", "Europe/Mariehamn", "Europe/Minsk", "Europe/Monaco", "Europe/Moscow",
        "Europe/Oslo", "Europe/Paris", "Europe/Podgorica", "Europe/Prague", "Europe/Riga",
        "Europe/Rome", "Europe/Samara", "Europe/San_Marino", "Europe/Sarajevo",
        "Europe/Saratov", "Europe/Simferopol", "Europe/Skopje", "Europe/Sofia",
        "Europe/Stockholm", "Europe/Tallinn", "Europe/Tirane", "Europe/Ulyanovsk",
        "Europe/Vaduz", "Europe/Vatican", "Europe/Vienna", "Europe/Vilnius",
        "Europe/Volgograd", "Europe/Warsaw", "Europe/Zagreb", "Europe/Zurich",

        // Indian
        "Indian/Antananarivo", "Indian/Chagos", "Indian/Christmas", "Indian/Cocos",
        "Indian/Comoro", "Indian/Kerguelen", "Indian/Mahe", "Indian/Maldives",
        "Indian/Mauritius", "Indian/Mayotte", "Indian/Reunion",

        // Pacific
        "Pacific/Apia", "Pacific/Auckland", "Pacific/Bougainville", "Pacific/Chatham",
        "Pacific/Easter", "Pacific/Efate", "Pacific/Enderbury", "Pacific/Fakaofo",
        "Pacific/Fiji", "Pacific/Funafuti", "Pacific/Galapagos", "Pacific/Gambier",
        "Pacific/Guadalcanal", "Pacific/Guam", "Pacific/Honolulu", "Pacific/Kiritimati",
        "Pacific/Kosrae", "Pacific/Kwajalein", "Pacific/Majuro", "Pacific/Marquesas",
        "Pacific/Midway", "Pacific/Nauru", "Pacific/Niue", "Pacific/Norfolk", "Pacific/Noumea",
        "Pacific/Pago_Pago", "Pacific/Palau", "Pacific/Pitcairn", "Pacific/Ponape",
        "Pacific/Port_Moresby", "Pacific/Rarotonga", "Pacific/Saipan", "Pacific/Tahiti",
        "Pacific/Tarawa", "Pacific/Tongatapu", "Pacific/Truk", "Pacific/Wake",
        "Pacific/Wallis",
    ];

    private static IReadOnlyList<TimeZoneOption> Build()
    {
        var now = DateTimeOffset.UtcNow;
        var seen = new Dictionary<string, TimeZoneOption>(StringComparer.Ordinal);

        foreach (var id in IanaIds)
        {
            TimeZoneInfo zone;

            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Not on this host. Dropped rather than offered: the service validates the
                // same way on save, so offering it would be a choice that is then refused.
                continue;
            }

            if (seen.ContainsKey(id)) continue;

            var offset = zone.GetUtcOffset(now);

            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var label = $"(UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}) {id}";

            seen[id] = new TimeZoneOption(id, label, RegionOf(id));
        }

        return seen.Values
            // By offset first, so the list reads west to east the way a person scanning for
            // their own city expects, rather than alphabetically by continent.
            .OrderBy(option => OffsetOf(option.Id, now))
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>"Australia/Sydney" groups under "Australia"; "UTC" under itself.</summary>
    private static string RegionOf(string ianaId)
    {
        var slash = ianaId.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 ? ianaId[..slash] : ianaId;
    }

    private static TimeSpan OffsetOf(string ianaId, DateTimeOffset when)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId).GetUtcOffset(when);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Sorted to the front rather than dropped. It came out of the system list, so
            // it exists; failing to re-resolve it is a reason to show it oddly, not to hide
            // a zone somebody may be looking for.
            return TimeSpan.MinValue;
        }
    }
}
