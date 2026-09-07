# Tarkov Server Guard

[한국어](README.md) | English

<p align="center">
  <img src="assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard shield logo">
</p>

<br>

<p align="center">
All the servers you've selected...<br><br> For example, even within US West or China, there are multiple data centers and server IPs,<br> and quality varies from server to server.<br> Some of these are low-quality server IPs that cause high ping and desync.
</p>

<br><br>

### [Tarkov Server Guard]

I started building this free Windows app to stop those situations where your ping spikes after you enter a raid,<br> you get kicked, and you lose your time and gear.

It automatically reads the official Tarkov and Arena logs,<br> so you can view and manage the quality of servers you've connected to and your raid history on one screen.

Current public release: **v0.8.3**. The app interface is in Korean; this page provides English instructions.

<br>

## At a glance

- Compare current ping, in-game ping (RTT) and packet loss to block only poor-quality servers,<br> reduce the risk of high-ping kicks and prevent the resulting gear loss.

  (In EFT, if you are assigned to a blocked server, TSG prevents the connection.<br> You can select `나가기 확인` (Confirm exit) right away and keep your gear.)

  <br>

- Review maps, game modes, server allocation, connection results and report activity<br> from past EFT and Arena raids by date range.

  <br>

- Save raid notes, player-report notes, tags and screenshot paths,<br> and find them again in the note archive even after the game logs are deleted.

  (Report activity is available, but reported players' nicknames cannot be retrieved.<br> Click the `유저신고` text to write a player-report note, or the yellow note speech bubble to write a raid note.)

<br>

## Download

- **[Download installer — recommended](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [Download Portable ZIP — no installation](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10/11 · .NET Framework 4.8. For Portable, extract the ZIP and keep its files together. Both editions support updates after your confirmation.

The executable is unsigned, so Windows may show an unknown-publisher warning. Use the official downloads above; [SHA-256 checksums](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SHA256SUMS.txt) are available.

<br>

## Main features

- **Review and manage raid records:** Check maps, game modes, connection results and report activity, and keep notes, tags and screenshot links alongside your records.

- **History filters:** Find up to 100 recent records by date range, game and data-center region.

- **Server comparison:** View current ping, in-game latency (RTT), packet loss, data center and estimated location.

- **Block-list management:** Add a reason for each block, unblock individual, selected or all entries, and export or import the list. Blocks remain manageable after logs are deleted.

- **Note backup and restore:** Export raid and player-report notes together and restore missing entries without overwriting existing notes. Original screenshot files are not included.

- **Convenience features:** Detect official-launcher and Steam installations, show the selected server regions, and install app updates after your confirmation.

<br>

## Getting started

1. Install or extract the app, open `TarkovServerGuard.exe`, and check the game-log paths. If automatic detection fails, use `직접선택` (Browse), then `적용` (Apply).
2. Choose a date range, game and region, then select `조회` (Refresh) to load records and check server quality.
3. Select `차단` (Block) or `해제` (Unblock) for a server. Windows requests administrator permission only when firewall rules are changed.

The in-app guide is available through `사용방법` (Usage guide). See [Troubleshooting — Korean](TROUBLESHOOTING.md) for additional help.

<br>

## Before you use TSG

- If you are assigned to a blocked server, loading may stall or the connection may fail. If a block prevents you from joining an EFT raid, leave the failed connection through the game's confirmation dialog and match again.
- Blocks apply **only to this PC and remain active after the app closes or Windows restarts**. They are not automatically shared with party members.
- It has not been confirmed whether blocking an Arena server results in a penalty for leaving the match. Read the separate warning before blocking.
- Current ping is measured now; in-game RTT and packet loss come from saved logs. A blocked server's current ping cannot be measured, and location data is an estimate.

<br>

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
