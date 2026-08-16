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

내장 변경 사항 창은 Velopack의 실제 `OnAfterUpdateFastCallback`이 완료 버전을 기록하고, 업데이트된 설치형 정식 빌드가 다음 첫 실행에서 이를 원자적으로 소비한 경우에만 한 번 나타납니다. 제품에는 상시 `패치내역` 버튼, 업데이트 전 `변경 사항` 버튼 또는 임의 미리보기 실행 인자가 없습니다. 소비 receipt 저장에 실패하면 반복 팝업을 피하기 위해 표시하지 않는 at-most-once 정책입니다. 로직과 대화상자 구조는 격리된 임시 저장소를 사용하는 `ReleaseNotesTests`로 검증합니다.

별도 사용자 선택형 설치 제거 UI·인자·Velopack install/uninstall hook은 현재 제품 빌드에서 제외했습니다. 보존한 설계와 재도입 조건은 소스 저장소의 `deferred/uninstall/README.md`를 참고하세요. Windows 제어판·`설치된 앱`의 Velopack 기본 제거 동작은 그대로 유지합니다.

### v0.8.0 검증 빌드

기능 구현 중에는 정식 배포 폴더와 분리된 출력 경로로 다음 명령을 사용합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\build\test-v0.8.0
```

이 명령은 `/warn:4` 앱과 전체 단위·저장소·UI 테스트를 실행하지만 Setup, Portable, Velopack 패키지, 업데이트 피드, 소스 ZIP과 배포 해시는 만들지 않습니다. 짧은 UI 반복에서는 `-SkipTests`로 컴파일만 확인할 수 있지만 최종 검증에서는 생략하지 않습니다. `package-release.ps1`은 기능 반복 중에는 실행하지 않고 최종 배포 검증에서만 실행합니다.

시험 구현했던 Build ID·provenance, 공개 자산 재다운로드 검증과 최종 배포물 전용 민감정보 검사는 현재 빌드·패키징·배포 절차에서 제외했습니다. 소스·테스트·한계와 재도입 조건은 소스 저장소의 `deferred/release-integrity/README.md`에 보존합니다.

## v0.8.0 배포 패키지

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package-release.ps1 -Version 0.8.0
```

패키징에는 .NET 8 런타임이 필요합니다. 스크립트는 SHA-256으로 고정 검증한 Velopack 1.2.0과 Newtonsoft.Json 13.0.4를 빌드 캐시에 준비하고 런타임 연결 테스트 후 다음 결과를 생성합니다.

- `build\Releases-v0.8.0`: Setup, Portable, `.nupkg`, 업데이트 feed와 SHA-256 목록
- `review\TarkovServerGuard-v0.8.0`: 실행 검토용 복사본과 검증 파일

사용자 검토 폴더는 현재 실행 중인 검토본을 덮어쓰지 않도록 별도로 생성합니다.

게시 전에는 먼저 위의 분리된 `build\test-v0.8.0` 검증 빌드로 기능을 확인하고, 패키징 결과의 실행 파일·업데이트 feed·해시·검토본 ZIP을 모두 검수합니다.

GitHub Releases에는 Setup과 Portable만이 아니라 같은 실행에서 생성된 `releases.win.json`, `.nupkg` 등 전체 업데이트 자산을 함께 게시해야 자동 업데이트가 작동합니다. `SHA256SUMS.txt`는 게시 자산에서 다시 생성하지 말고 패키징 결과를 사용합니다.

현재 활성 패키징은 정식 게시된 `v0.7.4`와 같은 단순 절차를 유지합니다. 빌드와 단위 테스트를 통과한 뒤 Velopack 자산과 검토본을 만들고 각각의 `SHA256SUMS.txt`를 생성합니다. 게시 전에는 `build\Releases-v0.8.0`의 전체 자산과 `review\TarkovServerGuard-v0.8.0`을 직접 검수하고, GitHub에는 업데이트 feed와 `.nupkg`을 포함한 자산 전체를 그대로 올립니다.

자동 Build ID·provenance·release-readiness 완료 marker·전용 민감정보 검사·게시 후 재다운로드 검증은 활성 절차가 아닙니다. 시험 구현은 소스 저장소의 `deferred/release-integrity/README.md`에 격리했으며 해당 폴더의 스크립트를 현재 배포 명령에 섞어 사용하지 않습니다.

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
