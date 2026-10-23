# Publishing to the Microsoft Store

The CI workflow (`.github/workflows/build.yml`, job `msix`) produces `ValueStreamMapper.msix` on every push to `main`. This page explains how to turn that into a Store listing.

## 1. Partner Center account
1. Go to https://partner.microsoft.com/dashboard and enroll in the **Microsoft Store developer program** (one-time fee: about $19 individual / $99 company). Use the same Microsoft account you want the app published under.
2. In **Apps and games → New product → MSIX or PWA app**, reserve the name **Value Stream Mapper** (Store names are unique; have an alternative ready, e.g. "Value Stream Mapping").

## 2. Copy the product identity into the repository
Open the new product → **Product management → Product identity**. Note the three values and add them as *repository variables* in GitHub (**Settings → Secrets and variables → Actions → Variables**):

| GitHub variable | Partner Center value | Example |
|---|---|---|
| `MSIX_IDENTITY_NAME` | Package/Identity/Name | `12345VincenzoRenzo.ValueStreamMapper` |
| `MSIX_PUBLISHER` | Package/Identity/Publisher | `CN=A1B2C3D4-0000-1111-2222-333344445555` |
| `MSIX_PUBLISHER_DISPLAY_NAME` | Package/Properties/PublisherDisplayName | `Vincenzo Renzo` |
| `MSIX_VERSION` | Package version, four parts, last must be `0` | `1.0.0.0` |

The manifest must match these exactly or the Store rejects the upload. Push any commit (or run the workflow manually) and download the `ValueStreamMapper-msix` artifact.

## 3. Signing — what the certificate is for
- **Store submissions do not need your own certificate.** The Store re-signs the package with Microsoft's certificate on ingestion; it only checks that the identity matches. The CI's signature (self-signed by default) is harmless here.
- **Sideloading outside the Store** (handing the `.msix` to customers directly, or testing on a PC) does need a trusted signature. Two options:
  - *Testing:* install `ValueStreamMapper-TestCertificate.cer` from the artifact into **Trusted People** on the test PC (see `HOW-TO-INSTALL.txt`), then install the `.msix`.
  - *Real distribution:* buy a code-signing certificate (OV or EV, e.g. from DigiCert, Sectigo, GlobalSign; EV avoids SmartScreen warnings fastest), export it as a password-protected `.pfx`, and store it as GitHub **secrets** `MSIX_PFX_BASE64` (the file base64-encoded: `base64 -i cert.pfx | pbcopy` on a Mac) and `MSIX_PFX_PASSWORD`. The workflow then signs with it automatically. The certificate's subject must equal `MSIX_PUBLISHER`.

## 4. Submit
1. In Partner Center open the product → **Start your submission**.
2. **Packages:** upload `ValueStreamMapper.msix` from the artifact. Wait for validation (a few minutes); fix any identity mismatch by correcting the variables and rebuilding.
3. **Properties:** category *Business → Productivity* (or *Productivity*), no Store-managed in-app purchases.
4. **Age ratings:** complete the IARC questionnaire (no objectionable content → suitable for all ages).
5. **Store listing (English):** reuse the App Store texts — description, promotional text ("What's new" / short description), and the keywords as *search terms* (7 max, each up to 30 characters). Upload screenshots: at least one **desktop** screenshot, PNG, between 1366×768 and 3840×2160 (the `ui-screenshots` CI artifact contains 1380×860 frames that qualify, or capture the app at 1920×1080 on a PC). Add the 300×300 Store logo if asked (`packaging/Assets/LargeTile.png` scaled works).
6. **Privacy policy URL:** required — the same page you use for the Apple listing.
7. **Pricing and availability:** free or paid, markets, release date (manual or automatic after certification).
8. **Submit to the Store.** Certification usually takes 1–3 business days; failures arrive by email with the policy number (common ones: missing privacy URL, screenshot size, identity mismatch).

## 5. Updating
Bump `MSIX_VERSION` (e.g. `1.0.1.0`), push, download the new artifact, and create a new submission with the new package. Users receive the update automatically.
