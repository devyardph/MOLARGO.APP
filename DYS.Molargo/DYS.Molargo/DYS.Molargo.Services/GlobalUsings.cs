// The data layer moved out to DYS.Molargo.Domain.Data — see that project's file header for why.
//
// Imported globally rather than added as a using to the sixty-odd files that name a
// repository, a context, the clock or the tenant. Those files did not change and had no
// reason to: what moved is which assembly the types live in, not anything about how they
// are used.
global using DYS.Molargo.Domain.Data;

// The Domain's sub-namespaces, imported globally rather than added as a using to the
// hundreds of files that name a PagedResult, a PracticeHours or a PracticeAccess.
//
// Those types were loose at the root of DYS.Molargo.Domain and are now in Rules/ and
// Values/, so folder and namespace agree the way Entities/ and Enums/ already did. Nothing
// about how they are used changed — only which namespace they sit in — so the import goes
// in one place per project instead of a mechanical edit across every file that reads one.
global using DYS.Molargo.Domain.Rules;
global using DYS.Molargo.Domain.Values;

// This project's own cross-cutting namespaces.
//
// Session, auditing, messaging and formatting are used by nearly every feature service in
// here, and they all sat in one namespace until the folders were introduced. Imported
// globally rather than adding four usings to forty-five files that each need two or three
// of them — what moved is which namespace they are in, not anything about how they are used.
global using DYS.Molargo.Services.Auditing;
global using DYS.Molargo.Services.Formatting;
global using DYS.Molargo.Services.Messaging;
global using DYS.Molargo.Services.Session;

// Searching, for the same reason: how a typed term is normalised is one rule, and a file
// that imports it locally is a file that can quietly stop following it.
global using DYS.Molargo.Services.Search;

// Payments, beside Messaging for the same reason: the gateway resolver and the provider
// contract are named by the billing service, the platform screens and the API's webhook,
// and importing them per file is three usings that say nothing.
global using DYS.Molargo.Services.Payments;
