# 개발·빌드 안내

## 개발 빌드와 테스트

Windows PowerShell에서 실행합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

.NET Framework 4.8에 포함된 C# 컴파일러로 `dist\TarkovServerGuard.exe`를 만들고 테스트를 함께 실행합니다. 이 단계에는 별도 SDK나 NuGet 패키지가 필요하지 않습니다.

테스트 범위에는 합성 EFT·Arena·런처 로그, EFT 캐릭터·솔로/파티·파티 인원 판정, 실게임 지표 상태와 최근 레이드 차단 근거, 차단현황 표시, Arena 지역 설정, 기간 경계, 방화벽 입력, 메모 저장·통합 백업·없는 항목만 복원, MMDB 파싱·월간 갱신·손상 복구와 GitHub 업데이트 판단이 포함됩니다.

### v0.8.3 판정·표시 기준

- 서버형 EFT 레이드의 PMC·스캐브는 같은 매칭 세대의 ProfileId 동일·정확한 `+1` 관계와 파티의 실제 캐릭터 Side를 제한적으로 사용합니다. 로그의 `PvpSeasonN` 번호는 클라이언트 버전 매핑보다 우선합니다. 파티 인원은 확인되면 `2인`~`5인`, 인원 미확인은 `파티`로 표시합니다. `/client/match/local/start`가 있는 로컬 PvE는 솔로만 확정하며, 현재 확인된 로컬 로그에는 역할 직접 근거가 없으므로 PMC·스캐브를 추측하지 않습니다.
- 차단 완료 근거는 기간·지역 화면 필터가 아니라 서버형·로컬형을 합친 최신 레이드 최대 100개를 먼저 정한 뒤 대상 IP를 셉니다. RTT 150ms 이상·패킷손실 5% 이상·시간초과가 같은 레이드에서 함께 나타나도 한 건으로 세며, 관찰된 징후일 뿐 서버가 문제를 유발했다는 인과 판정이 아닙니다. 이 보조 근거를 다시 읽지 못해도 성공한 방화벽 변경을 실패로 바꾸지 않습니다.
- 실게임 Statistics 행 자체가 없거나 해당 행에서 수신 표본이 0건으로 확인되면 RTT와 패킷손실을 모두 `로그없음`으로 표시합니다. 수신 표본이 1건 이상이면 별도의 최소 표본 기준 없이 유효한 로그 값을 표시합니다. 패킷손실은 로그의 `lose` 값을 사용하며, `sent`·`received`의 차이로 새로 계산하지 않습니다.
- Arena 로그는 `/client/match/join`과 `/client/match/group/current`가 솔로·파티에 공통으로 나타나고 그룹 응답 본문이나 직접적인 시작 이벤트가 남지 않아, 현재 보유 로그만으로 참가 형태와 사전 파티 인원을 확정하지 않습니다. 같은 클라이언트 버전의 확인된 솔로·사전 파티 표본에서 직접 근거가 발견될 때까지 Arena는 기존 맵·게임 모드만 표시합니다.
- 서버차단현황은 방화벽 규칙을 즉시 불러오며, 최근 실게임 RTT·패킷손실 열을 위해 창을 열기 전에 전체 로그를 다시 읽지 않습니다. 차단 규칙이 ICMP를 포함한 통신을 막는 동안 현재 핑은 측정하지 않으며, 측정을 위해 규칙을 자동 해제·재적용하지 않습니다.
- Windows 방화벽 차단은 PC별입니다. 파티원 모두의 접속을 막으려면 각 PC에서 같은 서버를 차단해야 하며, 앱은 차단 목록을 자동 공유·동기화하지 않습니다.
- 사용방법 2번은 `다음 화면에서 재진입 대신 나가기 확인 선택 (장비는 보존됩니다)`로 표시하며 별도의 부연 설명 줄은 두지 않습니다.

이 경계는 `RaidClassificationTests`, `RaidParticipantLogTests`, `RaidQualityEvidenceTests`, `CoreTests`, `BlockedServersUiTests`, `V080UiTests`와 `ReleaseNotesTests`에서 합성 식별값과 정확 문구로 회귀 검증합니다. 실제 사용자 ProfileId·파티원 식별값·닉네임·장비 원문은 fixture에 넣지 않습니다.

UI 데모는 다음 명령으로 실행할 수 있습니다.

```powershell
.\dist\TarkovServerGuard.exe --demo
```

개발용 단일 EXE 옆에 Velopack 런타임이 없으면 자동 업데이트만 안전하게 비활성화됩니다.

내장 변경 사항 창은 Velopack의 실제 `OnAfterUpdateFastCallback`이 완료 버전을 기록하고, 업데이트된 설치형 정식 빌드가 다음 첫 실행에서 이를 원자적으로 소비한 경우에만 한 번 나타납니다. 제품에는 상시 `패치내역` 버튼, 업데이트 전 `변경 사항` 버튼 또는 임의 미리보기 실행 인자가 없습니다. 소비 receipt 저장에 실패하면 반복 팝업을 피하기 위해 표시하지 않는 at-most-once 정책입니다. 로직과 대화상자 구조는 격리된 임시 저장소를 사용하는 `ReleaseNotesTests`로 검증합니다.

별도 사용자 선택형 설치 제거 UI·인자·Velopack install/uninstall hook은 현재 제품 빌드에서 제외했습니다. 시험 설계는 공개 저장소 밖의 로컬 보류 자료로만 보존합니다. Windows 제어판·`설치된 앱`의 Velopack 기본 제거 동작은 그대로 유지합니다.

### v0.8.3 검증 빌드

기능 구현 중에는 정식 배포 폴더와 분리된 출력 경로로 다음 명령을 사용합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\build\test-v0.8.3
```

이 명령은 `/warn:4` 앱과 전체 단위·저장소·UI 테스트를 실행하지만 Setup, Portable, Velopack 패키지, 업데이트 피드, 소스 ZIP과 배포 해시는 만들지 않습니다. 짧은 UI 반복에서는 `-SkipTests`로 컴파일만 확인할 수 있지만 최종 검증에서는 생략하지 않습니다. `package-release.ps1`은 기능 반복 중에는 실행하지 않고 최종 배포 검증에서만 실행합니다.

시험 구현했던 Build ID·provenance, 공개 자산 재다운로드 검증과 최종 배포물 전용 민감정보 검사는 현재 빌드·패키징·배포 절차에서 제외했습니다. 관련 시험 자료와 재도입 조건은 공개 저장소 밖의 로컬 보류 영역에만 보존합니다.

## v0.8.3 배포 패키지

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package-release.ps1 -Version 0.8.3
```

패키징에는 .NET 8 런타임이 필요합니다. 스크립트는 SHA-256으로 고정 검증한 Velopack 1.2.0과 Newtonsoft.Json 13.0.4를 빌드 캐시에 준비하고 런타임 연결 테스트 후 다음 결과를 생성합니다.

- `build\Releases-v0.8.3`: Setup, Portable, `.nupkg`, 업데이트 feed와 SHA-256 목록
- `review\TarkovServerGuard-v0.8.3`: 실행 검토용 복사본과 검증 파일

사용자 검토 폴더는 현재 실행 중인 검토본을 덮어쓰지 않도록 별도로 생성합니다.

게시 전에는 먼저 위의 분리된 `build\test-v0.8.3` 검증 빌드로 기능을 확인하고, 패키징 결과의 실행 파일·업데이트 feed·해시·검토본 ZIP을 모두 검수합니다.

GitHub Releases에는 Setup과 Portable만이 아니라 같은 실행에서 생성된 `releases.win.json`, `.nupkg` 등 전체 업데이트 자산을 함께 게시해야 자동 업데이트가 작동합니다. `SHA256SUMS.txt`는 게시 자산에서 다시 생성하지 말고 패키징 결과를 사용합니다.

현재 활성 패키징은 정식 게시된 `v0.7.4`와 같은 단순 절차를 유지합니다. 빌드와 단위 테스트를 통과한 뒤 Velopack 자산과 검토본을 만들고 각각의 `SHA256SUMS.txt`를 생성합니다. 게시 전에는 `build\Releases-v0.8.3`의 전체 자산과 `review\TarkovServerGuard-v0.8.3`를 직접 검수하고, GitHub에는 업데이트 feed와 `.nupkg`을 포함한 자산 전체를 그대로 올립니다.

자동 Build ID·provenance·release-readiness 완료 marker·전용 민감정보 검사·게시 후 재다운로드 검증은 활성 절차가 아닙니다. 시험 구현은 공개 저장소 밖의 로컬 보류 영역에 격리했으며 해당 스크립트를 현재 배포 명령에 섞어 사용하지 않습니다.

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
