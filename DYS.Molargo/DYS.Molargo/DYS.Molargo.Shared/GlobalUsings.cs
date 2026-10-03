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

// The services project's own namespaces.
//
// Imported globally rather than per file because five of these were, until the rename,
// the SAME namespace as types that stayed in DYS.Molargo.Shared — DYS.Molargo.Shared.Api
// held MolargoRpc here and RpcChannel there, DYS.Molargo.Shared.Services held the audit
// log here and the navigator there. Splitting them is the point: a type's namespace now
// says which project it is in, and whether it can run on the server.
global using DYS.Molargo.Services.Auditing;
global using DYS.Molargo.Services.Formatting;
global using DYS.Molargo.Services.Messaging;
global using DYS.Molargo.Services.Session;
global using DYS.Molargo.Services.Api;
global using DYS.Molargo.Services.Documents;
global using DYS.Molargo.Services.Settings;
