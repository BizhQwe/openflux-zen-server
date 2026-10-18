# OpenFlux upstream

- Release: [v0.13.0](https://github.com/p1neappleXpress/OpenFlux/releases/tag/v0.13.0).
- Commit: `4f9c786cdd1d9fcd0c133e2e0fec0c5a12522610`.
- Source archive: `https://codeload.github.com/p1neappleXpress/OpenFlux/tar.gz/refs/tags/v0.13.0`.
- Archive SHA-256: `473e810f7a45765c9f13082b4ae99db4de0a06c6d33d40324cb1efc99d0df76a`.

The complete server-side upstream delta from v0.7.3 is included. The previous
Windows/Linux scope and all local patches were audited against the original
v0.7.3 archive before the three-way migration. See the full inventory and
integration notes in [the migration report](../../docs/core-upgrade-v0.13.0.md).

Preserved patches: Telemost's per-type URL; transport-scoped cookie jars with
legacy fallback; logical IPv4 metering and optional IPC counters/status requests;
peer activity and Session expiry; Linux SO_MARK for the panel's scoped L3 guard;
Windows/UDP/heartbeat/PHP and Mail.ru batching regression tests. Mobile and iOS
applications and their build scripts remain excluded from this server tree.

New Zen integration: Bitrix24 and mixed URL flags, --transport-options-file for
private upstream registry parameters, and Bitrix24 on the classic CLI path.
The share format retains the official v0.13.0 transport allowlist. Mixed groups
and Bitrix24 credentials are omitted from links with an explanation; both need
manual client configuration. Wire framing/encryption stay upstream.

Avito room-creation diagnostics additionally report progress and the first HTTP
or network failure at the normal log level. Exhausted startup attempts preserve
the underlying error instead of only returning "could not create any room".
The retry policy and room/wire formats remain unchanged.
