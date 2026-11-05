# Evergreen mobile

Flutter app for Android and iOS. For now it's sign-in only: the same accounts and the
same call as the admin web app (`POST /api/auth/admin-login` on Globeweigh.Api), reached
off-site through the Cloudflare tunnel at `https://evergreen.specdigital.com`.

Built on the Specbooks app's setup (`D:\Git-Spec\specbooks\mobile`): Riverpod, dio,
go_router, Shorebird code push, fastlane for the stores, iOS builds on the Mac.
Not to be confused with `Globeweigh.Picking`, the Zebra scanner app on the LAN.

## Running it

```
npm run mobile          # against the live API (evergreen, via the tunnel)
npm run mobile:local    # against the API on this PC (:5099, via adb reverse)
```

Both run on the attached Android emulator through `tools/flutter_dev.dart`, which hot
reloads by itself whenever a `.dart` file under `lib/` changes. Nothing to press, and
edits from Claude show up too. It also takes `POST /reload`, `POST /restart` and
`GET /status` on `http://127.0.0.1:8991`.

## Releasing (from the repo root)

| Command | What it does |
|---|---|
| `npm run mobile:release:android` | Bumps the build number, `shorebird release android`, uploads to Play **Internal testing** |
| `npm run mobile:release:android:prod` | The same, to **Production** at 100% (Google review, then live) |
| `npm run mobile:release:ios` | Bumps, builds on the Mac with `shorebird release ios`, uploads to **TestFlight** |
| `npm run mobile:release:ios:prod` | The same, then submits for **App Store review** with auto-release |
| `npm run mobile:patch:android` / `mobile:patch:ios` | Shorebird over-the-air patch to the current release (Dart changes only) |

Add `-- -Version 1.1.0` to a release for a new version number, and
`-- -Track staging` to a patch to stage it first.

A patch needs nothing else: the app checks Shorebird on launch and resume, shows
"Downloading update…", then asks for a restart. There is no store-version prompt yet
(Specbooks has one through `GET /api/app/version`; Globeweigh.Api doesn't).

## Credentials (reused from Specbooks/SpecGauge, never committed)

- `android/key.properties` → the upload keystore in `D:\specgauge-keys`
- `D:\specgauge-keys\play-service-account.json` → Play uploads (fastlane)
- App Store Connect API key `JMRW9TWS55` → on the Mac in `~/.appstoreconnect/private_keys`
- `.env.mac` → the Mac login password, to unlock its keychain over SSH (copy `.env.mac.example`)
- Shorebird: `shorebird.yaml` (app "Evergreen", not secret)

## One-time store setup (before the first release)

iOS comes first; Android is parked for now (the project builds, but there's no Play listing).

1. **Apple**: register the bundle ID `com.specdigital.evergreen` (developer.apple.com →
   Identifiers) and create the app in App Store Connect with that bundle ID. Then
   `npm run mobile:release:ios` makes the "Evergreen App Store" profile itself
   (`fastlane ios profiles`) and puts the build on TestFlight.
2. **Google Play** (later): create the app in the Play Console (`com.specdigital.evergreen`),
   give the `play-service-account` service account access to it, and upload the
   **first** AAB by hand (Play won't accept an API upload for a brand-new app). Build
   it with `shorebird release android`, then use `npm run mobile:release:android` from then on.
