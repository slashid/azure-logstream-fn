# azure-logstream-fn — agent guidance

This repo builds the SlashID Azure Monitor forwarder. Read README.md first.

## Release contract (load-bearing — do not improvise)

- Tagging `vX.Y.Z` only STAGES a build: CI creates a **pre-release**. It is not
  production, and nothing anywhere follows "latest".
- Promotion to a published release is a MANUAL act, done only after sandbox
  validation (README → Release flow).
- Production adoption happens exclusively in `slashid/ng-evangelion` by bumping
  the pinned versioned asset URL (never a draft, never a pre-release). If you
  are asked to "release" or "ship" this function, that pin bump — plus the
  README button-tag bump here — IS the ship step; do not skip the staging and
  promotion gates before it.
- Before a release tag: azuredeploy.json's `packageUri`/`forwarderVersion`
  defaults must match the tag (CI enforces this).

## Contracts with ng-evangelion (never change one side alone)

- App-setting names: EventHubConnection, EVENTHUB_NAME, EVENTHUB_CONSUMER_GROUP,
  SLASHID_EVENTS_ENDPOINT, SLASHID_PUSH_AUTH_TOKEN, FORWARDER_VERSION.
- Control event types: slashid.forwarder.heartbeat, slashid.forwarder.record_dropped.
- Versions are bare semver (no leading `v`) everywhere except git tags and URLs.
- The no-loss contract: ForwardEvents must keep `[FixedDelayRetry(-1, …)]` and
  every delivery failure must throw. A reflection test pins this — do not
  weaken either side.
