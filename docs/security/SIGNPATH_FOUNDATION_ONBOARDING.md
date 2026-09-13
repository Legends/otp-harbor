# SignPath Foundation onboarding

## Status

The initial SignPath Foundation application was not approved because OTP Harbor did not yet have sufficient independent public visibility and adoption signals. There is no Foundation certificate and no active SignPath-backed production channel. Microsoft Store is now the primary Windows distribution path.

This document is retained only as a future reapplication runbook. Reapplication is deferred until GitHub stars, release-download counts, and other independently verifiable active-user signals show materially broader adoption. Do not configure SignPath credentials, set `SIGNPATH_PRODUCTION_ENABLED`, describe a release as SignPath-signed, or activate the direct Windows download workflow unless a later application is approved and the returned Authenticode signatures have been verified.

Repository evidence:

- OSI-approved project license: [MIT](../../LICENSE.txt)
- Product and download documentation: [README](../../readme.md)
- Published release form: Windows x64 ZIP packages on [GitHub Releases](https://github.com/Legends/otp-harbor/releases)
- Code signing policy and roles: [CODE_SIGNING_POLICY.md](../../CODE_SIGNING_POLICY.md)
- Privacy disclosure: [PRIVACY.md](../../PRIVACY.md)
- Vulnerability reporting: [SECURITY.md](../../SECURITY.md)
- Automated build, test, dependency, CodeQL, and secret-scanning workflows under `.github/workflows`

## Maintainer prerequisites

Before reapplying:

1. Enable multi-factor authentication on the GitHub account and require it for every future maintainer with repository or SignPath access.
2. Apply and verify the `master` branch protections in [BRANCH_PROTECTION.md](BRANCH_PROTECTION.md). External pull requests must be reviewed, required checks must pass, force pushes and deletion must remain disabled, and release/trust files must remain covered by `CODEOWNERS`.
3. Review the project-owned raster record in [ASSET_PROVENANCE.md](../assets/ASSET_PROVENANCE.md) whenever an embedded image changes. Unknown earlier icon and flag files have been replaced; do not reintroduce them.
4. Establish credible external trust signals: sustained releases, real Store installs/reviews, independent contributors, stars/forks, and third-party discussion or coverage.
5. Keep public evidence of supported installation, security reporting, release provenance, and real-world platform testing.
6. Read and accept the current [SignPath Foundation conditions](https://signpath.org/terms.html), then submit a new [application](https://signpath.org/apply).

Certificate approval is discretionary. Repository preparation cannot guarantee acceptance, particularly while a project has limited verifiable reputation.

## SignPath project configuration

After acceptance, configure the values expected by the release workflow:

| Setting | Value |
| --- | --- |
| Repository | `https://github.com/Legends/otp-harbor` |
| Project slug | `totp-manager` |
| Signing policy slug | `release-signing` |
| Artifact configuration slug | `windows-release-v1` |
| GitHub Actions variable | `SIGNPATH_ORGANIZATION_ID` |
| GitHub Actions secret | `SIGNPATH_API_TOKEN` |

The SignPath project slug remains `totp-manager` as an external compatibility identifier after the OTP Harbor rebrand. The GitHub repository URL and required PE product-name metadata use the current brand. First-party assembly names remain `TOTP.*`; changing them is outside the public rebrand and would require a separately reviewed signing-configuration migration.

Use the predefined GitHub.com trusted build system, install the SignPath GitHub App if SignPath requests it, and grant it access only to this repository. Give the API token submitter permission only; do not give it approval or project-configuration authority.

Create `windows-release-v1` by uploading a sample of the workflow artifact named `signpath-unsigned-windows-<commit>`. The artifact is a ZIP with `release-publish` and `release-fast` directories. Review the generated configuration so that it:

- signs every first-party `TOTP*.exe` and `TOTP*.dll` under those two directories;
- does not sign Microsoft, Avalonia, OpenCV, or any other upstream binary;
- requires `OTP Harbor` for PE product-name metadata;
- requires the signing-request `version` parameter for product-version metadata; and
- rejects missing, additional first-party, or structurally unexpected signing targets.

Configure `release-signing` to use the Foundation certificate, GitHub origin verification, and manual approval by the named approver for every request. Do not add a test-signing policy that can publish artifacts under the Foundation certificate without equivalent origin controls.

## First signed-release rehearsal

After SignPath confirms the project configuration, run **Build, Test and Publish OTP Harbor App** manually from `master` with `signpath_rehearsal` enabled. Set `rehearsal_version` to the intended three-part release version. This path builds both Windows variants, submits the bounded payload through the normal release signing policy, validates every returned first-party signature and product-version field, and retains the signed result for one day. It does not create a tag, GitHub Release, package manifest, or update feed.

Approve the rehearsal request in SignPath exactly as a release request. Origin verification and the `release-signing` policy must restrict the request to reviewed `master` or `release/*` sources. A successful rehearsal proves integration for that commit but does not authorize later binaries automatically.

Create the immutable stable tag only after the rehearsal and the remaining release gates pass. The stable workflow fails closed if the organization ID, API token, approval, returned structure, PE metadata, or Authenticode verification is missing.

Before publishing, verify in the draft release workflow evidence that:

1. all prerequisite build and security jobs passed on GitHub-hosted runners;
2. the signing request references the expected repository, tag, and commit;
3. the request received manual approval;
4. every first-party Windows PE in both packages has a valid Foundation-backed Authenticode signature;
5. upstream binaries were not re-signed;
6. artifact manifests and the Ed25519 appcast validate; and
7. the release page contains a link headed **Code signing policy**.

Retain the signing-request URL and workflow run URL as release evidence. Never copy a SignPath API token, certificate material, or update private key into that evidence.
