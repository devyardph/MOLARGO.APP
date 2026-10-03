// The Domain's sub-namespaces, imported globally rather than added as a using to the
// hundreds of files that name a PagedResult, a PracticeHours or a PracticeAccess.
//
// Those types were loose at the root of DYS.Molargo.Domain and are now in Rules/ and
// Values/, so folder and namespace agree the way Entities/ and Enums/ already did. Nothing
// about how they are used changed — only which namespace they sit in — so the import goes
// in one place per project instead of a mechanical edit across every file that reads one.
global using DYS.Molargo.Domain.Rules;
global using DYS.Molargo.Domain.Values;
