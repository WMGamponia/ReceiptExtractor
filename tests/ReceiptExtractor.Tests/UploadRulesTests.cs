using System.Text;
using Microsoft.AspNetCore.Http;
using ReceiptExtractor.Api.Endpoints;

namespace ReceiptExtractor.Tests;

public sealed class UploadRulesTests
{
    private static IFormFile Upload(byte[] bytes, string name = "receipt.jpg") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]       // JPEG
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D })] // PNG
    public async Task Jpeg_and_png_look_like_images(byte[] bytes) =>
        Assert.True(await UploadRules.LooksLikeImageAsync(Upload(bytes), CancellationToken.None));

    [Fact]
    public async Task Text_renamed_to_jpg_does_not() =>
        Assert.False(await UploadRules.LooksLikeImageAsync(Upload(Encoding.UTF8.GetBytes("hello")), CancellationToken.None));

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".JPG")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    public void Photo_extensions_are_allowed_in_any_case(string extension) =>
        Assert.True(UploadRules.AllowedFiles.ContainsKey(extension));

    [Fact]
    public void Pdf_is_not_allowed_yet() => Assert.False(UploadRules.AllowedFiles.ContainsKey(".pdf"));
}
