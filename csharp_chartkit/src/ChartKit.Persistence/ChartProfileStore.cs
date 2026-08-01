using System.Text;

namespace ChartKit.CSharp.Persistence;

public sealed class ChartProfileStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly ChartProfileCodec _codec;

    public ChartProfileStore(ChartProfileCodec? codec = null)
    {
        _codec = codec ?? new ChartProfileCodec();
    }

    public async Task SaveAsync(
        string path,
        ChartProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string fullPath = RequirePath(path);
        string directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                $"Profile path has no directory: {fullPath}");
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = _codec.Serialize(profile);
            await File.WriteAllTextAsync(
                    tempPath,
                    json,
                    Utf8WithoutBom,
                    cancellationToken)
                .ConfigureAwait(false);

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public async Task<ChartProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = RequirePath(path);
        string json = await File.ReadAllTextAsync(
                fullPath,
                Utf8WithoutBom,
                cancellationToken)
            .ConfigureAwait(false);
        return _codec.Deserialize(json);
    }

    private static string RequirePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Profile path is required.", nameof(path));
        return Path.GetFullPath(path.Trim());
    }
}
