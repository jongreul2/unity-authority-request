# 검증 기록

## 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro |
| Unity | 6000.3.9f1 (URP 17.3.0, Test Framework 1.6.0) — Fusion 런타임만 6000.0.58f2 |
| 실행 | `Unity -batchmode -nographics -runTests -testPlatform EditMode` / `PlayMode` |
| 날짜 | 2026-09-14 |

## 결과 요약

| 스위트 | 통과 / 전체 | 비고 |
|---|---|---|
| EditMode | 119 / 119 | `Jongreul.AuthorityRequest.Core.Tests` — UnityEngine 무의존 어셈블리만 대상 |
| PlayMode — 데모 흐름 | 2 / 2 | `GateDemoPlayModeTests` |
| PlayMode — 캡처 | (Explicit) | `GateDemoCapture` — README GIF 촬영 전용, 일반 실행에서 제외 |
| PlayMode — Fusion Single | 2 / 2 | Unity **6000.0.58f2** + Fusion 2.0.6. [결과 XML](fusion-single-mode-unity6000.0.xml) · 아래 "Fusion 런타임" |

### 픽스처별 EditMode 수

| 픽스처 | 수 |
|---|---|
| `Gate.ActionGateTests` | 30 |
| `Gate.MockGateServerTests` | 8 |
| `LateJoin.SnapshotProviderTests` | 5 |
| `LateJoin.SnapshotRequesterTests` | 15 |
| `LateJoin.MockSnapshotNetworkTests` | 4 |
| `Purchase.IdempotencyCacheTests` | 16 |
| `Purchase.PurchaseLedgerTests` | 12 |
| `Grab.GrabArbiterTests` | 19 |
| `Grab.HandInventoryPolicyTests` | 5 |
| `Grab.GrabbableReplicaTests` | 5 |

## 불변식 테스트 조건

### 게이트만으로 이중 실행 방지 — `MockGateServerTests.ChaosWithoutTimeouts_GateAloneKeepsRequestsOutOfServerCooldown`

- 서버: 지연 250 ms ± 250 ms(균등), 거절 20 %, 중복 응답 30 %, 쿨타임 0.4 s, 시드 77
- 게이트: 응답 타임아웃 없음
- 입력: 20 ms 간격 3000회(가상 시계 60 s)
- 단언: 서버 쿨타임(`server-cooldown`)으로 거절된 요청 0건, 서버 실행 50회 초과, 중복 응답 무시 1회 이상
- 의미: 목 서버도 쿨타임을 판정하지만, 이 테스트는 그 판정이 한 번도 쓰이지 않았음을 보인다. 게이트가 응답 전에는 요청을 보내지 않고 쿨타임을 응답 도착 시점부터 세므로, 서버보다 먼저 입력을 여는 일이 없다.

### 게이트 혼돈 실행 — `MockGateServerTests.ChaosRun_KeepsGateAndServerConsistent`

- 위 조건에 응답 타임아웃 0.35 s, 시드 1234
- 단언
  1. 게이트가 적용한 응답의 시퀀스가 엄격히 증가(중복·역전 응답 미적용)
  2. 중복 무시·지난 응답 무시·타임아웃이 각각 1회 이상 발생(조건이 실제로 가혹했음을 확인)
  3. 요청 수 일치·서버 실행 간격 ≥ 쿨타임 — 목 서버 구조상 참인 단언이므로 증명력은 1·2와 위 테스트에 있다

### 늦은 입장 수렴 — `MockSnapshotNetworkTests.LateJoinersWithJitter_ConvergeToServerState`

- 서버 16슬롯, 20 ms마다 무작위 슬롯에 무작위 값(0~3, 0은 기본값) 400회
- 60스텝마다 클라이언트 입장(총 7명), 입장 직후 스스로 스냅샷 요청
- 메시지 지연 150 ms ± 120 ms(균등), 빈칸 유예 0.3 s
- 단언: 순서가 어긋난 변경이 1건 이상 도착 / 변경 종료 후 전원 `Synced`, 버전·16슬롯 값 전부 서버와 일치 / 스냅샷 전송 ≤ 클라이언트 수 × 2

### 빈칸 유예 측정 — `MockSnapshotNetworkTests.GapGrace_KeepsClientSyncedWhileChangesKeepFlowing`

- 클라이언트 1명, 60 s 동안 20 ms마다 변경, 지연 150 ms ± 120 ms
- 매 스텝 `State == Synced` 비율과 스냅샷 수를 잰다

| 빈칸 처리 | Synced 유지율 | 스냅샷 |
|---|---|---|
| 즉시 재요청 | 14.1 % | 162 |
| 유예 0.3 s | 99.6 % | 1 |

### 스냅샷 크기 — `MockSnapshotNetworkTests.SnapshotSize_EqualsNonDefaultSlots`

- 64슬롯 중 3슬롯만 기본값이 아님 → 스냅샷 항목 3개(전체 64개가 아니라 변경된 것만)

### 정지 자세 일치 — `GrabbableReplicaTests.RestBroadcast_PutsEveryPeerAtIdenticalPose`

- 피어 3명: 소유자(손 자세), 보간 35 %, 보간 80 %로 서로 다른 표시 자세
- 서버 정지 방송 뒤 세 피어의 `PoseData`가 7개 float 모두 정확히 같음. 정지 방송은 보간을 거치지 않고 그대로 복사한다는 규칙의 확인이다.

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
- 결과 원본: [fusion-single-mode-unity6000.0.xml](fusion-single-mode-unity6000.0.xml)

| 테스트 | 결과 | 시간 | 흐름 |
|---|---|---|---|
| `PurchaseSingleModeTests.SameRequestIdTwice_ChargesOnce_AndReplaysResult` | 통과 | 0.23 s | `StartGame(Single)` → 네트워크 프리팹 스폰 → 시작 잔고 1000 확인 → `hat`(300) 구매 RPC → 잔고 700·보유 확인 → 같은 요청 ID 재전송 → 결과 `Replayed`, 잔고 700 유지, 서버 실행 1회 |
| `GrabSingleModeTests.GrabRejectSecond_ReleaseSettlesAtServerRestPose` | 통과 | 1.47 s | 큐브 2개·바닥 → 첫 큐브 잡기 승인 → 두 번째는 `LimitReached` → 손으로 옮긴 뒤 놓기(서버 검증 통과) → 서버 물리가 바닥까지 떨어뜨리고 정지 판정(선속도·각속도·Sleeping·최소 0.3 s) → 서버 `Resting`, 표시 자세와 서버 정지 자세 차이 < 1 mm → 두 번째 큐브 잡기 승인 |

### 검증하면서 고친 것

- **Host 자기 요청의 Source** — Fusion `RpcHostMode` 기본값은 `SourceIsServer`라 Host가 부른 RPC의 `info.Source`가 `PlayerRef.None`이다. 구매 RPC는 실제 플레이어가 아니면 버리므로 Host 플레이어의 구매가 조용히 사라진다. 클라이언트 → 서버 RPC 4개에 `HostMode = RpcHostMode.SourceIsHostPlayer`를 지정했다. Single 모드에서는 수정 전에도 Source가 로컬 플레이어였으므로(수정 전 구매 테스트도 통과) 이 수정은 Host 모드용이며, Host 실행은 App ID가 필요해 아직 확인하지 않았다.
- **테스트 대기 기준** — 처음엔 프레임 수(600)로 기다렸는데 배치 모드는 프레임 제한이 없어 0.85 s 만에 끝나 서버 물리 정지를 기다리지 못했다. 실시간 10 s 기준으로 바꾸고 실패 시 단계 이름과 서버·레플리카 상태를 출력하게 했다.

### 코드 리뷰 뒤 고친 것

독립 리뷰(읽기 전용, 스펙·정확성·README 주장 기준)에서 나온 항목과 조치.

| 항목 | 조치 | 확인 |
|---|---|---|
| 순서 역전 1회에도 즉시 스냅샷 재요청 → 변경이 계속되면 거의 항상 `Requesting` | 빈칸 유예 0.3 s, 진전이 있으면 유예 갱신, 유실일 때만 재요청 | 유지율 14.1 % → 99.6 % (위 측정) |
| `ForgetPlayer`가 재응답 캐시를 남김 → 같은 번호로 들어온 새 사람이 이전 결과를 받음 | `IdempotencyCache.RemoveWhere`로 해당 플레이어 키 삭제 | `PurchaseLedgerTests.ForgetPlayer_NextPersonWithSameIdDoesNotGetPreviousReplay` |
| 복제 목록이 가득 차면 원장만 차감될 수 있음 | 판정 전에 용량 검사, `PurchaseStatus.Unavailable`(차감·보관 없음) | 코드 확인(가득 찬 상황 테스트는 없음) |
| 정지 판정이 선속도 하나뿐 | 선속도·각속도·Sleeping + 최소 경과 시간 | `GrabSingleModeTests` 재실행 통과 |
| 정상 릴리즈에서 물리 시작이 두 번 호출됨 | RPC 릴리즈 중 표시로 "서버가 직접 놓은 경우" 분기와 분리 | 코드 확인 |
| 클라이언트 릴리즈 위치·속도를 그대로 채택 | 마지막 손 자세 기준 0.75 m, 속도 15 m/s 상한 | 코드 확인 |
| 타임아웃이 `Tick`에서만 평가됨 | 응답 처리 전에 `Tick` | `ActionGateTests.ResponseArrivingAfterTimeout_BeforeTick_IsTreatedAsLate` |
| 스냅샷 요청에 타임아웃 없음 | 요청 타임아웃 2 s 후 새 ID로 재요청 | `SnapshotRequesterTests.WithClock_LostSnapshotResponse_RetriesAfterTimeout` |
| 이전 응답의 중복을 `Stale`로 분류 | 최근 해결 시퀀스 64개 기억 | `ActionGateTests.DuplicateOfOlderResolvedResponse_IsCountedAsDuplicate` |
| 같은 Id 뷰·32개 초과·Despawn 뒤 연결 유지 | 검증 로그, Despawn 시 연결 해제 | 코드 확인 |
| 혼돈 실행의 일부 단언이 목 구조상 당연히 참 | 게이트 단독 증명 테스트 추가, 역전 발생 단언 추가 | 위 테스트 조건 |

### Unity 6000.3에서 실행되지 않는 이유

Fusion 2.0.6 에디터 코드가 `UnityEditor.HierarchyProperty.CopySearchFilterFrom`를 리플렉션으로 찾는데 Unity 6000.3에는 그 메서드가 없어 `NetworkRunner.StartGame` → `NetworkProjectConfig.Global` 로드가 실패한다. 저장소 코드 문제가 아니라 SDK·에디터 버전 조합 문제다. 6000.3에서 돌리려면 Unity 6.3을 지원하는 Fusion 2 SDK가 필요하다(지원 버전은 Photon 릴리스 노트에서 확인하지 못했다).

### 남은 범위

- Host + Client 2피어, Dedicated Server — Photon App ID 필요
- 늦게 들어온 피어가 `[Networked]` 잔고·그랩 상태를 받는지 — 2피어 필요
- 스펙 2-6 "멀티 대기실" 데모 씬(모듈 B 시각화) — 미작성
