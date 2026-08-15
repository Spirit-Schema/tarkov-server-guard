# 개발·빌드 안내

## 개발 빌드와 테스트

Windows PowerShell에서 실행합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

.NET Framework 4.8에 포함된 C# 컴파일러로 `dist\TarkovServerGuard.exe`를 만들고 테스트를 함께 실행합니다. 이 단계에는 별도 SDK나 NuGet 패키지가 필요하지 않습니다.

테스트 범위에는 합성 EFT·Arena·런처 로그, Arena 지역 설정, 기간 경계, 방화벽 입력, 메모 저장, MMDB 파싱·월간 갱신·손상 복구와 GitHub 업데이트 판단이 포함됩니다.

UI 데모는 다음 명령으로 실행할 수 있습니다.

```powershell
.\dist\TarkovServerGuard.exe --demo
```

개발용 단일 EXE 옆에 Velopack 런타임이 없으면 자동 업데이트만 안전하게 비활성화됩니다.

## v0.7.4 배포 패키지

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package-release.ps1 -Version 0.7.4
```

패키징에는 .NET 8 런타임이 필요합니다. 스크립트는 SHA-256으로 고정 검증한 Velopack 1.2.0과 Newtonsoft.Json 13.0.4를 빌드 캐시에 준비하고 런타임 연결 테스트 후 다음 결과를 생성합니다.

- `build\Releases-v0.7.4`: Setup, Portable, `.nupkg`, 업데이트 feed, SHA-256 목록
- `review\TarkovServerGuard-v0.7.4`: 실행 검토용 복사본과 검증 파일

사용자 검토 폴더는 현재 실행 중인 검토본을 덮어쓰지 않도록 별도로 생성합니다.

GitHub Releases에는 Setup과 Portable만이 아니라 같은 실행에서 생성된 `releases.win.json`, `.nupkg` 등 전체 업데이트 자산을 함께 게시해야 자동 업데이트가 작동합니다. `SHA256SUMS.txt`는 게시 자산에서 다시 생성하지 말고 패키징 결과를 사용합니다.

검토 폴더 최상위의 `TarkovServerGuard.exe`는 기능을 빠르게 확인하는 개발용 실행 파일입니다. 실제 자동 교체 흐름은 `Velopack-Release` 안의 Setup 또는 Portable로 검증합니다.

## 주요 로컬 데이터

- `%LOCALAPPDATA%\TarkovServerGuard\RaidNotes`: 레이드 메모
- `%LOCALAPPDATA%\TarkovServerGuard\UserReportMemos`: 유저신고 메모
- `%LOCALAPPDATA%\TarkovServerGuard\DbIpLite`: 활성·직전 정상 지역 DB
- `%LOCALAPPDATA%\TarkovServerGuard\blocked-server-metadata.json`: 차단 서버 부가 정보와 선택 입력한 차단 메모
- `%LOCALAPPDATA%\TarkovServerGuard\usage-guide.shown`: 첫 실행 안내 상태

개인정보와 외부 통신의 상세 범위는 [PRIVACY.md](PRIVACY.md)를 유지 기준으로 사용하세요.

## 참고 구현과 라이선스

- 서버 IP 로그 탐색 참고: [karpitony/eft-where-am-i](https://github.com/karpitony/eft-where-am-i)
- 추정 지역 데이터: [DB-IP Lite City](https://db-ip.com/db/download/ip-to-city-lite) · [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
- 설치·자동 업데이트: [Velopack](https://github.com/velopack/velopack) · MIT

프로젝트 자체 소스와 자산은 [Tarkov Server Guard Source-Available Freeware License 1.0](LICENSE)을 따릅니다. 이는 OSI 오픈소스 라이선스가 아닙니다. 적용 범위는 [라이선스 안내](LICENSING.md), 전체 외부 구성요소 고지는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)를 참고하세요.

외부 코드 기여는 사전 협의 후 검토합니다. 새 의존성을 추가할 때에는 저작권·재배포 조건을 먼저 확인하며, GPL·AGPL 계열처럼 프로젝트의 배포 조건과 충돌할 수 있는 의존성은 포함하지 않습니다.
