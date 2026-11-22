# Proposed update for RFP #4758

Suggested follow-up text. Not posted to GitLab. The maintainer's toolchain-policy
question is already in the request; this update supplies supporting evidence.

---

Supporting review materials are now public:

- [Review packet and remaining gates](https://github.com/Legends/otp-harbor/blob/master/packaging/fdroid/submission/README.md)
- [Disabled metadata/build recipe](https://github.com/Legends/otp-harbor/blob/master/packaging/fdroid/submission/metadata/io.github.legends.otpharbor.yml)
- [Locked NuGet dependency inventory](https://github.com/Legends/otp-harbor/blob/master/packaging/fdroid/submission/NUGET_DEPENDENCIES.json)
- [Validation results and logs](https://github.com/Legends/otp-harbor/blob/master/packaging/fdroid/submission/validation/validation-results.json)

I have prepared a disabled `fdroiddata` recipe for Android release `3.0.3`
(`android-v3.0.3`, commit `c801e73224f3d6abed33396f858c54866b8e70f2`).

Preparatory validation with `fdroidserver 2.4.5`:

- Metadata parsing, canonical formatting and lint pass. Lint also passes on a
  temporary copy without the build-disable marker.
- The tagged source scan reports zero fatal findings. This is before workload
  installation, NuGet restore or compilation; no final APK scan has been run.
- The tag pins .NET SDK `10.0.402`. The recipe verifies the official Linux SDK
  archive checksum and uses locked application dependency restores.
- The Android solution's seven projects contain 137 locked NuGet package/version
  pairs. An inventory of their content hashes is prepared for review; SDK/workload
  packs require separate review.

The intended initial route is F-Droid-managed signing. Upstream production
signing keys will remain private. Bit-for-bit upstream APK reproducibility and
upstream-signature reuse are not being claimed.

The remaining question is which toolchain and dependency-origin strategy would
be acceptable for this .NET/Avalonia application, especially its .NET Android
workload and native libraries delivered through NuGet. Is there an existing
approved source-build or provenance approach we should follow?

The recipe stays disabled pending that review and a clean buildserver/APK scan.
The existing maintainer-operated binary repository remains separate from an
official F-Droid inclusion.
