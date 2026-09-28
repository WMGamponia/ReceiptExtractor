namespace ReceiptExtractor.Api.Endpoints;

/// <summary>
/// The files one receipt owns on disk. All of them start with the receipt's id:
/// the image (<c>{id}.jpg</c>), the backup kept before the first rotation (<c>{id}.original.jpg</c>)
/// and any <c>.tmp</c> left behind by an interrupted image replace.
/// </summary>
public static class ReceiptFiles
{
    public static IReadOnlyList<string> Find(string folder, Guid id) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, $"{id}.*") : [];

    /// <summary>Deletes every file the receipt owns. Returns the ones that couldn't be deleted (e.g. locked by another program).</summary>
    public static IReadOnlyList<string> DeleteAll(string imagePath, Guid id)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
        var files = Find(folder, id).Append(imagePath).Distinct(StringComparer.OrdinalIgnoreCase);

        var failed = new List<string>();
        foreach (var file in files)
        {
            try
            {
                File.Delete(file); // no error if it's already gone
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(file);
            }
        }
        return failed;
    }
}
