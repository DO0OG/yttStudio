# PROGRESS.md

YttStudio 마일스톤 진행 체크리스트. SSOT는 `docs/SPEC.md` §16.

**규칙:** 각 마일스톤의 "완료 조건"을 만족해야 다음으로 넘어간다.
체크는 **증거가 있을 때만** 켠다 (빌드 로그 / 테스트 출력 / 스크린샷).

| 마일스톤 | 상태 | 브랜치 |
|---|---|---|
| M0 스캐폴딩 + Compatibility Spike | ✅ 완료 (CI 3개 OS 통과) | `1-m0-스캐폴딩-및-호환성-스파이크` |
| M1 렌더러 (영상 없이) | ✅ 완료 (CI 3개 OS 통과) · tolerance 실측 대기 | `3-m1-렌더러-구현` |
| M2 영상 + 편집 캔버스 | 🟡 핵심 완료 · 잔여 항목 있음 | `5-m2-영상-파이프라인-및-편집-캔버스` |
| M3 효과 + 뷰포트 + 검증기 | 🟡 핵심 완료 · 잔여 항목 있음 | `7-m3-효과와-뷰포트와-검증기` |
| M4 가라오케 | ⬜ 미시작 | `m4/karaoke` |
| M5 마감 + 배포 | ⬜ 미시작 | `m5/release` |

---

## M0 — 스캐폴딩 + Compatibility Spike

- [x] .NET 10 솔루션 생성, 의존 방향 검증
- [x] YTSubConverter 서브모듈 추가 + `b186a40bc21e58a8c9651cf616cbb5e80425dfc6`로 pin
- [x] `YTSubConverter.Shared` 프로젝트 참조로 clean build
- [x] Windows restore 성공 / Linux·macOS는 CI에서 실측 통과
- [x] `System.Drawing` 의존 범위 확인 — 어댑터 밖으로 새지 않음
- [x] Avalonia 12.1.1 + SkiaSharp 3.119.4 버전 정렬 확인
- [ ] libmpv 로드 가능 여부 — Windows 미설치로 확인 불가, M2 재확인
- [x] FAIL 항목 없음 — fallback 비용 산정 불필요
- [x] CI: 3개 OS 빌드 + 테스트 워크플로 구성 및 실행 통과
- [x] `docs/PROGRESS.md` 갱신

**완료 조건:** `dotnet build` / `dotnet test` 클린 통과. §3 스택이 `DEPENDENCIES.md`에 확정 기록됨.

## M1 — 렌더러 (영상 없이)

- [x] §6 도메인 모델 (`internal set`), `CueCollection`, `FormatResolver`
- [x] `YTSubConverter.Shared` 어댑터 (양방향, `System.Drawing` 격리)
- [x] `.ytt` / `.ass` import·export (§10), 손실 보고 포함
- [x] Skia 렌더러: 레이아웃(§7.4) + pen 매핑(§7.5)
- [x] `IFontResolver` + 폰트 번들링 (§7.5.1) — Roboto·Liberation 3종 + 라이선스 포함
- [x] 좌표 변환, 폰트 크기 공식 적용
- [x] 앵커 9종, 정렬 3종 정확 동작
- [x] `DocumentEditor` + Undo (§13)
- [x] **`ap`/`ju` 독립성 확보** — 서브모듈 로컬 패치 (V-13, 포크 `c460cca`)
- [ ] 기준 fixture 세트 작성 후 tolerance 확정 — **잠정값으로 진행, 실측 미수행**
- [x] Layout / raster / smoke 3계층 테스트 (§15.2) — 63건 통과, Windows·Linux·macOS CI 검증 완료
- [x] 최소 Avalonia 창: `.ytt` 열기 → 배경 위 렌더 → 시간 슬라이더 스크럽

**완료 조건 대비:**

| 항목 | 결과 |
|---|---|
| `dotnet build -c Release` | 경고 0 / 오류 0 |
| `dotnet test` | 63건 전부 통과 (로컬 + CI 3개 OS) |
| 폰트 크기 왕복 오차 | 0 |
| 좌표 왕복 오차 | ≤ 1px @ 1280×720 |
| 앵커 9종 × 정렬 3종 | 통과 |
| `.ytt` / `.ass` 왕복 | 통과 (`ap`/`ju` 독립 조합 포함) |
| 실제 유튜브 스크린샷 대조 | **미수행** — `docs/render-comparison/README.md` 참조 |

**남은 것:** 앵커·박스 tolerance는 **잠정값**이다. `docs/MANUAL_QA.md`의
"tolerance 확정용 유튜브 스크린샷 측정"을 수행해야 확정된다.

## M2 — 영상 + 편집 캔버스

- [x] libmpv Render API 파이프라인 (§8.2 스레딩 하드 규칙 준수)
- [x] `IVideoSource` + latest-frame-wins 백버퍼 (§8.3, §8.4)
- [x] **성능 spike (§8.6)** — `docs/PERFORMANCE.md`. 3개 해상도 실측, 예산 분리 기록
- [x] VFR / frame step용 mpv property 조합 확정 (아래 표)
- [x] SW vs GPU benchmark 후 기본 경로 결정 — **SW 채택**
- [x] 영상 위 자막 합성, 재생/시크/프레임 이동/속도 조절
- [x] 큐 선택, 드래그, 다중 선택, 범위 선택
- [x] 앵커 UI, 정렬 UI (§9.4) — `ap`/`ju` 독립, 마지막 선택 기준 다중 정렬, 균등 분배
- [x] 스냅 & 가이드 (§9.3)
- [x] 속성 패널: 위치/정렬/텍스트/엣지 + 혼합 값 표시
- [x] 스타일 프리셋 CRUD + 삭제 시 override 굳히기 (§6.5)
- [x] `DocumentEditor` + Undo/Redo (§13)
- [x] 자막 목록 그리드, 타임라인 기본형 (Track / ZOrder 분리)

**확정한 mpv property 조합 (§8.5):**

| 용도 | property |
|---|---|
| 프레임 전진 | `frame-step` |
| 프레임 후진 | `frame-back-step` |
| 현재 PTS | `playback-time` |
| 빠른 scrub | `seek absolute+keyframes` |
| 최종 seek | `seek absolute+exact` |
| FPS | `estimated-vf-fps`, 실패 시 `container-fps` |

VFR 영상에서 frame step이 구간별 33.333ms / 50ms / 16.667ms로 **decoder timestamp 간격을
따르는 것을 확인**했다 (§8.5의 "단일 `double`로 프레임 경계를 표현할 수 없다" 요구 충족).

**완료 조건 대비:**

| 항목 | 결과 |
|---|---|
| `dotnet build -c Release` | 경고 0 / 오류 0 |
| `dotnet test` | 80건 전부 통과 |
| 영상 열고 마우스만으로 배치·스타일링 후 `.ytt` 저장 | 확인 (`docs/render-comparison/m2-canvas.png`) |
| 성능 spike 문서화 | `docs/PERFORMANCE.md` |
| 유튜브 업로드 후 의도대로 표시 | **미수행** — `[EMPIRICAL]`, `MANUAL_QA.md` |

**잔여 항목 (M3 이후로 이월):**

- [ ] 타임라인의 프레임 / 큐 경계 / 재생 헤드 스냅
- [ ] Track 추가·삭제 전용 UI
- [ ] 큐 그리드 다중 행 선택, Enter / Shift+Enter 전용 동작
- [ ] 타임라인에서 다중 선택 큐 동시 이동
- [ ] 슬라이더·연속 속성 입력을 입력 종료 시 커맨드 하나로 묶기 (§13 트랜잭션 확장)
- [ ] 속성별 플랫폼 호환성 배지 전체 (§5.8, §9.5)
- [ ] 실제 유튜브 업로드 검증 (PC / iOS / Android)

## M3 — 효과 + 뷰포트 + 검증기

- [x] Move / Fade / Shake / Chroma / Animate 모델
- [x] 각 효과 미리보기 시각화 (§7.6) — **Shake 결정성 확보** (`cueId` + `frameIndex` 시드)
- [x] ASS 태그 생성 및 `.ass` 왕복 테스트
- [x] 검증기 전체 (§11) — 17개 규칙 전부 + 문제 패널 UI
- [x] `W101` 근사치 표현 및 안전 마진 7168 bit/s (§11.1)
- [x] mitmproxy 스크립트 — **두 역할 모두** (§14.1)
- [x] 뷰포트: `PlayerViewport` 구조 + `VideoFrame`만 활성, 나머지 비활성 유지 (§7.8 실측 게이트)
- [ ] 효과 세부 파라미터 편집 UI — 활성/비활성 토글만 제공
- [ ] mitmproxy 어댑터를 실행하는 앱 UI와 안내 화면

**검증기 규칙별 상태:**

| 규칙 | 구현 | 자동 수정 |
|---|:--:|:--:|
| `E001` 시작 < 1ms | ✅ | ✅ |
| `E002` 끝 ≤ 시작 | ✅ | — |
| `E003` 인접 가라오케 오프셋 동일 | ✅ | ✅ |
| `E004` `fc = #FFFFFF` | ✅ | ✅ |
| `E005` 불투명도 255 | ✅ | ✅ |
| `E006` 영상 길이 초과 | ✅ | — |
| `W101`~`W108` | ✅ (8종) | — |
| `I201`~`I203` | ✅ (3종) | — |

`W102`는 효과 3개 이상 휴리스틱, `W103`은 `VideoFrame` 기준이다. 둘 다 `[HEURISTIC]`이며
정량 근거가 없다 (`YTT-VERIFICATION.md` E-7).

**완료 조건 대비:**

| 항목 | 결과 |
|---|---|
| `dotnet build -c Release` | 경고 0 / 오류 0 |
| `dotnet test` | 111건 전부 통과 |
| `.ytt` XML 직접 조립 | 없음 (`YttDocument.Save()` 위임 유지) |
| shake/chroma가 실제 플레이어와 동등 | **미검증** — `[EMPIRICAL]` |
| 파일 크기·모바일 경고가 근사치임을 명시 | 확인 |

**잔여 항목 (M4 이후로 이월):**

- [ ] 효과 세부 파라미터 편집 UI (radius, offset, accel, 색/크기 전환값 등)
- [ ] `Animate`의 엣지 색 전환을 실제 렌더링에 적용
- [ ] `Animate` 전경색 보간 시작값을 현재 해석 색상으로 (지금은 기본 흰색이라 부정확)
- [ ] 가라오케 타입 Fade / Glitch / Cursor / LeftCursor 미리보기 (M4 본 범위)
- [ ] mitmproxy 어댑터 실행 UI + 프록시·인증서 안내 화면
- [ ] mitmproxy 빈 caption-track 배열 처리, graceful failure 자동 테스트
- [ ] 효과 포함 `.ytt` 경로 전용 테스트
- [ ] 검증기 패널이 보이는 스크린샷
- [ ] **실제 유튜브 플레이어 효과 동등성 검증** — `MANUAL_QA.md`
- [ ] **뷰포트 좌표 실측 (E-8)** — 측정 전까지 `VideoFrame` 외 비활성 유지

### M2 이월 항목 (미착수)

컨텍스트 소진으로 M3에서 손대지 못했다. M4 또는 M5로 이월한다.

- [ ] 슬라이더·연속 속성 입력을 커맨드 하나로 묶기 (§13)
- [ ] 타임라인 프레임 / 큐 경계 / 재생 헤드 스냅 (§9.6)
- [ ] 속성별 플랫폼 호환성 배지 전체 (§5.8, §9.5)
- [ ] 큐 그리드 다중 행 선택, Enter / Shift+Enter (§9.8)
- [ ] Track 추가·삭제 UI (§9.6)
- [ ] 타임라인 다중 선택 큐 동시 이동

## M4 — 가라오케

- [ ] 음절 자동 분할 (한글/가나/라틴/한자)
- [ ] 음절 칩 UI, 분할점 편집
- [ ] 탭 타이밍 입력 + 수동 편집
- [ ] 음절 타임라인 바
- [ ] 가라오케 타입 5종
- [ ] 오디오 파형 표시
- [ ] 미리보기 실시간 반영

## M5 — 마감 + 배포

- [ ] 루비, 세로쓰기, 첨자, 패킹 UI
- [ ] `.yttproj` 저장/열기, 마이그레이션, 자동 저장/복구
- [ ] 검색·치환, 일괄 시간 이동
- [ ] 설정 화면 (단축키, 스냅, 기본 스타일)
- [ ] 한국어/영어 로컬라이제이션
- [ ] §18 네이티브 배포 전략 확정 및 구현
- [ ] 사용자 가이드

---

## 측정 대기 항목 (`[EMPIRICAL]`)

`docs/YTT-VERIFICATION.md` E-1 ~ E-8. 특히 **E-8(뷰포트 좌표 동작)** 은 §7.8 구현 전 필수 게이트다.
