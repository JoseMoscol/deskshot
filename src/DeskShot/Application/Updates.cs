namespace DeskShot.Application;

public sealed record UpdateAsset(string Name, Uri Url, long Size, string? Digest);
public sealed record AvailableUpdate(Version Version, string Tag, string Notes, Uri Page, UpdateAsset Installer, UpdateAsset Checksum);
public sealed record VerifiedUpdate(AvailableUpdate Release, string InstallerPath, string Sha256);
public sealed record UpdateDownloadProgress(long Received, long Total)
{
    public double Percent => Total > 0 ? Received * 100d / Total : 0;
}
public interface IUpdateService
{
    Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken token);
    Task<VerifiedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress, CancellationToken token);
    Task ValidateAsync(VerifiedUpdate update, CancellationToken token);
}
