# Tarkov Server Guard v0.7.1

Tarkov Server Guard의 첫 공개 버전입니다. EFT와 Arena의 최근 접속 서버와 연결 품질을 확인하고, 필요한 서버만 Windows 방화벽으로 차단하거나 해제할 수 있습니다.

## 다운로드

- **일반 사용자 권장:** `SpiritSchema.TarkovServerGuard-win-Setup.exe`
- **설치 없이 사용:** `SpiritSchema.TarkovServerGuard-win-Portable.zip`

Portable은 압축을 푼 뒤 폴더 안의 파일 구성을 유지해 주세요. 실행 파일에는 현재 코드 서명이 없어 Windows SmartScreen 안내가 표시될 수 있습니다.

## 주요 기능

- 공홈·Steam의 EFT·Arena 로그 경로 자동 탐색
- 최근 세션, 기간 검색, 게임 필터와 최대 100개 기록 표시
- 현재 핑·실게임 RTT·실게임 패킷손실·데이터센터 및 추정 지역 비교
- 서버별 차단·해제와 로그 독립형 서버차단현황
- PvP·PvP시즌·PvE 구분, 서버배정·연결 결과·재접속 정보 표시
- EFT 런처 및 Arena 인게임 선택 서버 표시
- 레이드·유저신고 메모 로컬 보관
- 사용자 확인 방식의 안정 버전 자동 업데이트

## 사용 방법

1. Setup을 설치하거나 Portable ZIP의 압축을 풉니다.
2. 앱을 실행하고 자동으로 찾은 EFT·Arena 로그 경로를 확인합니다.
3. 기간과 게임 필터를 선택한 뒤 `조회`를 누릅니다.
4. 서버별 핑·지역·차단 상태를 확인하고 `차단` 또는 `해제`를 선택합니다.
5. 방화벽 변경 시에만 표시되는 Windows 관리자 권한 요청을 확인합니다.

## 주의사항

- 차단한 서버가 매칭되면 로딩이 멈추거나 접속 오류가 표시될 수 있습니다. 게임에서 나간 뒤 다시 매칭하세요.
- Arena 서버 차단의 탈주 페널티 적용 여부는 확인되지 않았으며, 전용 경고창에서 동의해야 차단됩니다.
- 지역 정보는 DB-IP Lite 기반 추정값입니다. 조회 시 새 월간 DB가 있으면 약 60~70MB를 새 파일로 교체하며 매달 누적하지 않습니다.
- 사용자의 게임 로그·계정 정보·SID·로컬 경로는 전송하지 않습니다.

## 실행 환경

- Windows 10 또는 Windows 11
- .NET Framework 4.8
- 최초 지역 DB 준비와 자동 업데이트 확인을 위한 인터넷 연결

## 파일 확인

릴리스에 첨부된 `SHA256SUMS.txt`와 내려받은 파일의 SHA-256을 비교하세요.

```powershell
Get-FileHash -Algorithm SHA256 .\SpiritSchema.TarkovServerGuard-win-Setup.exe
```

자세한 사용법과 개인정보 안내는 [README](https://github.com/Spirit-Schema/tarkov-server-guard#readme)에서 확인할 수 있습니다.
