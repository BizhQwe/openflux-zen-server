# OpenFlux upstream

- Release: [v0.7.3](https://github.com/p1neappleXpress/OpenFlux/releases/tag/v0.7.3).
- Commit: `f12e981629a18c8c01007d59ac002254c02972bb`.
- Source archive: `https://codeload.github.com/p1neappleXpress/OpenFlux/tar.gz/refs/tags/v0.7.3`.
- Archive SHA-256: `b18f30ee53c4dc134bbce15666e7ef678f886c17fe7226b7cbc9985d774ea589`.

The complete seven-file upstream change from v0.7.2 is included: CHANGELOG,
node-install script and pin, batched transport, Mail.ru carrier, Session and
the optional BatchSizeHinter interface. Session's new batch hint is merged
into the existing local file, preserving the Zen Server changes.

Zen Server keeps its Windows/Linux build scope, per-transport Telemost URL,
optional IPC peer status, and Linux raw-socket marking used by the panel's
scoped L3 RST guard. Network framing, encryption and share links retain the
upstream format. Mobile applications and their build scripts are excluded.

CookieStore also accepts optional transport-scoped records preloaded by the
panel while the tunnel is stopped. The Session and classic startup paths
prefer those records and retain the original document/script-key fallback
for existing stores. This separates auto-created room carriers with empty
URLs without changing cookie IPC or the network protocol.

`transport/batch_hint_test.go` checks the Session hint, environment override,
and round-trip of a 128 KB burst in one existing-v2 carrier message.

Zen Server also meters logical IPv4 packets above compression and encryption.
IPC status adds optional `traffic_bytes_in` / `traffic_bytes_out` counters and
supports an optional `status` request for the final snapshot before stopping.
Original carrier byte counters, framing, handshake and share links are unchanged.
Older clients do not need an update for these local diagnostic extensions.
