# Tarkov Server Guard

한국어 | [English](https://github.com/Spirit-Schema/tarkov-server-guard/blob/main/README.en.md)

<p align="center">
  <img src="assets/branding/tarkov-server-guard-tsg-icon-master.png" width="128" alt="Tarkov Server Guard 방패 로고">
</p>

**한참 기다려 들어간 레이드에서 핑이 치솟고, 결국 강제로 연결이 끊기는 순간.** 들인 시간도, 챙겨 간 장비도 허무하게 잃을 수 있습니다.

같은 서버 지역을 선택해도 실제 접속하는 서버 IP는 여럿이고, 연결 품질도 다를 수 있습니다.

**타르코프 서버 가드(TSG)는 핑킥으로 반복되는 시간·장비 손실을 줄이고 싶어 만든 무료 Windows 프로그램입니다.** EFT·아레나의 게임 로그로 지난 접속 기록을 살펴보고, 문제가 반복되는 서버를 골라 차단해 같은 서버로 다시 연결되는 것을 막을 수 있습니다.

현재 공개 버전: **v0.8.3**

## 핵심 기능

- **핑킥 위험 줄이기** — 핑·패킷 손실을 비교해 문제가 반복되는 서버를 차단하고, 같은 서버에 다시 접속하는 것을 막습니다.
- **레이드 기록 조회** — EFT·아레나의 지난 레이드와 연결 결과를 찾아봅니다.
- **메모 보관** — 레이드·신고 메모를 남기고, 게임 로그를 지운 뒤에도 확인합니다.

## 다운로드

- **[설치형 다운로드 — 권장](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Setup.exe)**
- [포터블 ZIP 다운로드 — 설치 없이 사용](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SpiritSchema.TarkovServerGuard-win-Portable.zip)

Windows 10·11 / .NET Framework 4.8이 필요합니다. 포터블은 압축을 풀고 폴더 안의 파일 구성을 유지해 주세요. 두 방식 모두 사용자 확인 후 업데이트할 수 있습니다.

현재 실행 파일에는 코드 서명이 없어 Windows에서 ‘알 수 없는 게시자’ 안내가 표시될 수 있습니다. 위 공식 링크에서 받고, [SHA-256 확인 파일](https://github.com/Spirit-Schema/tarkov-server-guard/releases/latest/download/SHA256SUMS.txt)로 검증할 수 있습니다.

## 주요 기능

- **서버 상태 비교:** 현재 핑, 게임 중 지연(RTT)·패킷 손실, 데이터센터와 추정 지역을 확인합니다.
- **차단 목록 관리:** 차단 사유 메모, 개별·선택·전체 해제, 목록 내보내기·불러오기를 지원합니다. 로그를 지운 뒤에도 관리할 수 있습니다.
- **레이드 상세 정보:** 맵·게임 유형·서버 배정 시간·연결 결과·신고 기록을 확인합니다. EFT는 로그로 확인된 캐릭터·파티 인원·시즌도 표시합니다.
- **기록 검색:** 기간·게임·데이터센터 지역으로 필터링해 최근 기록을 최대 100개까지 확인합니다.
- **메모와 스크린샷:** 레이드 메모·태그와 신고 대상·사유를 직접 기록합니다. 스크린샷은 원본 파일을 복사하지 않고 경로만 연결합니다.
- **메모 백업·복원:** 레이드·신고 메모를 함께 내보내고, 기존 메모를 덮어쓰지 않고 빠진 항목만 복원합니다. 스크린샷 원본은 백업에 포함되지 않습니다.
- **편의 기능:** 공식 런처·Steam 설치 경로 자동 탐색, 게임에서 선택한 서버 지역 표시, 사용자 확인 후 자동 업데이트를 지원합니다.

## 사용 방법

1. 설치하거나 압축을 푼 뒤 `TarkovServerGuard.exe`를 실행하고 게임 로그 경로를 확인합니다. 자동으로 찾지 못하면 `직접선택` 후 `적용`을 누릅니다.
2. 원하는 기간·게임·지역을 선택하고 `조회`를 눌러 기록과 서버 품질을 확인합니다.
3. 필요한 서버의 `차단`·`해제`를 선택합니다. 방화벽을 변경할 때만 Windows 관리자 권한을 요청합니다.

자세한 조작은 앱의 `사용방법`에서, 문제가 생기면 [문제 해결](TROUBLESHOOTING.md)에서 확인하세요.

## 알아둘 점

- 차단한 서버에 배정되면 로딩이 멈추거나 접속 실패가 표시될 수 있습니다. 차단으로 EFT 레이드에 입장하지 못했다면 게임에서 나간 뒤 다시 매칭하세요.
- 차단은 **이 PC에만 적용되며 앱 종료·재부팅 후에도 유지**됩니다. 파티원에게 자동으로 공유되지 않습니다.
- 아레나 차단 시 탈주 페널티 적용 여부는 확인되지 않았습니다. 차단 전 앱의 별도 경고를 확인하세요.
- 현재 핑은 지금 측정한 응답이고, 게임 중 RTT·패킷 손실은 로그에 남은 기록입니다. 차단 중에는 해당 서버의 현재 핑을 측정할 수 없으며, 지역 정보는 추정값입니다.

## 개인정보·라이선스

게임 로그는 PC에서 분석하며 로그 원문·계정 정보·SID·로컬 경로를 외부로 전송하지 않습니다. 메모와 설정은 PC에 저장됩니다. 현재 핑 측정, 지역 DB 다운로드, GitHub 업데이트 확인에는 네트워크를 사용합니다.

개인적·비상업적 사용은 무료입니다. 소스는 검토할 수 있도록 공개하지만 오픈소스는 아니며, 허가 없는 재배포·수정본 배포·판매·상업적 이용은 금지됩니다.

자세한 내용: [개인정보 및 네트워크](PRIVACY.md) · [라이선스 안내](LICENSING.md) · [라이선스 전문 — 한국어·English](LICENSE) · [서드파티 고지](THIRD_PARTY_NOTICES.md)

이 프로그램은 Battlestate Games 또는 Escape from Tarkov의 공식 도구가 아닙니다.

<details>
<summary>소스 검토·개발 문서</summary>

- [소스 공개 및 제외 범위](PUBLICATION_SCOPE.md)
- [기여 안내](CONTRIBUTING.md)
- [개발·빌드 안내](DEVELOPMENT.md)

</details>

Developer · Spirit-Schema
