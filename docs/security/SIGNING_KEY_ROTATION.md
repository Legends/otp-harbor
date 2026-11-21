# Signing credential rotation runbook

## Scope

The primary Windows channel is Microsoft Store. The Store signs accepted MSIX packages; repository CI does not hold a Windows certificate or Partner Center publishing credential. The unsigned MSIX workflow produces a short-lived submission artifact that a maintainer uploads manually after verifying its source commit and SHA-256 metadata.

The previous SignPath Foundation application was not approved. The SignPath sections below are conditional response procedures for a future approved integration; `SIGNPATH_API_TOKEN` and `SIGNPATH_ORGANIZATION_ID` must not be configured unless that integration has been explicitly approved. The separate NetSparkle Ed25519 update key and Apple Developer ID/notarization credentials follow their own provider and release procedures.

## Microsoft Store account incident

After suspected Partner Center or Microsoft-account compromise:

1. Pause Store submissions and publishing without distributing the unsigned MSIX elsewhere.
2. Revoke affected sessions and credentials, enforce multi-factor authentication, and review account users and submission history.
3. Compare every affected Store submission with its GitHub workflow commit and `store-package.json` hash.
4. Contact Microsoft support and withdraw or replace a pending submission if provenance cannot be established.
5. Resume only after a controlled private-package install passes the physical acceptance checklist.

## Conditional SignPath API-token rotation

Rotate the submitter token immediately after suspected exposure, maintainer offboarding, or an unexpected signing request, and at the cadence required by SignPath:

1. Pause stable tag publication without weakening signature checks.
2. Revoke the affected API token in SignPath.
3. Review signing requests, approval records, GitHub workflow runs, repository access, and branch-protection changes for the affected period.
4. Create a replacement token with submitter permission only. It must not approve requests or configure the project.
5. Replace the `SIGNPATH_API_TOKEN` GitHub Actions secret.
6. Perform a controlled signing rehearsal and confirm that manual approval, repository origin, commit, metadata, and Authenticode verification all match the request.
7. Record the rotation and evidence without recording the token.

## Conditional SignPath certificate or service incident

If a Foundation certificate, SignPath organization, artifact configuration, or signing policy may be compromised:

1. Stop stable release publication and contact SignPath support and SignPath Foundation immediately.
2. Do not bypass origin verification, manual approval, metadata restrictions, or the Foundation certificate policy.
3. Preserve signing-request and GitHub workflow evidence without downloading or copying private signing material.
4. Follow SignPath Foundation's revocation or replacement instructions.
5. Identify every affected release and publish a security notice when users may be impacted.
6. Re-sign or replace trusted artifacts only through a newly verified policy and certificate path.

## Conditional SignPath verification checklist

- The SignPath request identifies the expected repository, tag, commit, artifact configuration, and GitHub-hosted workflow.
- A named approver manually approved the request.
- Every first-party Windows PE has a valid Authenticode signature and expected product metadata.
- Upstream binaries were not signed with the project's Foundation certificate.
- No exportable Windows certificate or password exists in GitHub Actions secrets or repository history.

Legacy local PFX signing helpers are not used by the Foundation-backed hosted workflow and must not be wired back into stable publication.
