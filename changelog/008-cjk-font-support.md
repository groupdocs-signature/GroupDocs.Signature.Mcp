---
id: 008
date: 2026-10-06
type: feature
---

# CJK text signing works: a font package in the image, and a `font` parameter on `sign`

## What changed

- **`docker/Dockerfile` installs `fonts-noto-cjk`.** The image previously carried only the Microsoft core
  fonts, which have no Chinese, Japanese or Korean coverage, so signing CJK text failed outright.
- **`sign` gained an optional `font` parameter**, mapped to `TextSignOptions.Font`. It applies to
  `type: "text"` only - `QrCodeSignOptions` and `BarcodeSignOptions` have no font property.
- **A missing-font error now says which parameter fixes it.** The engine names the font it could not find
  but not how to pick another; the response now adds that, with concrete family names. The hint is appended
  only when the engine's message matches, so any other failure is returned untouched.

## Why

The font package alone does **not** fix CJK signing, which is what the ticket's title implies. Verified in
the image:

| Attempt | Result |
|---|---|
| install `fonts-noto-cjk` (30 CJK faces present) | still `Font SimSun was not found` |
| add a fontconfig alias `SimSun` -> `Noto Sans CJK SC` (`fc-match` confirms it resolves) | still `Font SimSun was not found` |
| install the package **and** pass `font: "Noto Sans CJK SC"` | **signs, and the text reads back intact** |

The engine does not consult fontconfig. It resolves fonts by family name, and the family it selects by
default for CJK scripts is a Windows font that does not exist on Debian. So the image needs the glyphs and
the caller needs a way to name the family - neither half is sufficient alone.

## Migration / impact

- **Image size grows by roughly 170 MB** (867 MB -> 1.04 GB). `fonts-noto-cjk` is the complete set; a
  narrower package would render some characters and box others instead of failing cleanly, which is worse.
- No behaviour change for existing callers: `font` is optional, and omitting it leaves the engine's own
  default exactly as before.
- **CJK text still requires the caller to pass `font`.** Signing CJK without it fails, now with an error
  that names the parameter and suggests families. The tool description states the requirement up front so
  an agent can get it right the first time.

Verified in the container with a licence, across Japanese, Chinese and Korean, each signed and then found
again intact; Latin signing with and without a font is unchanged.
