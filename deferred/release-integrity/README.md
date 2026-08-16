# 배포 무결성 실험 구현 보류본

이 디렉터리는 v0.8.0 제품과 기본 빌드에서 제외한 공개 Build ID, 빌드 출처(provenance), 바이너리 일관성 검사 구현을 보존한다. 현재 제품의 인증 또는 신뢰 기준이 아니며, 자동 빌드·테스트·패키징에서 실행하지 않는다.

## Build ID·출처 구현

보존한 파일은 활성 당시의 상대 경로를 이 디렉터리 아래에 그대로 유지한다.

| 보류 경로 | 바이트 | SHA-256 |
| --- | ---: | --- |
| `build.ps1` | 36420 | `025bed2a9b74d3e0614926e3755c31389fae815e3d3ca2c8218041c6b07fcf2a` |
| `src/BuildIdentity.cs` | 16359 | `6de793d72e938af9dc0aa4b34f6796831cab298221295fba205737dff8c92706` |
| `tools/Prepare-BuildIdentity.ps1` | 10035 | `36bb57c7c1e9728832fb4c50ab04a27d191bf7a3ecf3f0a8a39eea3afc6fd31d` |
| `tools/New-ReleaseProvenance.ps1` | 9342 | `03641fbd1da2098b8e8617b969f31689a6b842ea20140dc6e82680a25a5795e8` |
| `tools/Test-ReleaseBinaryIdentity.ps1` | 8496 | `70509706157b98f44ec4b550ddd7a3128d809b5f83cbbd90b1e880ce626c22fa` |
| `tests/BuildIdentityAppInspector.cs` | 7140 | `285721cf7fe9434627b51ef931cc2df8d94509ab11292f23c1e87e548d7c2136` |
| `tests/BuildIdentityTests.cs` | 11686 | `96522af44fd1e977f8cd81112729d255aabd138d9fb5a800038181f9153d8071` |
| `tests/BuildIdentityScriptTests.ps1` | 30188 | `17cc5718594ad11cb9375bf2bcb173883c141d1411175b99b04b66e025d36552` |
| `tests/ReleaseProvenanceTests.ps1` | 10587 | `5e6bb65668e1e2e53d8013ae2a0fdfca0fb9e40461a7b49cdd1155a3dcaac2b9` |
| `tests/ReleaseBinaryIdentityScannerTests.ps1` | 7604 | `ae5e4eb62aaff0949cfb0adf9ff6115e8e972ed26c3cb4604ac5f6c48e574d3d` |

`src`, `tools`, `tests` 파일의 해시는 보류 이동 직전 활성 파일의 정확한 바이트와 일치한다. `build.ps1`은 검증된 컴파일 스냅샷의 원본 텍스트를 보존하면서 줄 끝만 LF로 정규화했다. 스냅샷 원본은 36566바이트, SHA-256 `bf909322ae2e1257b75b185068ab0490f0117637972dcac3bedf6071cfc8326e`였으며 줄 끝을 제외한 텍스트가 일치함을 확인했다. 재사용 전에는 보류본 자체의 표 해시와 다시 비교해야 한다.

## 패키징·공개 자산 검증 묶음

다음 파일은 강화 패키징, 최종 배포물 민감정보 검사, 공개 Release 자산 재다운로드 검증을 함께 시험하던 당시 상태를 원래 상대 경로로 보존한 것이다.

| 보류 경로 | 바이트 | SHA-256 |
| --- | ---: | --- |
| `package-release.ps1` | 65422 | `5a24fd13a4d32b4b855e4e5c3e313543578818d3d14a1ffccb6c673534cbf8e8` |
| `release-scan-allowlist.json` | 10889 | `3b553edc616e7ba8d742820542fad23d1c67f31d84be20a924a7a6fa9928117f` |
| `tools/Prepare-Velopack.ps1` | 13475 | `7f32f5c102d3adaca5c0c250ddc992732c9da2d3f01faa3a27f6900bb8de3524` |
| `tools/Assert-ReleaseSanitized.ps1` | 46279 | `a23775707aa419dcd0b2e1b824629569b5bebb3cef91f8c3713b16b08078babb` |
| `tools/New-ReleaseAssetManifest.ps1` | 12809 | `43762ceea9f4e60cead181b1da59be18957f9388bc4677dce7db90dcefa74cf2` |
| `tools/New-ReleaseCompletionMarker.ps1` | 5526 | `dff0ee7acf5fb6af87a9ad0a10c386906c6b0ee4d1e75c3317953071717ceeb0` |
| `tools/Test-PublishedReleaseAssets.ps1` | 38265 | `4a844c7c48e79272f583d78646ce12679e1b7305a853306b2ec1b84a50bca498` |
| `tests/PackageReleasePreflightTests.ps1` | 28763 | `602f99a804d51b18c3934f5e989facfb94b213aeb6f959d68d0ce52be8ce0dcd` |
| `tests/PublishedReleaseAssetVerifierTests.ps1` | 24500 | `764bbd958ffc14c73ec1735388feaf31a315c9a5aa2a7d8bcf28c45f6a815f80` |
| `tests/ReleaseSanitizationTests.ps1` | 31146 | `073aeca3ab5caf492d79b8b150955a923d77165916ff750345f96eae82a31b76` |

활성 루트의 `package-release.ps1`과 `tools/Prepare-Velopack.ps1`은 정식 게시된 v0.7.4 방식으로 복원하고 기본 패키지 버전만 v0.8.0에 맞췄다. 활성 절차는 Velopack 의존성의 고정 SHA-256 검증, 빌드·테스트, Setup·Portable·NUPKG·업데이트 feed 생성, `SHA256SUMS.txt`와 검토 ZIP 해시 생성을 유지한다. 위 보류 묶음의 전용 민감정보 스캔, provenance, expected manifest·ready marker 및 게시 후 재다운로드 검증은 호출하지 않는다.

보존한 `package-release.ps1`과 테스트는 원래 프로젝트 루트 배치를 전제로 상대 경로를 계산한다. 따라서 이 디렉터리에서 곧바로 실행할 수 있는 독립 도구가 아니며, 활성 배포 명령으로 사용해서도 안 된다. 또한 `New-ReleaseCompletionMarker.ps1`의 release-readiness marker는 앱 업데이트 완료 뒤 패치 내역을 한 번 표시하기 위한 제품 marker와 목적·저장 위치가 다른 별도 실험이다.

## 의존성과 결합 지점

- Windows PowerShell 5.1, Git CLI, .NET Framework 4.x `csc.exe`를 전제로 한다.
- `BuildIdentity.cs`는 생성된 `BuildIdentity.Generated.cs`, `System.Web.Extensions`의 JSON 직렬화기, `TarkovServerReporter.BuildIdentity.json` 내장 리소스와 어셈블리 메타데이터가 모두 필요하다.
- `Prepare-BuildIdentity.ps1`은 Git 작업 트리 또는 소스 아카이브를 입력으로 사용하며, 당시 `build.ps1`의 검증된 컴파일 스냅샷 함수들과 함께 설계됐다.
- 보존한 `build.ps1`은 이 묶음의 결합 구조를 재현하기 위한 참조 스냅샷이다. 현재 디렉터리 구조에서는 상대 경로가 맞지 않으므로 직접 실행하지 않는다.
- `New-ReleaseProvenance.ps1`과 `Test-ReleaseBinaryIdentity.ps1`은 패키징 시 만들어지는 Build ID 매니페스트, 검사기 EXE, ZIP·NUPKG 자산 구조에 의존한다.
- 강화 `package-release.ps1`은 같은 보류 묶음의 검사·manifest·marker 도구와 exact allowlist를 활성 루트 위치에서 찾으며, 일부만 복원해서는 동작하지 않는다.
- `Test-PublishedReleaseAssets.ps1`은 GitHub API와 공개 자산 다운로드 네트워크 접근을 사용한다. fixture 테스트와 실제 공개 Release 검증을 분리해야 한다.
- `Assert-ReleaseSanitized.ps1`은 Windows PowerShell 5.1과 .NET 압축 API를 사용하며, 당시 allowlist와 패키징 입력 구조에 맞춰져 있다.
- 보존된 테스트에는 활성 루트의 `src`, `tools`, `build.ps1`, `package-release.ps1` 경로를 검사하는 회귀 조건이 있다. 현재 위치에서 곧바로 실행하는 테스트 묶음이 아니다.

## 알려진 한계

- 공개 Build ID와 출처 정보는 파일 간 일관성 진단일 뿐, 제작자 인증·코드 서명·변조 방지 수단이 아니다.
- 동일한 공개 로직과 메타데이터는 수정본도 재생성할 수 있으므로 공식 배포본 여부를 독립적으로 증명하지 못한다.
- 당시 컴파일 스냅샷·패키징 흐름과 결합돼 있어 일부만 복원하면 빌드가 실패하거나 잘못된 신뢰 인상을 줄 수 있다.
- 다시 도입하려면 활성 소스 경로로 무조건 복사하지 말고, 필요성·유지비·사용자 가치부터 재검토한 뒤 코드 서명 및 서명된 해시 매니페스트 계획과 함께 새 설계를 검증해야 한다.
- 전용 민감정보 검사는 난독화되거나 실행 중 조립되는 비밀값과 Setup 내부의 독자 압축 영역을 완전하게 판별하지 못하므로 단독 신뢰 기준으로 사용할 수 없다.
- 공개 자산 재다운로드는 검증 자체가 GitHub 다운로드 횟수에 포함될 수 있고, 공식 배포자 인증이나 코드 서명을 대신하지 않는다.

## 재검토 원칙

현재 v0.8.0은 v0.7.4 수준의 단순 빌드·릴리스 방식을 유지한다. 이 보류본은 향후 검토 자료이며, 명시적인 재도입 결정과 별도 테스트 계획 없이는 제품·패키징·공개 문서에 연결하지 않는다.
