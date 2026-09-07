# Tarkov Server Guard v0.8.6

<p align="center">
  <img src="https://raw.githubusercontent.com/Spirit-Schema/tarkov-server-guard/v0.8.6/assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard logo">
</p>

<details>
<summary><strong>English — Overview, download, release notes and features</strong></summary>

<br>

<p align="center">
All the servers you've selected...<br> For example, even within US West or China, there are multiple data centers and server IPs, and quality varies from server to server.<br> Some of these are low-quality server IPs that cause high ping and desync.
</p>

<br><br><br><br><br><br>

### Tarkov Server Guard

I started building this free Windows app to stop those situations where your ping spikes after you enter a raid, you get kicked, and you lose your time and gear.

It automatically reads the official Tarkov and Arena logs, so you can view and manage the quality of servers you've connected to and your raid history on one screen.

Current public release: **v0.8.6**. Choose Korean or English in Settings.

<br>

## At a glance

- Compare current ping, in-game ping (RTT) and packet loss to block only poor-quality servers, reduce the risk of high-ping kicks and prevent the resulting gear loss.<br> (In EFT, if you are assigned to a blocked server, TSG prevents the connection. You can select `나가기 확인` (Confirm exit) right away and keep your gear.)

- Review maps, game modes, server allocation, connection results and report activity from past EFT and Arena raids by date range.

- Save raid notes, player-report notes, tags and screenshot paths, and find them again in the note archive even after the game logs are deleted.<br> (Report activity is available, but reported players' nicknames cannot be retrieved. Click the `유저신고` text to write a player-report note, or the yellow note speech bubble to write a raid note.)

<br>

## Download

- **[Download installer — recommended](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [Download Portable ZIP — no installation](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10/11 · .NET Framework 4.8. For Portable, extract the ZIP and keep its files together. Both editions support updates after your confirmation.

The executable is unsigned, so Windows may show an unknown-publisher warning. Use the official downloads above; [SHA-256 checksums](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SHA256SUMS.txt) are available.

<br>

## What's new

**0.8.6 hotfix**

- Added a step-by-step usage guide to Add Party IPs.

**Also included: the 0.8.5 update**

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

The in-app guide is available through `Usage Guide`. See [Troubleshooting — Korean](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/TROUBLESHOOTING.md) for additional help.

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

Details: [Privacy and network — Korean](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PRIVACY.md) · [License overview — Korean](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSING.md) · [Full license — Korean and English](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSE) · [Third-party notices](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/THIRD_PARTY_NOTICES.md)

This is not an official tool from Battlestate Games or Escape from Tarkov.

<details>
<summary>Source review and development documents — Korean</summary>

- [Publication scope](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PUBLICATION_SCOPE.md)
- [Contributing](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/CONTRIBUTING.md)
- [Development and build instructions](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/DEVELOPMENT.md)

</details>

Developer · Spirit-Schema


</details>

<br>

<p align="center">
유저가 선택한 모든 서버..<br> ex)미국서부, 중국서버 내에서도 여러 데이터센터와 서버IP가 존재하며 품질은 제각각이다.<br> 이 중에서 하이핑, 디싱크를 유발하는 저품질 서버 IP가 존재한다.
</p>

<br><br><br><br><br><br>

### 타르코프 서버 가드

레이드에 들어간 뒤 핑이 튀어서 강제로 쫓겨나 시간과 장비를 잃어버리는 상황을 없애려고 만들기 시작한 무료 Windows 프로그램입니다.

타르코프와 아레나의 공식 로그를 자동으로 읽어 지금까지 접속했던 서버의 품질과 레이드 기록을 한 화면에서 확인하고 관리할 수 있습니다.

현재 공개 버전: **v0.8.6**

<br>

## 핵심 기능

- 현재 핑·실게임 핑(RTT)·패킷 손실을 비교해 품질이 좋지 않은 서버만 골라 차단하여 핑킥 위험을 줄이고, 그로 인한 장비 손실을 막아줍니다.<br> (EFT에서 차단한 서버에 배정되면 해당 서버로의 접속을 막아주며, 바로 `나가기 확인`을 눌러도 장비가 보존됩니다.)

- EFT·아레나 과거 레이드의 맵·게임 유형·서버 배정·연결 결과·신고 기록을 기간별로 확인할 수 있습니다.

- 레이드 메모·신고 유저 메모·태그·스크린샷(경로)를 저장하고, 로그 삭제 후에도 메모 보관함에서 다시 확인할 수 있습니다.<br> (신고 기록은 조회할 수 있지만 닉네임은 조회할 수 없습니다. 신고 메모는 `유저신고` 텍스트를, 레이드 메모는 노란색 메모 말풍선을 클릭해 작성합니다.)

<br>

## 다운로드

- **[설치형 다운로드 — 권장](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [포터블 ZIP 다운로드 — 설치 없이 사용](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10·11 / .NET Framework 4.8이 필요합니다. 포터블은 압축을 풀고 폴더 안의 파일 구성을 유지해 주세요. 두 방식 모두 사용자 확인 후 업데이트할 수 있습니다.

현재 실행 파일에는 코드 서명이 없어 Windows에서 ‘알 수 없는 게시자’ 안내가 표시될 수 있습니다. 위 공식 링크에서 받고, [SHA-256 확인 파일](https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.6/SHA256SUMS.txt)로 검증할 수 있습니다.

<br>

## 이번 업데이트 내역

**0.8.6 핫픽스**

- 파티 IP 추가 창에 단계별 사용방법 안내를 추가했습니다.

**함께 포함된 0.8.5 업데이트**

0.8.3 이후의 변경 사항을 한 번에 담았습니다.

- 창 크기와 직접 조절한 열 너비를 기억해 다음 실행에서도 이어서 사용합니다.
- 설정에서 한국어·영어를 선택할 수 있습니다.
- 메모 보관함에 검색과 종류 필터를 추가했습니다. 본문·태그·맵·닉네임·신고 사유로 메모를 찾을 수 있습니다.
- 파티원이 공유한 서버 IP를 목록별로 추가하고 한 번에 해제할 수 있습니다. 기존 개인 차단과 다른 파티 목록의 차단은 유지됩니다.
- UI 조작 편의성을 개선했습니다.
- 로그 조회 속도를 높이고, 큰 로그를 읽을 때의 메모리 사용량을 줄였습니다.
- 메모 저장·백업·복원의 안정성을 높였습니다.

<br>

## 주요 기능

- **레이드 기록 확인·관리:** 맵·게임 유형·연결·신고 기록을 확인하고, 메모·태그·스크린샷 경로를 함께 관리합니다.

- **기록 검색:** 기간·게임·데이터센터 지역으로 필터링해 최근 기록을 최대 100개까지 확인합니다.

- **서버 상태 비교:** 현재 핑, 게임 중 지연(RTT)·패킷 손실, 데이터센터와 추정 지역을 확인합니다.

- **차단 목록 관리:** 차단 사유 메모, 개별·선택·전체 해제, 목록 내보내기·불러오기를 지원합니다. 로그를 지운 뒤에도 관리할 수 있습니다.

- **메모 백업·복원:** 레이드·신고 메모를 함께 내보내고, 기존 메모를 덮어쓰지 않고 빠진 항목만 복원합니다. 스크린샷 원본은 백업에 포함되지 않습니다.

- **편의 기능:** 공식 런처·Steam 설치 경로 자동 탐색, 게임에서 선택한 서버 지역 표시, 사용자 확인 후 자동 업데이트를 지원합니다.

<br>

## 사용 방법

1. 설치하거나 압축을 푼 뒤 `TarkovServerGuard.exe`를 실행하고 게임 로그 경로를 확인합니다. 자동으로 찾지 못하면 `직접선택` 후 `적용`을 누릅니다.
2. 원하는 기간·게임·지역을 선택하고 `조회`를 눌러 기록과 서버 품질을 확인합니다.
3. 필요한 서버의 `차단`·`해제`를 선택합니다. 방화벽을 변경할 때만 Windows 관리자 권한을 요청합니다.

자세한 조작은 앱의 `사용방법`에서, 문제가 생기면 [문제 해결](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/TROUBLESHOOTING.md)에서 확인하세요.

<br>

<details>
<summary><strong>파티 차단 사용법 — 처음부터 따라 하기</strong></summary>

파티원이 보내준 **게임 서버 IP**를 내 PC에서도 차단하고, 나중에 그 목록만 한 번에 해제하는 기능입니다.

**① 파티 IP 추가하기**

1. TSG에서 `서버차단현황` → `파티 IP 추가`를 누릅니다.
2. `출처명`에 나중에 알아볼 이름을 적습니다. 예: `오늘 저녁 파티`.
3. `사유`에서 IP를 전달한 사람에 맞게 `파티장 공유` 또는 `파티원 공유`를 고릅니다.
4. 전달받은 서버 IP를 입력칸에 붙여 넣습니다. 여러 개라면 한 줄에 하나씩 넣으면 됩니다.
5. `IP 미리보기`를 눌러 차단할 IP를 확인합니다. 잘못된 주소는 제외되므로 필요하면 고칩니다.
6. `한 번에 적용`을 누르고 Windows 관리자 권한 요청에서 `예`를 누릅니다.

**② 필요 없어졌을 때 해제하기**

1. `서버차단현황` → `파티 차단 해제`를 누릅니다.
2. 앞에서 적은 목록 이름(예: `오늘 저녁 파티`)을 선택합니다.
3. 해제할 IP와 유지할 IP를 확인한 뒤 `일괄 해제`를 누릅니다. 관리자 권한 요청이 뜨면 `예`를 누릅니다.

**원래 개인적으로 차단했던 IP와 다른 파티 목록에서 사용 중인 IP는 그대로 유지됩니다.**

- 차단은 **내 PC에만** 적용됩니다. 파티원에게 자동으로 전달되지 않으므로, 각자 같은 IP를 추가해야 합니다.
- 앱을 끄거나 재부팅해도 차단은 유지됩니다. 필요 없어지면 위 순서로 직접 해제하세요.
- 파티원의 집 IP가 아니라, 차단하려는 **게임 서버 IP**를 입력하세요.

</details>

<br>

## 알아둘 점

- 차단한 서버에 배정되면 로딩이 멈추거나 접속 실패가 표시될 수 있습니다. 차단으로 EFT 레이드에 입장하지 못했다면 게임에서 나간 뒤 다시 매칭하세요.
- 차단은 **이 PC에만 적용되며 앱 종료·재부팅 후에도 유지**됩니다. 파티원에게 자동으로 공유되지 않습니다.
- 아레나 차단 시 탈주 페널티 적용 여부는 확인되지 않았습니다. 차단 전 앱의 별도 경고를 확인하세요.
- 현재 핑은 지금 측정한 응답이고, 게임 중 RTT·패킷 손실은 로그에 남은 기록입니다. 차단 중에는 해당 서버의 현재 핑을 측정할 수 없으며, 지역 정보는 추정값입니다.

<br>

## 개인정보·라이선스

게임 로그는 PC에서 분석하며 로그 원문·계정 정보·SID·로컬 경로를 외부로 전송하지 않습니다. 메모와 설정은 PC에 저장됩니다. 현재 핑 측정, 지역 DB 다운로드, GitHub 업데이트 확인에는 네트워크를 사용합니다.

> **무료 사용 범위:** 현재 배포 버전의 모든 기능은 개인적·비상업적 용도로 무료로 사용할 수 있습니다. 향후 추가되는 기능이나 별도 서비스는 유료로 제공될 수 있습니다.

소스는 검토할 수 있도록 공개하지만 오픈소스는 아니며, 허가 없는 재배포·수정본 배포·판매·상업적 이용은 금지됩니다.

자세한 내용: [개인정보 및 네트워크](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PRIVACY.md) · [라이선스 안내](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSING.md) · [라이선스 전문 — 한국어·English](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/LICENSE) · [서드파티 고지](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/THIRD_PARTY_NOTICES.md)

이 프로그램은 Battlestate Games 또는 Escape from Tarkov의 공식 도구가 아닙니다.

<details>
<summary>소스 검토·개발 문서</summary>

- [소스 공개 및 제외 범위](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/PUBLICATION_SCOPE.md)
- [기여 안내](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/CONTRIBUTING.md)
- [개발·빌드 안내](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/DEVELOPMENT.md)

</details>

Developer · Spirit-Schema
