using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// The upload is the one place a tenant (not the operator) hands this
/// process an image to decode, so it only decodes the formats the asset type
/// allows. The Content-Type header is the client's claim; these tests send
/// the wrong bytes under the right header, which is what an attacker does.
/// </summary>
public sealed class BrandingAssetStorageServiceTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "branding-upload-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeBrandingRepository _repository = new();
    private readonly BrandingAssetStorageService _sut;

    public BrandingAssetStorageServiceTests()
    {
        Directory.CreateDirectory(_webRoot);
        _sut = new BrandingAssetStorageService(new WebRoot(_webRoot), _repository, TimeProvider.System);
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task A_png_logo_is_stored_as_png()
    {
        var asset = await _sut.StoreAsync(Upload(Encode(64, 32, new PngEncoder()), "image/png"), BrandingAssetType.Logo, 7, TenantId);

        Assert.EndsWith(".png", asset.StoragePath);
        Assert.Equal((64, 32), (asset.WidthPx, asset.HeightPx));
        Assert.True(File.Exists(StoredFile(asset)));
    }

    [Fact]
    public async Task A_jpeg_logo_is_stored_as_jpeg()
    {
        var asset = await _sut.StoreAsync(Upload(Encode(64, 64, new JpegEncoder()), "image/jpeg"), BrandingAssetType.Logo, 7, TenantId);

        Assert.EndsWith(".jpg", asset.StoragePath);
    }

    [Theory]
    [MemberData(nameof(OtherFormats))]
    public async Task Another_format_labelled_png_is_rejected_without_being_decoded(string format, IImageEncoder encoder)
    {
        _ = format; // the row's display label
        var upload = Upload(Encode(64, 64, encoder), "image/png");

        var refused = await Assert.ThrowsAsync<BrandingAssetValidationException>(
            () => _sut.StoreAsync(upload, BrandingAssetType.Logo, 7, TenantId));

        Assert.Equal("The uploaded file is not a valid image.", refused.Message);
        Assert.Empty(_repository.Assets);
        Assert.Empty(Directory.EnumerateFiles(_webRoot, "*", SearchOption.AllDirectories));
    }

    public static TheoryData<string, IImageEncoder> OtherFormats() => new()
    {
        // TIFF is the format behind the 2026-10 ImageSharp advisories; a
        // 1-bit CCITT TIFF is the one that crashed the process on re-encode.
        { "tiff (CCITT group 4)", new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit1, Compression = TiffCompression.CcittGroup4Fax } },
        { "tiff", new TiffEncoder() },
        { "bmp", new BmpEncoder() },
        { "gif", new GifEncoder() }
    };

    [Fact]
    public async Task A_jpeg_favicon_is_rejected_even_when_labelled_png()
    {
        var upload = Upload(Encode(32, 32, new JpegEncoder()), "image/png");

        await Assert.ThrowsAsync<BrandingAssetValidationException>(
            () => _sut.StoreAsync(upload, BrandingAssetType.Favicon, 7, TenantId));
        Assert.Empty(_repository.Assets);
    }

    [Fact]
    public async Task An_oversized_canvas_is_rejected()
    {
        var upload = Upload(Encode(5000, 16, new PngEncoder()), "image/png");

        var refused = await Assert.ThrowsAsync<BrandingAssetValidationException>(
            () => _sut.StoreAsync(upload, BrandingAssetType.Hero, 7, TenantId));

        Assert.StartsWith("Image dimensions must be between", refused.Message);
    }

    [Fact]
    public async Task A_corrupt_png_is_a_validation_error_not_a_crash()
    {
        var bytes = Encode(64, 64, new PngEncoder());
        Array.Fill(bytes, (byte)0xAB, 33, bytes.Length - 33); // keep the signature and IHDR, wreck the rest

        await Assert.ThrowsAsync<BrandingAssetValidationException>(
            () => _sut.StoreAsync(Upload(bytes, "image/png"), BrandingAssetType.Logo, 7, TenantId));
        Assert.Empty(_repository.Assets);
    }

    [Fact]
    public async Task Exif_metadata_is_not_carried_into_the_stored_file()
    {
        using var source = new Image<Rgba32>(64, 64);
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        using var buffer = new MemoryStream();
        await source.SaveAsync(buffer, new JpegEncoder());

        var asset = await _sut.StoreAsync(Upload(buffer.ToArray(), "image/jpeg"), BrandingAssetType.Logo, 7, TenantId);

        using var stored = await Image.LoadAsync(StoredFile(asset));
        Assert.Null(stored.Metadata.ExifProfile);
    }

    private string StoredFile(BrandingAsset asset) => Path.Combine(_webRoot, asset.StoragePath.TrimStart('/'));

    private static byte[] Encode(int width, int height, IImageEncoder encoder)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var buffer = new MemoryStream();
        image.Save(buffer, encoder);
        return buffer.ToArray();
    }

    private static IFormFile Upload(byte[] bytes, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "upload")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private sealed class WebRoot(string path) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = path;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = path;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
