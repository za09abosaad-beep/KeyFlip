# Releasing KeyFlip

## Cut a release

1. Make sure `main` is green (CI workflow).
2. Tag and push:

   ```
   git tag v1.2.3
   git push origin v1.2.3
   ```

3. The **Release** workflow runs the tests, publishes `KeyFlip.exe` with version `1.2.3`
   (the tag without the `v`), signs it if signing is configured, and creates a GitHub Release
   with `KeyFlip.exe`, `KeyFlip.exe.sha256` and generated release notes.

If no signing is configured, the workflow still releases, but shows a warning
("Releasing an UNSIGNED exe") and the exe is unsigned. Check the workflow log before
telling users a release is signed.

If both options below are configured, SignPath is used.

Signing is set up by the repository owner: the secrets and variables below live in the main
repository's settings, and tag pushes from forks do not run the release with them.

## Signing option A: SignPath Foundation (free for open source)

The certificate is issued to **SignPath Foundation**, so Windows shows that name as the publisher,
not yours.

1. Apply at <https://signpath.org/> (SignPath Foundation, open-source program). The project must
   be open source with an OSI license (KeyFlip is MIT) and meet their other conditions. Approval can take a while.
2. Once approved, in SignPath create/confirm a **project** and a **signing policy**
   (for example `release-signing`). Add the GitHub Actions trusted build system, link this repository, and
   create an **artifact configuration**. The workflow uploads a zip that contains `KeyFlip.exe`
   at its root, so the configuration should sign that exe (Authenticode).
3. Create an API token for a CI user that is allowed to submit to the policy.
4. In GitHub, go to **Settings > Secrets and variables > Actions** and add:
   - Secrets: `SIGNPATH_API_TOKEN`, `SIGNPATH_ORGANIZATION_ID`
   - Variables: `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`
5. Push a tag. The workflow uploads the unsigned exe, waits for SignPath, and downloads the signed exe.
   Release signing policies may require a manual approval in the SignPath web app; the workflow waits for it.

SignPath's GitHub action documentation: <https://docs.signpath.io/trusted-build-systems/github>

## Signing option B: your own certificate

Requires a code signing certificate (a `.pfx` file with a password).

1. Encode it: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx")) | Set-Clipboard`
2. In **Settings > Secrets and variables > Actions**, add secrets:
   - `SIGNING_CERT_PFX_BASE64` (the base64 text)
   - `SIGNING_CERT_PASSWORD`
3. Push a tag. The workflow signs with `signtool` (SHA-256, DigiCert timestamp), verifies the
   signature, and deletes the temporary `.pfx`.

Note: a new certificate, even a valid one, may still trigger SmartScreen until it builds reputation
(EV certificates do not have this delay).

## Verify a release

```
Get-FileHash KeyFlip.exe -Algorithm SHA256
Get-AuthenticodeSignature KeyFlip.exe
```

The hash must match `KeyFlip.exe.sha256`. `Status` should be `Valid` for a signed build.

## Notes

- Third-party actions are pinned to major versions (`@v3`, `@v7`). For stricter supply-chain safety, pin them to commit SHAs.
