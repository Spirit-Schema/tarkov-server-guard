# Tarkov Server Guard

<p align="center">
  <img src="assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard 방패 로고">
</p>

Escape from Tarkov와 Escape from Tarkov: Arena의 최근 접속 서버를 한눈에 확인하고, 연결 품질이 좋지 않은 서버를 Windows 방화벽으로 선택 차단·해제하는 도구입니다.

현재 배포 버전은 `v0.7.3`입니다.

## 핵심 기능

- 공홈·Steam 설치 경로를 자동으로 찾아 EFT와 Arena의 최근 로그 세션을 최대 100개까지 표시
- 현재 핑, 실게임 RTT, 실게임 패킷손실, 데이터센터·추정 지역을 한 화면에서 비교
- `최근100개`, `오늘`, `7일`, `30일`, 직접 지정 기간과 EFT·Arena 필터 지원
- 서버별 차단·해제·차단 메모 및 로그가 없어도 사용할 수 있는 `서버차단현황` 제공
- PvP, PvP시즌, PvE(서버), PvE(로컬)과 서버배정·연결 결과·재접속 횟수 표시
- EFT 런처와 Arena 인게임 설정에서 확인한 선택 서버 표시
- 레이드 메모·유저신고 메모를 PC에 보관하고 새 안정 버전은 확인 후 자동 업데이트

## 다운로드

[최신 릴리스](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest)에서 사용 방식에 맞는 파일을 받으세요.

모든 기능을 개인적·비상업적 목적으로 무료로 사용할 수 있는 프리웨어입니다. 공식 배포처는 Spirit-Schema GitHub Releases이며, 비공식 배포본은 안전성과 정상 작동을 보증하거나 지원하지 않습니다.

| 파일 | 용도 |
| --- | --- |
| `SpiritSchema.TarkovServerGuard-win-Setup.exe` | 일반 사용자 권장. 설치·시작 메뉴·자동 업데이트 지원 |
| `SpiritSchema.TarkovServerGuard-win-Portable.zip` | 설치 없이 사용하고 자동 업데이트 지원. 압축을 푼 뒤 폴더 구성을 유지해야 함 |

현재 실행 파일에는 코드 서명이 없어 Windows SmartScreen에서 `알 수 없는 게시자` 안내가 표시될 수 있습니다. 공식 Releases의 `SHA256SUMS.txt`로 파일을 확인할 수 있습니다.

## 빠른 시작

1. Setup을 설치하거나 Portable ZIP의 압축을 푼 뒤 `TarkovServerGuard.exe`를 실행합니다.
2. 자동으로 찾은 EFT·Arena 로그 경로를 확인합니다. 찾지 못했다면 `자동 찾기` 또는 `직접선택`을 사용합니다.
3. 기간과 게임 필터를 선택한 뒤 `조회`를 누릅니다. 새로 생성되거나 변경된 최신 로그를 먼저 다시 읽고 핑·지역·차단 상태를 이어서 갱신합니다.
4. 현재 핑·지역·차단 상태를 비교하고 원하는 서버의 `차단` 또는 `해제`를 누릅니다.
5. 방화벽을 변경할 때만 표시되는 Windows 관리자 권한 요청을 확인합니다.

`현재 핑`은 지금 측정한 ICMP 응답이고, `실게임 RTT`와 `실게임 패킷손실`은 게임이 실제 연결 중 로그에 기록한 값입니다. 핑은 100ms 미만 초록색, 100~149ms 노란색, 150ms 이상 빨간색으로 표시됩니다.

## 꼭 알아두세요

- 차단한 서버가 매칭되면 로딩이 멈추거나 접속실패가 표시될 수 있습니다.
   이는 차단서버 연결을 막은 정상 작동이며, 게임에서 나간 뒤 다시 매칭하면 됩니다.
- Arena 서버 차단 시 탈주 페널티 적용 여부는 확인되지 않았습니다. 전용 확인창에 동의한 경우에만 차단됩니다.
- 지역은 DB-IP Lite 기반 추정값이므로 실제 데이터센터 위치와 다를 수 있습니다.
- ICMP 무응답만으로 차단 상태를 판단하지 않습니다. 앱이 관리하는 Windows 방화벽 규칙을 직접 확인합니다.

## 개인정보와 네트워크

- 사용자의 게임 로그·계정 정보·SID·로컬 경로는 전송하지 않습니다.
- 지역 조회는 외부 IP 조회 API가 아닌 PC의 DB-IP Lite 데이터로 처리합니다.
- 조회 시 새 월간 지역 DB가 있으면 자동으로 업데이트합니다. 약 60~70MB를 새 파일로 교체하는 방식이며 매달 저장 용량이 누적되지 않습니다.
- 메모, 차단 서버 정보와 설정은 `%LOCALAPPDATA%\TarkovServerGuard`에 로컬로 저장됩니다.
- 자동 업데이트 확인은 공식 GitHub Releases만 사용하며, 새 버전이 있을 때 사용자 확인 후 적용합니다.

자세한 전송 범위와 저장 항목은 [개인정보 및 네트워크](PRIVACY.md)에서 확인할 수 있습니다.

## 실행 환경

- Windows 10 또는 Windows 11
- .NET Framework 4.8
- 최초 지역 DB 준비와 자동 업데이트 확인을 위한 인터넷 연결
- 지역 DB 갱신 중 임시 파일을 포함해 최대 약 500MB의 여유 공간 권장

## 라이선스

소스는 사용자가 안전성과 투명성을 확인할 수 있도록 공개합니다. Tarkov Server Guard는 OSI 오픈소스가 아닌 **소스 공개형(Source-Available) 독점 프리웨어**이며, 무단 수정본 배포·재배포·판매·상업적 이용을 금지합니다.

정확한 사용 조건은 [Tarkov Server Guard Source-Available Freeware License 1.0](LICENSE)을 확인하세요. 제3자 구성요소는 각각의 기존 라이선스를 따르며 [서드파티 고지](THIRD_PARTY_NOTICES.md)에 별도로 정리되어 있습니다.

## 자세한 문서

- [문제 해결](TROUBLESHOOTING.md)
- [개인정보 및 네트워크](PRIVACY.md)
- [라이선스 안내](LICENSING.md)
- [소스 공개 및 제외 범위](PUBLICATION_SCOPE.md)
- [기여 안내](CONTRIBUTING.md)
- [개발·빌드 안내](DEVELOPMENT.md)
- [서드파티 고지](THIRD_PARTY_NOTICES.md)
- [라이선스 전문](LICENSE)

이 프로그램은 Battlestate Games 또는 Escape from Tarkov의 공식 도구가 아닙니다.

Developer · Spirit-Schema
