# Tarkov Server Guard v0.8.3

<details>
<summary><strong>English — Overview, download, release notes and features</strong></summary>

<br>

<p align="center">
All the servers you've selected...<br> For example, even within US West or China, there are multiple data centers and server IPs, and quality varies from server to server.<br> Some of these are low-quality server IPs that cause high ping and desync.
</p>

<br><br><br><br><br><br>

### Tarkov Server Guard

I started building this free Windows app to stop those situations where your ping spikes after you enter a raid,<br> you get kicked, and you lose your time and gear.

<br>

It automatically reads the official Tarkov and Arena logs,<br> so you can view and manage the quality of servers you've connected to and your raid history on one screen.

<br>

**v0.8.3 has a Korean interface.** The following is English documentation for this release.

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

- **[Download installer — recommended](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [Download Portable ZIP — no installation](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10/11 · .NET Framework 4.8. For Portable, extract the ZIP and keep its files together. Both editions support updates after your confirmation.

The executable is unsigned, so Windows may show an unknown-publisher warning. Use the official downloads above; [SHA-256 checksums](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SHA256SUMS.txt) are available.

<br>

## What's new

- Raid history now shows character type, party size and season when available in EFT logs.
- After a server is blocked, TSG shows its recent usage count and signs of connection problems.

<br>

## Main features

- **Review and manage raid records:** Check maps, game modes, connection results and report activity, and keep notes, tags and screenshot links alongside your records.

- **History filters:** Find up to 100 recent records by date range, game and data-center region.

- **Server comparison:** View current ping, in-game latency (RTT), packet loss, data center and estimated location.

- **Block-list management:** Add a reason for each block, unblock individual, selected or all entries, and export or import the list. Blocks remain manageable after logs are deleted.

- **Note backup and restore:** Export raid and player-report notes together and restore missing entries without overwriting existing notes. Original screenshot files are not included.

- **Convenience features:** Detect official-launcher and Steam installations, show the selected server regions, and install app updates after your confirmation.

<br>

## Getting started and important notes

Open the app, check the game-log paths, select `조회` (Refresh), then use `차단` (Block) or `해제` (Unblock) for a server. Windows requests administrator permission only when firewall rules are changed.

- If you are assigned to a blocked server, loading may stall or the connection may fail. If a block prevents you from joining an EFT raid, leave the failed connection through the game's confirmation dialog and match again.
- Blocks apply **only to this PC and remain active after the app closes or Windows restarts**. They are not automatically shared with party members.
- It has not been confirmed whether blocking an Arena server results in a penalty for leaving the match. Read the separate warning before blocking.
- Current ping is measured now; in-game RTT and packet loss come from saved logs. A blocked server's current ping cannot be measured, and location data is an estimate.

<br>

## Privacy and license

TSG analyzes game logs on your PC and does not send raw logs, account data, session IDs or local paths to external services. Notes and settings stay on your PC. Ping checks, location-database downloads and GitHub update checks use the network.

Free for personal, non-commercial use. The source is available for inspection, but this is not open-source software. Redistribution, distribution of modified versions, sale and commercial use require permission.

[English guide](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/README.en.md) · [Troubleshooting — Korean](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/TROUBLESHOOTING.md) · [Privacy — Korean](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PRIVACY.md) · [Full license — Korean and English](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSE) · [Third-party notices](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/THIRD_PARTY_NOTICES.md)

This is not an official tool from Battlestate Games or Escape from Tarkov.

</details>

<br>

<p align="center">
유저가 선택한 모든 서버..<br> ex)미국서부, 중국서버 내에서도 여러 데이터센터와 서버IP가 존재하며 품질은 제각각이다.<br> 이 중에서 하이핑, 디싱크를 유발하는 저품질 서버 IP가 존재한다.
</p>

<br><br><br><br><br><br>

### 타르코프 서버 가드

레이드에 들어간 뒤 핑이 튀어서 강제로 쫓겨나<br> 시간과 장비를 잃어버리는 상황을 없애려고 만들기 시작한 무료 Windows 프로그램입니다.

<br>

타르코프와 아레나의 공식 로그를 자동으로 읽어<br> 지금까지 접속했던 서버의 품질과 레이드 기록을 한 화면에서 확인하고 관리할 수 있습니다.

<br>

<br>

## 핵심 기능

- 현재 핑·실게임 핑(RTT)·패킷 손실을 비교해 품질이 좋지 않은 서버만 골라 차단하여<br> 핑킥 위험을 줄이고, 그로 인한 장비 손실을 막아줍니다.

  (EFT에서 차단한 서버에 배정되면 해당 서버로의 접속을 막아주며,<br> 바로 `나가기 확인`을 눌러도 장비가 보존됩니다.)

  <br>

- EFT·아레나 과거 레이드의 맵·게임 유형·서버 배정·연결 결과·신고 기록을<br> 기간별로 확인할 수 있습니다.

  <br>

- 레이드 메모·신고 유저 메모·태그·스크린샷(경로)를 저장하고,<br> 로그 삭제 후에도 메모 보관함에서 다시 확인할 수 있습니다.

  (신고 기록은 조회할 수 있지만 닉네임은 조회할 수 없습니다.<br> 신고 메모는 `유저신고` 텍스트를, 레이드 메모는 노란색 메모 말풍선을 클릭해 작성합니다.)

<br>

## 다운로드

- **[설치형 다운로드 — 권장](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [포터블 ZIP 다운로드 — 설치 없이 사용](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10·11 / .NET Framework 4.8이 필요합니다. 포터블은 압축을 풀고 폴더 안의 파일 구성을 유지해 주세요. 두 방식 모두 사용자 확인 후 업데이트할 수 있습니다.

현재 실행 파일에는 코드 서명이 없어 Windows에서 ‘알 수 없는 게시자’ 안내가 표시될 수 있습니다. 위 공식 링크에서 받고, [SHA-256 확인 파일](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.3/SHA256SUMS.txt)로 검증할 수 있습니다.

<br>

## 이번 업데이트

- EFT 레이드 기록에 로그로 확인된 캐릭터·파티 인원·시즌 정보를 표시합니다.
- 서버 차단 후 해당 서버의 최근 이용 횟수와 연결 문제 징후를 안내합니다.

<br>

## 주요 기능

- **레이드 기록 확인·관리:** 맵·게임 유형·연결·신고 기록을 확인하고, 메모·태그·스크린샷 경로를 함께 관리합니다.

- **기록 검색:** 기간·게임·데이터센터 지역으로 필터링해 최근 기록을 최대 100개까지 확인합니다.

- **서버 상태 비교:** 현재 핑, 게임 중 지연(RTT)·패킷 손실, 데이터센터와 추정 지역을 확인합니다.

- **차단 목록 관리:** 차단 사유 메모, 개별·선택·전체 해제, 목록 내보내기·불러오기를 지원합니다. 로그를 지운 뒤에도 관리할 수 있습니다.

- **메모 백업·복원:** 레이드·신고 메모를 함께 내보내고, 기존 메모를 덮어쓰지 않고 빠진 항목만 복원합니다. 스크린샷 원본은 백업에 포함되지 않습니다.

- **편의 기능:** 공식 런처·Steam 설치 경로 자동 탐색, 게임에서 선택한 서버 지역 표시, 사용자 확인 후 자동 업데이트를 지원합니다.

<br>

## 사용 안내·주의사항

실행 후 로그 경로를 확인하고 `조회`를 누른 뒤 원하는 서버를 `차단`·`해제`합니다. 방화벽 변경 시에만 관리자 권한을 요청합니다.

- 차단한 서버에 배정되면 로딩이 멈추거나 접속 실패가 표시될 수 있습니다. 차단으로 EFT 레이드에 입장하지 못했다면 게임에서 나간 뒤 다시 매칭하세요.
- 차단은 **이 PC에만 적용되며 앱 종료·재부팅 후에도 유지**됩니다. 파티원에게 자동으로 공유되지 않습니다.
- 아레나 차단 시 탈주 페널티 적용 여부는 확인되지 않았습니다. 차단 전 앱의 별도 경고를 확인하세요.
- 현재 핑은 지금 측정한 응답이고, 게임 중 RTT·패킷 손실은 로그에 남은 기록입니다. 차단 중에는 해당 서버의 현재 핑을 측정할 수 없으며, 지역 정보는 추정값입니다.

<br>

## 개인정보·라이선스

게임 로그는 PC에서 분석하며 로그 원문·계정 정보·SID·로컬 경로를 외부로 전송하지 않습니다. 메모와 설정은 PC에 저장됩니다. 현재 핑 측정, 지역 DB 다운로드, GitHub 업데이트 확인에는 네트워크를 사용합니다.

개인적·비상업적 사용은 무료입니다. 소스는 검토할 수 있도록 공개하지만 오픈소스는 아니며, 허가 없는 재배포·수정본 배포·판매·상업적 이용은 금지됩니다.

[자세한 사용법](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/README.md) · [문제 해결](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/TROUBLESHOOTING.md) · [개인정보](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PRIVACY.md) · [라이선스 전문](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSE) · [서드파티 고지](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/THIRD_PARTY_NOTICES.md)

이 프로그램은 Battlestate Games 또는 Escape from Tarkov의 공식 도구가 아닙니다.
