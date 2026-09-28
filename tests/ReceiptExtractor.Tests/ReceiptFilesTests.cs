using ReceiptExtractor.Api.Endpoints;

namespace ReceiptExtractor.Tests;

public sealed class ReceiptFilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("receipt-files-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Touch(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Deletes_the_image_backup_and_leftover_temp_file()
    {
        var id = Guid.NewGuid();
        var image = Touch($"{id}.png");
        Touch($"{id}.original.jpg");
        Touch($"{id}.png.tmp");

        var failed = ReceiptFiles.DeleteAll(image, id);

        Assert.Empty(failed);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Leaves_other_receipts_alone()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var image = Touch($"{id}.jpg");
        var keep = Touch($"{other}.jpg");
        var keepBackup = Touch($"{other}.original.jpg");

        ReceiptFiles.DeleteAll(image, id);

        Assert.Equal(new[] { keep, keepBackup }.Order(), Directory.GetFiles(_folder).Order());
    }

    [Fact]
    public void Missing_files_are_not_an_error()
    {
        var id = Guid.NewGuid();

        var failed = ReceiptFiles.DeleteAll(Path.Combine(_folder, $"{id}.jpg"), id);

        Assert.Empty(failed);
    }
}
