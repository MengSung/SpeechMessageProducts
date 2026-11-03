# ChurchReport Image Input Contract

## Scope and entry points

P0-12A uses Services/Images/ImageInputValidator for all six image decode sites:
MemberInfo upload normalization, Personal upload, the shared NewPerson upload,
MemberInfo crop/fit thumbnails, and Personal crop thumbnails. The NewPerson
normalizer serves both SaveNewPerson attachments and UploadNewPersonImage.
Do not introduce a separate default Image.Load path in controllers.

## Content and size validation

Keep the existing declaration whitelist (.jpg/.jpeg/.png/.gif and JPEG/PNG/GIF
content types), the existing upload size limit of 5 MiB, and authorization flow.
Declaration checks do not establish the actual format. A private Configuration
registers only JPEG, PNG and GIF modules; never clone Configuration.Default,
which would keep other formats registered. WebP, TIFF, BMP and other formats
masquerading as JPG are rejected before full decoding.

Identify dimensions with the same restricted options before Load. Reject a side
above 8192 or a long width-times-height above 40,000,000; equality is allowed.
Decode only the first frame because the existing output is a single JPEG.
Preserve orientation, square padding, crop/fit and JPEG quality from each
existing path. Golden tests compare decoded dimensions and pixels, not encoder
bytes. Every fixture is synthetic and intentionally declares JPG/JPEG even when
the actual format differs.

## Rejections, publication and ownership

ImageInputRejectedException carries only fixed application messages, without
raw decoder exception messages or input references. Unsupported/corrupt content
and dimension violations are expected refusals and do not notify administrators.
No upload may fall back to storing the original unvalidated bytes.

Thumbnails deliberately keep their preexisting fallback: return the same original
array when input is refused, without throwing into the page. Genuine unexpected
faults still pass the original Exception to the existing shared diagnostics entry;
JSON uses fixed messages. A photo refusal after a newcomer has already been
created must not claim the newcomer creation failed or prompt a duplicate create.

The caller owns its source stream and the returned Image; using scopes dispose
both, plus output streams, before publishing a new JPEG byte array. The helper
borrows the source and owns only its temporary buffer. Non-seekable input is
buffered at most 5 MiB plus one byte before rejection; the buffer is disposed on
success and failure. Options are operation-local. Configuration is private,
unchanging and contains no user data; its pixel pool is disabled so no previous
request image is retained by that decoder. No timers, background tasks or mutable
user-specific statics are introduced.

## Verification

Retain allowed-format golden cases and all three thumbnail fallback contracts.
Cover disguised WebP/TIFF/BMP, corrupt input, size limits before pixel decoding,
exact dimension limits, bounded non-seekable input, first-frame GIF behavior,
compiled calls to the shared helper, fixed error output and the actual shared
exception diagnostics owner with an in-memory sender. Concurrent mixed inputs
must stay isolated, streams must drain to zero and repeated real decodes must
not add ImageSharp allocations abandoned to finalizers. No D365/LINE access.

## Fixed interface text

New rejection and failure text lives in ImageInputText.resx. I18nCheck generates
ImageInputText.g.cs; verify checks both this file and SharedText.g.cs. The local
facade copies only the fixed Traditional Chinese column into a frozen dictionary,
with no runtime user-specific cache or culture mutation. Do not increase the CJK
baseline or ignore the validator to accept new display literals.
