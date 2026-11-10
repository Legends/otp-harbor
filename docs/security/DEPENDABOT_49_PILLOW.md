# Dependabot alert 49: Pillow marketing tooling

Status: reviewed; remediation deferred to a separate change at the user's request.
The alert remains open and has not been dismissed.

[Alert 49](https://github.com/Legends/otp-harbor/security/dependabot/49) identifies
`Pillow==11.2.1` in `scripts/marketing/requirements.txt` as affected by critical
[CVE-2026-96639 / GHSA-v9pv-5gc8-w2m6](https://github.com/python-pillow/Pillow/security/advisories/GHSA-v9pv-5gc8-w2m6).
Upstream lists Pillow 13.0.0 as patched. The issue concerns native buffer reuse
when image frames change modes and can cause memory corruption or code execution.

Pillow is used by repository marketing generation and validation tooling, including
the Pages job. It is not a dependency of the .NET desktop or Android scanner.
Inspection found no image-frame seeking or `ImageSequence.Iterator` use in the
marketing scripts; their current inputs are local, recorded marketing assets.
This narrows the observed exposure but does not establish that the affected pin
is safe or justify dismissing the alert. No vault or OTP data flow changes are
required for the dependency upgrade.

An isolated Python 3.13 environment successfully installed Pillow 13.0.0, passed
`pip check`, and decoded benign generated MPO transitions from grayscale to RGB
and CMYK with correct dimensions and pixels. However, its resized output differs
from the reviewed derivatives: the existing strict phone-screenshot and framed
gallery pixel comparisons fail. The trial pin and generated asset changes were
reverted; no vulnerable decoder workaround or relaxed image comparison remains.

The follow-up should upgrade the pin, add a safe mode-transition regression check,
regenerate affected derivatives from unchanged canonical captures, synchronize
provenance and submission mirrors, and run marketing, website, QR-readability and
browser checks. Repeated generation must remain deterministic. Review the image
differences and obtain the required publication approval before deployment.
