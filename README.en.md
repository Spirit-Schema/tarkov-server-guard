# Tarkov Server Guard

[한국어](README.md) | English

<p align="center">
  <img src="assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard shield logo">
</p>

**You wait to get into a raid, only for a ping spike to get you kicked.** The time you spent preparing and queuing, and the gear you brought in, can be lost in an instant.

Even within the same selected region, you can connect to different server IPs with different connection quality.

**Tarkov Server Guard (TSG) is a free Windows app built to help reduce the time and gear lost to repeated high-ping kicks.** It uses EFT and Arena logs to review past connections, helping you identify servers with recurring problems and block further connections to them.

Current public release: **v0.8.3**. The app interface is in Korean; this page provides English instructions.

## At a glance

- **Reduce high-ping kick risk** — Compare ping and packet loss, then block problematic servers to prevent further connections to them.
- **Browse raid history** — Look up past EFT and Arena raids and their connection results.
- **Keep your notes** — Save raid and player-report notes, even after the game logs are deleted.

## Download

- **[Download installer — recommended](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [Download Portable ZIP — no installation](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10/11 · .NET Framework 4.8. For Portable, extract the ZIP and keep its files together. Both editions support updates after your confirmation.

The executable is unsigned, so Windows may show an unknown-publisher warning. Use the official downloads above; [SHA-256 checksums](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SHA256SUMS.txt) are available.

## Main features

- **Server comparison:** View current ping, in-game latency (RTT), packet loss, data center and estimated location.
- **Block-list management:** Add a reason for each block, unblock individual, selected or all entries, and export or import the list. Blocks remain manageable after logs are deleted.
- **Raid details:** Review maps, game modes, server allocation time, connection results and report activity. EFT also shows character type, party size and season when confirmed by the logs.
- **History filters:** Find up to 100 recent records by date range, game and data-center region.
- **Notes and screenshots:** Write raid notes, add tags, and manually record reported players and reasons. Screenshots are linked by file path; the original images are not copied.
- **Note backup and restore:** Export raid and player-report notes together and restore missing entries without overwriting existing notes. Original screenshot files are not included.
- **Convenience features:** Detect official-launcher and Steam installations, show the selected server regions, and install app updates after your confirmation.

## Getting started

1. Install or extract the app, open `TarkovServerGuard.exe`, and check the game-log paths. If automatic detection fails, use `직접선택` (Browse), then `적용` (Apply).
2. Choose a date range, game and region, then select `조회` (Refresh) to load records and check server quality.
3. Select `차단` (Block) or `해제` (Unblock) for a server. Windows requests administrator permission only when firewall rules are changed.

The in-app guide is available through `사용방법` (Usage guide). See [Troubleshooting — Korean](TROUBLESHOOTING.md) for additional help.

## Before you use TSG

- If you are assigned to a blocked server, loading may stall or the connection may fail. If a block prevents you from joining an EFT raid, leave the failed connection through the game's confirmation dialog and match again.
- Blocks apply **only to this PC and remain active after the app closes or Windows restarts**. They are not automatically shared with party members.
- It has not been confirmed whether blocking an Arena server results in a penalty for leaving the match. Read the separate warning before blocking.
- Current ping is measured now; in-game RTT and packet loss come from saved logs. A blocked server's current ping cannot be measured, and location data is an estimate.

## Privacy and license

TSG analyzes game logs on your PC and does not send raw logs, account data, session IDs or local paths to external services. Notes and settings stay on your PC. Ping checks, location-database downloads and GitHub update checks use the network.

Free for personal, non-commercial use. The source is available for inspection, but this is not open-source software. Redistribution, distribution of modified versions, sale and commercial use require permission.

Details: [Privacy and network — Korean](PRIVACY.md) · [License overview — Korean](LICENSING.md) · [Full license — Korean and English](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

This is not an official tool from Battlestate Games or Escape from Tarkov.

<details>
<summary>Source review and development documents — Korean</summary>

- [Publication scope](PUBLICATION_SCOPE.md)
- [Contributing](CONTRIBUTING.md)
- [Development and build instructions](DEVELOPMENT.md)

</details>

Developer · Spirit-Schema
