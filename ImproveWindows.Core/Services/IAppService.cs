namespace ImproveWindows.Core.Services;

public interface IAppService
{
    Task RunAsync(CancellationToken cancellationToken);
}