namespace ImproveWindows.Core.Services.CommandRunner;

public sealed class ManagedCommand
{
    public required Guid Id { get; init; }
    public required string CommandLine { get; init; }
}
