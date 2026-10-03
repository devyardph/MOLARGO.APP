using DYS.Molargo.SelfTest;

// Thin by design: the whole test is SelfTestCommand, and this exists only so it is a
// program. Arguments are passed straight through — --server, --clinic, --user, --password.
return await SelfTestCommand.RunAsync(args);
