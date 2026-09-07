# Tarkov Server Guard

[한국어](README.md) | English

<p align="center">
  <img src="assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard shield logo">
</p>

<br>

<p align="center">
All the servers you've selected...<br> For example, even within US West or China, there are multiple data centers and server IPs, and quality varies from server to server.<br> Some of these are low-quality server IPs that cause high ping and desync.
</p>

<br><br><br><br><br><br>

### Tarkov Server Guard

I started building this free Windows app to stop those situations where your ping spikes after you enter a raid, you get kicked, and you lose your time and gear.

It automatically reads the official Tarkov and Arena logs, so you can view and manage the quality of servers you've connected to and your raid history on one screen.

Current public release: **v0.8.5**. Choose Korean or English in Settings.

<br>

## At a glance

- Compare current ping, in-game ping (RTT) and packet loss to block only poor-quality servers, reduce the risk of high-ping kicks and prevent the resulting gear loss.<br> (In EFT, if you are assigned to a blocked server, TSG prevents the connection. You can select `나가기 확인` (Confirm exit) right away and keep your gear.)

- Review maps, game modes, server allocation, connection results and report activity from past EFT and Arena raids by date range.

- Save raid notes, player-report notes, tags and screenshot paths, and find them again in the note archive even after the game logs are deleted.<br> (Report activity is available, but reported players' nicknames cannot be retrieved. Click the `유저신고` text to write a player-report note, or the yellow note speech bubble to write a raid note.)

<br>

## Download

- **[Download installer — recommended](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [Download Portable ZIP — no installation](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10/11 · .NET Framework 4.8. For Portable, extract the ZIP and keep its files together. Both editions support updates after your confirmation.

The executable is unsigned, so Windows may show an unknown-publisher warning. Use the official downloads above; [SHA-256 checksums](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SHA256SUMS.txt) are available.

<br>

## What's new

Changes since 0.8.3:

- TSG remembers your window size and adjusted column widths for your next session.
- Choose Korean or English in Settings.
- Search Saved Notes by text, tags, map, player name, or report reason, and filter by note type.
- Add server IPs shared by your party as named lists, then remove a list's blocks together. Your personal blocks and blocks still used by other party lists stay in place.
- Made the interface easier to use.
- Improved log loading speed and reduced memory use when reading large logs.
- Made note saving, backups, and restores more reliable.

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

1. Install or extract the app, open `TarkovServerGuard.exe`, and check the game-log paths. If automatic detection fails, use `Browse`, then `Apply`.
2. Choose a date range, game and region, then select `Scan` to load records and check server quality.
3. Select `Block` or `Unblock` for a server. Windows requests administrator permission only when firewall rules are changed.

The in-app guide is available through `Usage Guide`. See [Troubleshooting — Korean](TROUBLESHOOTING.md) for additional help.

<br>

<details>
<summary><strong>How to use party blocks — step by step</strong></summary>

Block the **game server IPs** your party shares, then remove that list's blocks together when you no longer need them.

**① Add your party's IPs**

1. Open `Blocked Servers` → `Add Party IPs`.
2. Enter a recognizable `List Name`, such as `Tonight's party`.
3. Under `Shared By`, choose `Party Leader` or `Party Member` to match who sent the IPs.
4. Paste the server IPs into the input box. Use one IP per line if there are several.
5. Select `Preview IPs` and review the addresses. Invalid addresses are excluded; correct them if needed.
6. Select `Block IPs`, then choose `Yes` when Windows asks for administrator permission.

**② Remove the list's blocks later**

1. Open `Blocked Servers` → `Remove Party Blocks`.
2. Select the list name you entered, such as `Tonight's party`.
3. Review which IPs will be unblocked or kept, then select `Remove Blocks`. Approve the Windows permission prompt if it appears.

**Your existing personal blocks and blocks still used by another party list stay in place.**

- Blocks apply **only to your PC**. They are not sent to your party automatically; each person needs to add the same IPs.
- Blocks remain active after TSG closes or Windows restarts. Remove them when you no longer need them.
- Enter the **game server's IP**, not a party member's home IP.

</details>

<br>

## Before you use TSG

- If you are assigned to a blocked server, loading may stall or the connection may fail. If a block prevents you from joining an EFT raid, leave the failed connection through the game's confirmation dialog and match again.
- Blocks apply **only to this PC and remain active after the app closes or Windows restarts**. They are not automatically shared with party members.
- It has not been confirmed whether blocking an Arena server results in a penalty for leaving the match. Read the separate warning before blocking.
- Current ping is measured now; in-game RTT and packet loss come from saved logs. A blocked server's current ping cannot be measured, and location data is an estimate.

<br>

## Privacy and license

TSG analyzes game logs on your PC and does not send raw logs, account data, session IDs or local paths to external services. Notes and settings stay on your PC. Ping checks, location-database downloads and GitHub update checks use the network.

> **Scope of free use:** All features in the current release are free for personal, non-commercial use. Future features or separate services may be offered for a fee.

The source is available for inspection, but this is not open-source software. Redistribution, distribution of modified versions, sale and commercial use require permission.

Details: [Privacy and network — Korean](PRIVACY.md) · [License overview — Korean](LICENSING.md) · [Full license — Korean and English](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

This is not an official tool from Battlestate Games or Escape from Tarkov.

<details>
<summary>Source review and development documents — Korean</summary>

- [Publication scope](PUBLICATION_SCOPE.md)
- [Contributing](CONTRIBUTING.md)
- [Development and build instructions](DEVELOPMENT.md)

</details>

Developer · Spirit-Schema
