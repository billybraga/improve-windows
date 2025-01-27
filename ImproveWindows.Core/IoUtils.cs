using System.Text;
using CliWrap;

namespace ImproveWindows.Core;

public static class IoUtils
{
    private static readonly IReadOnlyCollection<string> ExecutableExtensions = OperatingSystem.IsWindows()
        ? ["cmd", "exe"]
        : [];

    public static string GetEffectiveProcessFilePath(string fileName)
    {
        foreach (var effectiveFilePath in EnumerateEffectiveProcessFilePaths(fileName))
        {
            if (File.Exists(effectiveFilePath))
            {
                return effectiveFilePath;
            }
        }

        throw new FileNotFoundException("Could not find executable file", fileName);
    }

    private static IEnumerable<string> EnumerateEffectiveProcessFilePaths(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            yield return fileName;
        }

        foreach (var executableExtension in ExecutableExtensions)
        {
            yield return $"{fileName}.{executableExtension}";
        }

        var pathEnvironmentVariable = Environment.GetEnvironmentVariable("PATH")
            ?? "";

        var paths = pathEnvironmentVariable.Split(Path.PathSeparator).ToArray();

        foreach (var executableExtension in ExecutableExtensions)
        {
            foreach (var path in paths)
            {
                yield return Path.Combine(path, $"{fileName}.{executableExtension}");
            }
        }

        foreach (var path in paths)
        {
            yield return Path.Combine(path, fileName);
        }
    }

    public static Command CreateCommand(string fileName, IReadOnlyCollection<string> arguments)
    {
        return CliWrap.Cli.Wrap(GetEffectiveProcessFilePath(fileName))
            .WithValidation(CommandResultValidation.None)
            .WithArguments(arguments, true);
    }

    public static async Task ExecuteAndValidateAsync(this Command command, CancellationToken cancellationToken = default)
    {
        StringBuilder? outputBuilder = null;
        if (command.StandardOutputPipe == PipeTarget.Null)
        {
            outputBuilder = new StringBuilder();
            command = command.WithStandardOutputPipe(PipeTarget.ToStringBuilder(outputBuilder));
        }
        var errorBuilder = new StringBuilder();
        var commandResult = await command
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(errorBuilder))
            .ExecuteAsync(cancellationToken);

        if (!commandResult.IsSuccess)
        {
            throw new InvalidOperationException($"Error running {command}:\n{outputBuilder}\n{errorBuilder}");
        }
    }

    public static async Task<string> ExecuteValidateAndGetOutputAsync(this Command command, CancellationToken cancellationToken = default)
    {
        var outputBuilder = new StringBuilder();
        await command
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(outputBuilder))
            .ExecuteAndValidateAsync(cancellationToken);

        return outputBuilder.ToString();
    }
}