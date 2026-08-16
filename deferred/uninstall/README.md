# 보류된 사용자 선택형 설치 제거 설계

이 폴더는 `v0.8.0` 테스트 버전에 포함하지 않기로 한 사용자 선택형 설치 제거 UI와 지원 코드를 나중에 재검토할 수 있도록 보존합니다. Windows 제어판·`설치된 앱`에서 실행하는 Velopack 기본 제거 기능은 이 보류 코드와 무관하며 계속 패키징 도구가 제공합니다.

## 제품에서의 격리 상태

- `src/`, `tests/` 및 `build.ps1`의 현재 컴파일 입력에 이 폴더의 C# 파일을 넣지 않습니다.
- 메인 화면에 `설치제거` 버튼을 제공하지 않고 `--uninstall`, `--uninstall-preview` 인자도 처리하지 않습니다.
- Velopack 시작 처리에는 이 설계의 `OnAfterInstallFastCallback`·`OnBeforeUninstallFastCallback`을 연결하지 않습니다.
- 공개 EXE·Setup·Portable·nupkg의 바이너리 입력에 포함하지 않습니다.
- `deferred/`는 재사용 검토용 문서·소스 영역입니다. 공개 소스 검토본에 포함할지는 별도로 결정하되, 포함하더라도 실행·패키징 입력으로 취급하면 안 됩니다.

## 보존 자료와 원래 위치

| 보존 파일 | 원래 제품 경로 | SHA-256 |
| --- | --- | --- |
| `src/UninstallSupport.cs` | `src/UninstallSupport.cs` | `51590684E43AD4DB9BC8E7503FF154CCE7F35D877F7868C3D8F8353EECF35E65` |
| `src/UninstallOptionsForm.cs` | `src/UninstallOptionsForm.cs` | `611B8584CEF76F00EBB1D22F5F29377774012CF19D20748B6B2D8C8E252F5CDD` |
| `tests/UninstallFeatureTests.cs` | `tests/ReleaseNotesAndUninstallTests.cs`의 설치 제거 부분 | 재구성본 |

두 제품 소스는 마지막 정상 검증 빌드의 불변 컴파일 스냅샷 `build/generated/compile-snapshot-b9f8f20df9414b64a6349c402f265b27/src/`에서 바이트 내용이 같은지 SHA-256으로 확인한 뒤 복원했습니다.

범위 변경 과정에서 원래 결합 테스트 소스가 보류 지시 전에 삭제되어, 설치 제거 테스트는 당시 실행 목록과 공개 API를 기준으로 `UninstallFeatureTests.cs`에 재구성했습니다. 삭제 전 컴파일된 로컬 `build/ReleaseNotesAndUninstallTests.exe`는 길이 `148480`, SHA-256 `E8246EE1E7A4B043DB6C286DE1D7596177F8A22B81C6CB26CDADA4FE66249B70`이지만 빌드 산출물이므로 이 폴더에 복사하지 않습니다. 원본 테스트 복구가 필요하면 해당 로컬 산출물의 심볼/IL과 이전 작업 기록을 대조해야 합니다.

## 기존 설계의 기능

- Velopack 설치 레이아웃과 `sq.version`의 정확한 패키지 ID·버전·main EXE를 검사한 뒤 등록된 `Update.exe uninstall --silent`에 위임
- Portable·개발 빌드에서는 실제 제거 위임 비활성화
- 사용자 로컬 데이터 보존 또는 명시적 완전 삭제 선택
- 앱 관리 방화벽 규칙 보존 또는 전체 해제 선택
- 방화벽 규칙은 비활성 규칙까지 조회하고 제거 후 다시 조회해 최종 부재 확인
- 앱 전용 시작 메뉴 제거 바로가기 생성·정리
- 삭제 경로·재분석 지점·환경 토큰을 좁게 제한

## 의존성

재도입하려면 다음 연결을 한 번에 복구하고 다시 검토해야 합니다.

- `AppBranding.cs`: `BrandedForm`
- `FirewallRuleManager.cs`: `QueryAllOwnedManagedRules()`와 `RemoveManyWithElevationAsync()`; 전자는 현재 제품에서 제거됨
- `MainForm.cs`: 헤더 진입점과 release/development/preview 경계
- `Program.cs`: 명시적 인자 라우팅
- `GitHubUpdateService.cs`: Velopack install/uninstall fast callback. 현재의 `OnAfterUpdateFastCallback` 기반 일회성 업데이트 완료 안내와 함께 등록 순서·실패 격리를 검증해야 함
- `build.ps1`: 앱 및 테스트 입력, `/reference:System.Xml.dll`
- `package-release.ps1`: Velopack 기본 `--mainExe`·설치 등록은 유지하되 중복 제거 프로그램을 만들지 않는 경계

## 현재 한계와 보류 이유

- Velopack 설치본은 이미 Windows 제어판·`설치된 앱`에서 제거할 수 있어 별도 앱 UI와 시작 메뉴 제거 바로가기의 사용자 가치가 확정되지 않았습니다.
- 방화벽 규칙·사용자 데이터 선택 삭제까지 제거 UI에 묶으면 UAC 취소, 부분 성공, 앱 제거 성공/정리 실패 조합을 모두 지원해야 합니다.
- `Update.exe` 레이아웃과 `sq.version` 형식은 사용 중인 Velopack 고정 버전에 종속됩니다.
- 사용자 데이터 삭제는 복구가 어려우므로 재분석 지점, 동시 실행, 잠긴 파일, 레거시 데이터 폴더를 실제 Windows 10·11 설치 환경에서 추가 검증해야 합니다.
- 기본 제어판 제거와 별도 UI가 동시에 존재할 때 서로 다른 데이터·방화벽 보존 정책이 생겨 사용자에게 혼란을 줄 수 있습니다.

## 재도입 체크리스트

1. 기본 제어판 제거만으로 해결되지 않는 실제 사용자 요구를 먼저 확인합니다.
2. 별도 UI 대신 제거 뒤 독립적인 `로컬 데이터 정리`·`방화벽 규칙 전체 해제` 기능이 더 적절한지 비교합니다.
3. 보존 소스를 현재 Velopack 고정 버전 API와 대조하고 install/update/uninstall callback 시그니처를 실제 DLL로 검증합니다.
4. `QueryAllOwnedManagedRules()`를 복구할 경우 앱 소유 규칙 판정과 비활성 규칙 범위를 다시 보안 검토합니다.
5. 개발·Portable·preview·release 빌드 경계와 임의 `Update.exe` 위임 차단을 회귀 테스트합니다.
6. 사용자 데이터 삭제의 exact-root·재분석 지점·경로 탈출·부분 실패·재시도 테스트를 통과시킵니다.
7. 방화벽 조회 → 제거 → 최종 재조회 순서와 UAC 취소/일부 실패에서 앱 제거를 시작하지 않는 정책을 확인합니다.
8. Windows 10·11의 신규 설치, 업데이트 후 제거, 제어판 제거, 재설치, 데이터 보존을 격리 VM에서 검증합니다.
9. 문서·개인정보 안내·UI 문구를 현재 동작과 일치시킨 뒤에만 제품 빌드 입력에 다시 포함합니다.

## 보존 테스트 목록

- 설치형/Portable/개발 레이아웃 판정
- 변조·중복·외부 엔터티·타 패키지 `sq.version` 거부
- 등록된 updater 위임과 실패 처리
- 사용자 데이터 환경 토큰과 exact-root 삭제 경계
- 시작 메뉴 소유 바로가기 경계
- preview UI의 실제 제거 비활성화
- 비활성 규칙 포함 방화벽 조회, 일괄 제거, 최종 부재 확인
- 방화벽 실패·UAC 취소·updater 시작 실패 시 재시도 가능 상태
- Velopack 기본 Installed Apps 설정을 중복 제거 프로그램 없이 유지
