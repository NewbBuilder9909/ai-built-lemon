# ImageSharp advisories, October 2026

The weekly CI run on 12 October (run 38272228992) failed its NuGet audit:
five advisories published against `SixLabors.ImageSharp` 3.1.12, which this
site gets transitively through Umbraco (`Umbraco.Cms`,
`Umbraco.Cms.Imaging.ImageSharp`). Nothing in the repository changed; the
advisories are new.

| Advisory | Severity | What a crafted file does | Reached by |
|---|---|---|---|
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q) | High (7.5, availability) | Out-of-bounds write that terminates the process | Re-encoding a 1-bit TIFF with CCITT T4 compression |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w) | High (7.5, availability) | Same, CCITT T6 | Re-encoding a 1-bit TIFF with CCITT T6 compression |
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4) | High (7.5, availability) | Out-of-range access that terminates the process | Decoding a float TIFF, then `HistogramEqualization()` |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g) | Moderate (5.3) | Decoder thread loops for seconds | Decoding a BigTIFF |
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44) | Moderate (5.3) | Large transient allocation | Reading the ICC profile of a JPEG or PNG |

All five are availability only: none affects confidentiality or integrity,
and none is known to be exploited. The only fixed release is **4.1.2**.

## Why the library isn't upgraded yet

ImageSharp 4 is a major version, and Umbraco 18.2.1 (the newest 18.x) still
requires 3.1.12. The 18.3.0 release candidate (checked 16 October) moves to
ImageSharp 3.2.0, which the advisories still cover, so it is no exit either.
A load test of `Umbraco.Cms.Imaging.ImageSharp` against the
fixed versions, done on 12 October:

- ImageSharp 4.1.3 with ImageSharp.Web 4.0.2: `AddUmbracoImageSharp` throws
  `TypeLoadException`, so **the site would not start**.
- ImageSharp 4.1.3 with ImageSharp.Web 3.2.0: Umbraco's imaging code loads,
  but ImageSharp.Web 3.2.0 is compiled against ImageSharp 3 (its tag helper
  already fails with `MissingMethodException`), and the generic image paths
  can't be proven by loading alone. Neither vendor supports the combination.

## Where this site decodes untrusted images

1. **Branding uploads** (`BrandingAssetStorageService`): a tenant Admin's logo,
   favicon or hero image. This was the real exposure, and a customer can use
   it. The service decoded any format ImageSharp knows, trusting the
   client's `image/png` claim. It then re-encoded in the decoded format, so a
   crafted TIFF reached both TIFF decoding and the T4/T6 encoders. One
   tenant's upload could have stopped the process for every tenant.
   **Fixed:** the decoder now registers only the formats the asset type
   allows (PNG, or PNG and JPEG) and skips metadata, so no TIFF code and no
   ICC parser runs. Dimensions are read from the header before pixels are
   decoded. `BrandingAssetStorageServiceTests` covers TIFF (including 1-bit
   CCITT G4), BMP and GIF labelled `image/png`, a JPEG favicon, a corrupt PNG
   and EXIF stripping. Seven of its ten cases fail on the previous code.
2. **Umbraco media** (ImageSharp.Web middleware): decodes media files when a
   request carries resize or format commands. Only backoffice (operator)
   accounts can upload media, and such an account can already change far
   more than an image. **Residual, accepted below.** Recommended hardening:
   set `Umbraco:CMS:Imaging:HMACSecretKey` per environment, so only
   Umbraco-generated image URLs are processed.

No other code in the repository calls ImageSharp.

## Risk acceptance

| Field | Value |
|---|---|
| State | Accepted risk for the Umbraco media path; branding path fixed |
| Owner | NewbBuilder9909 (accepted by merging the PR that adds this record) |
| Expiry | **12 November 2026**. The NuGet audit fails again on that date. |
| Mechanism | `scripts/nuget-audit-exceptions.json`, read by `scripts/test-dependency-audit.ps1`. Each exception covers one advisory for ImageSharp 3.1.12 only, so a new advisory, a different version or a lapsed date fails CI, and each run prints the accepted advisories as warnings. |
| Exit | When Umbraco ships a release on ImageSharp 4.1.2 or later, upgrade Umbraco and delete the five exceptions. The audit warns about any exception that no longer matches. |
| Customer notification | Not required: availability only, no known exploitation, and the tenant-reachable path is closed. |

At expiry, check Umbraco's releases. Renew only with a new date and a line
here saying why.
