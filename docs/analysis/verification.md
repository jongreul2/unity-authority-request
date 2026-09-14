# 검증 기록

## 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro |
| Unity | 6000.3.9f1 (URP 17.3.0, Test Framework 1.6.0) |
| 실행 | `Unity -batchmode -nographics -runTests -testPlatform EditMode` / `PlayMode` |
| 날짜 | 2026-09-14 |

## 결과 요약

| 스위트 | 통과 / 전체 | 비고 |
|---|---|---|
| EditMode | 110 / 110 | `Jongreul.AuthorityRequest.Core.Tests` — UnityEngine 무의존 어셈블리만 대상 |
| PlayMode — 데모 흐름 | 2 / 2 | `GateDemoPlayModeTests` |
| PlayMode — 캡처 | (Explicit) | `GateDemoCapture` — README GIF 촬영 전용, 일반 실행에서 제외 |
| PlayMode — Fusion Single | 2 / 2 | Unity **6000.0.58f2** + Fusion 2.0.6에서 실행. 아래 "Fusion 런타임" 참고 |

### 픽스처별 EditMode 수

| 픽스처 | 수 |
|---|---|
| `Gate.ActionGateTests` | 28 |
| `Gate.MockGateServerTests` | 7 |
| `LateJoin.SnapshotProviderTests` | 5 |
| `LateJoin.SnapshotRequesterTests` | 12 |
| `LateJoin.MockSnapshotNetworkTests` | 3 |
| `Purchase.IdempotencyCacheTests` | 15 |
| `Purchase.PurchaseLedgerTests` | 11 |
| `Grab.GrabArbiterTests` | 19 |
| `Grab.HandInventoryPolicyTests` | 5 |
| `Grab.GrabbableReplicaTests` | 5 |

## 불변식 테스트 조건

### 게이트 혼돈 실행 — `MockGateServerTests.ChaosRun_KeepsGateAndServerConsistent`

- 서버: 지연 250 ms ± 250 ms(균등), 거절 20 %, 중복 응답 30 %, 쿨타임 0.4 s, 시드 1234
- 게이트: 응답 타임아웃 0.35 s
- 입력: 20 ms 간격 3000회(가상 시계 60 s)
- 단언
  1. 게이트가 보낸 요청 수 = 서버가 받은 요청 수
  2. 게이트가 적용한 응답의 시퀀스가 엄격히 증가(중복·역전 응답 미적용)
  3. 서버 실행 간격 ≥ 쿨타임(이중 실행 없음)
  4. 중복 무시·지난 응답 무시·타임아웃이 각각 1회 이상 발생(조건이 실제로 가혹했음을 확인)

### 늦은 입장 수렴 — `MockSnapshotNetworkTests.LateJoinersWithJitter_ConvergeToServerState`

- 서버 16슬롯, 20 ms마다 무작위 슬롯에 무작위 값(0~3, 0은 기본값) 400회
- 60스텝마다 클라이언트 입장(총 7명), 입장 직후 스스로 스냅샷 요청
- 메시지 지연 150 ms ± 120 ms(균등) → 변경 순서 역전 발생
- 변경 종료 후 전송 중 메시지를 모두 배달한 뒤 단언: 전원 `Synced`, 버전 = 서버 버전, 16슬롯 값 전부 일치

### 스냅샷 크기 — `MockSnapshotNetworkTests.SnapshotSize_EqualsNonDefaultSlots`

- 64슬롯 중 3슬롯만 기본값이 아님 → 스냅샷 항목 3개(전체 64개가 아니라 변경된 것만)

### 정지 자세 일치 — `GrabbableReplicaTests.RestBroadcast_PutsEveryPeerAtIdenticalPose`

- 피어 3명: 소유자(손 자세), 보간 35 %, 보간 80 %로 서로 다른 표시 자세
- 서버 정지 방송 뒤 세 피어의 `PoseData`가 7개 float 모두 정확히 같음(`==` 비교, 오차 허용 없음)

## 데모 흐름 (PlayMode)

| 테스트 | 조건 | 결과 |
|---|---|---|
| `MashingTenTimes_SendsOneRequest_ServerExecutesOnce` | 지연 100 ms, 10 ms 간격 10탭 | 서버 수신 1 · 실행 1 · 로컬 거부 9 |
| `BypassingGate_SendsEveryTap_ServerStillExecutesOnce` | 같은 조건, 게이트 우회 | 서버 수신 10 · 실행 1 |

## Fusion 런타임

### 검증 환경

- Photon Fusion 2.0.6 (Stable 1034) + **Unity 6000.0.58f2** (Photon 공식 VR 샘플과 같은 조합)
- 같은 커밋을 별도 작업 사본으로 열고 패키지만 6000.0용으로 맞춤: URP 17.0.4, Test Framework 1.5.1, mono-cecil 1.11.5
- `-runTests -testPlatform PlayMode -testFilter Jongreul.AuthorityRequest.Demos.Fusion.Tests`

| 테스트 | 결과 | 시간 | 흐름 |
|---|---|---|---|
| `PurchaseSingleModeTests.SameRequestIdTwice_ChargesOnce_AndReplaysResult` | 통과 | 0.27 s | `StartGame(Single)` → 네트워크 프리팹 스폰 → 시작 잔고 1000 확인 → `hat`(300) 구매 RPC → 잔고 700·보유 확인 → 같은 요청 ID 재전송 → 결과 `Replayed`, 잔고 700 유지, 서버 실행 1회 |
| `GrabSingleModeTests.GrabRejectSecond_ReleaseSettlesAtServerRestPose` | 통과 | 1.47 s | 큐브 2개·바닥 → 첫 큐브 잡기 승인 → 두 번째는 `LimitReached` → 손으로 옮긴 뒤 놓기 → 서버 물리가 바닥까지 떨어뜨리고 정지 판정 → 서버 `Resting`, 표시 자세와 서버 정지 자세 차이 < 1 mm → 두 번째 큐브 잡기 승인 |

### 검증하면서 고친 것

- **Host 자기 요청의 Source** — Fusion `RpcHostMode` 기본값은 `SourceIsServer`라 Host가 부른 RPC의 `info.Source`가 `PlayerRef.None`이다. 구매 RPC는 실제 플레이어가 아니면 버리므로 Host 플레이어의 구매가 조용히 사라진다. 클라이언트 → 서버 RPC 4개에 `HostMode = RpcHostMode.SourceIsHostPlayer`를 지정했다. Single 모드에서는 수정 전에도 Source가 로컬 플레이어였으므로(수정 전 구매 테스트도 통과) 이 수정은 Host 모드용이며, Host 실행은 App ID가 필요해 아직 확인하지 않았다.
- **테스트 대기 기준** — 처음엔 프레임 수(600)로 기다렸는데 배치 모드는 프레임 제한이 없어 0.85 s 만에 끝나 서버 물리 정지를 기다리지 못했다. 실시간 10 s 기준으로 바꾸고 실패 시 단계 이름과 서버·레플리카 상태를 출력하게 했다.

### Unity 6000.3에서 실행되지 않는 이유

Fusion 2.0.6 에디터 코드가 `UnityEditor.HierarchyProperty.CopySearchFilterFrom`를 리플렉션으로 찾는데 Unity 6000.3에는 그 메서드가 없어 `NetworkRunner.StartGame` → `NetworkProjectConfig.Global` 로드가 실패한다. 저장소 코드 문제가 아니라 SDK·에디터 버전 조합 문제다. 6000.3에서 돌리려면 Unity 6.3을 지원하는 Fusion 2 SDK가 필요하다(지원 버전은 Photon 릴리스 노트에서 확인하지 못했다).

### 남은 범위

- Host + Client 2피어, Dedicated Server — Photon App ID 필요
- 늦게 들어온 피어가 `[Networked]` 잔고·그랩 상태를 받는지 — 2피어 필요
