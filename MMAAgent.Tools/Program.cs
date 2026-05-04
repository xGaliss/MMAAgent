using MMAAgent.Tools.Commands;

var argsList = args.ToList();

if (argsList.Count == 0 || IsHelp(argsList[0]))
{
    PrintHelp();
    return 0;
}

var commandName = argsList[0].Trim().ToLowerInvariant();
var commandArgs = argsList.Skip(1).ToArray();

return commandName switch
{
    "validate-country-data" => await CountryCultureValidationCommand.RunAsync(commandArgs),
    _ => UnknownCommand(commandName)
};

static bool IsHelp(string value)
{
    return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
}

static int UnknownCommand(string commandName)
{
    Console.Error.WriteLine($"Unknown command: {commandName}");
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("MMAAgent.Tools");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  validate-country-data [--db <path>]");
    Console.WriteLine("    Validates the web template database country/culture/name setup.");
}
