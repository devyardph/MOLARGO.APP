// The data layer moved out to DYS.Molargo.Data — see that project's file header for why.
//
// Imported globally rather than added as a using to the sixty-odd files that name a
// repository, a context, the clock or the tenant. Those files did not change and had no
// reason to: what moved is which assembly the types live in, not anything about how they
// are used.
global using DYS.Molargo.Data;
