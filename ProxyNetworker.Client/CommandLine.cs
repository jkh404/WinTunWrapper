internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options;

    private CommandLine(IReadOnlyList<string> arguments, Dictionary<string, string?> options)
    {
        Arguments = arguments;
        _options = options;
    }

    public IReadOnlyList<string> Arguments { get; }

    public static CommandLine Parse(string[] args)
    {
        var arguments = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                arguments.Add(arg);
                continue;
            }

            var option = arg[2..];
            var splitIndex = option.IndexOf('=', StringComparison.Ordinal);
            if (splitIndex >= 0)
            {
                options[option[..splitIndex]] = option[(splitIndex + 1)..];
                continue;
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[option] = args[++index];
                continue;
            }

            options[option] = "true";
        }

        return new CommandLine(arguments, options);
    }

    public bool HasOption(string name)
    {
        return _options.ContainsKey(name);
    }

    public bool IsFlagEnabled(string name)
    {
        return _options.TryGetValue(name, out var value) &&
            (value is null ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    public string? Option(string name, string? environmentVariable = null)
    {
        if (_options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return environmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(environmentVariable);
    }

    public string? Argument(int index)
    {
        return index >= 0 && index < Arguments.Count ? Arguments[index] : null;
    }

    public bool CommandIs(string command)
    {
        return Arguments.Count > 0 && string.Equals(Arguments[0], command, StringComparison.OrdinalIgnoreCase);
    }

    public bool SubCommandIs(string command)
    {
        return Arguments.Count > 1 && string.Equals(Arguments[1], command, StringComparison.OrdinalIgnoreCase);
    }

    public int IntOption(string name, int defaultValue)
    {
        var value = Option(name);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public int IntOption(string name, string? fallbackValue, int defaultValue)
    {
        var value = Option(name) ?? fallbackValue;
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
