# PROGRESS.md

YttStudio 마일스톤 진행 체크리스트. SSOT는 `docs/SPEC.md` §16.

**규칙:** 각 마일스톤의 "완료 조건"을 만족해야 다음으로 넘어간다.
체크는 **증거가 있을 때만** 켠다 (빌드 로그 / 테스트 출력 / 스크린샷).

| 마일스톤 | 상태 | 브랜치 |
|---|---|---|
| M0 스캐폴딩 + Compatibility Spike | ✅ 완료 (CI 3개 OS 통과) | `1-m0-스캐폴딩-및-호환성-스파이크` |
| M1 렌더러 (영상 없이) | ✅ 로컬 완료 · tolerance 실측 대기 | `3-m1-렌더러-구현` |
| M2 영상 + 편집 캔버스 | ⬜ 미시작 | `m2/canvas` |
| M3 효과 + 뷰포트 + 검증기 | ⬜ 미시작 | `m3/effects` |
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
- [x] Layout / raster / smoke 3계층 테스트 (§15.2) — 63건 통과
- [x] 최소 Avalonia 창: `.ytt` 열기 → 배경 위 렌더 → 시간 슬라이더 스크럽

**완료 조건 대비:**

| 항목 | 결과 |
|---|---|
| `dotnet build -c Release` | 경고 0 / 오류 0 |
| `dotnet test` | 63건 전부 통과 |
| 폰트 크기 왕복 오차 | 0 |
| 좌표 왕복 오차 | ≤ 1px @ 1280×720 |
| 앵커 9종 × 정렬 3종 | 통과 |
| `.ytt` / `.ass` 왕복 | 통과 (`ap`/`ju` 독립 조합 포함) |
| 실제 유튜브 스크린샷 대조 | **미수행** — `docs/render-comparison/README.md` 참조 |

**남은 것:** 앵커·박스 tolerance는 **잠정값**이다. `docs/MANUAL_QA.md`의
"tolerance 확정용 유튜브 스크린샷 측정"을 수행해야 확정된다.

## M2 — 영상 + 편집 캔버스

- [ ] libmpv Render API 파이프라인 (§8.2 스레딩 하드 규칙)
- [ ] `IVideoSource` + latest-frame-wins 백버퍼 (§8.3, §8.4)
- [ ] 성능 spike (§8.6)
- [ ] VFR / frame step용 mpv property 조합 확정 → §8.5 기록
- [ ] SW vs GPU 백엔드 benchmark 후 기본 경로 결정
- [ ] 영상 위 자막 합성, 재생/시크/프레임 이동/속도 조절
- [ ] 큐 선택, 드래그, 다중 선택
- [ ] 앵커 UI, 정렬 UI 전체 (§9.4)
- [ ] 스냅 & 가이드 (§9.3)
- [ ] 속성 패널: 위치/정렬/텍스트/엣지
- [ ] 스타일 프리셋 CRUD + 삭제 시 override 굳히기 (§6.5)
- [ ] `DocumentEditor` + Undo/Redo (§13)
- [ ] 자막 목록 그리드, 타임라인 기본형 (Track / ZOrder 분리)

## M3 — 효과 + 뷰포트 + 검증기

- [ ] Move / Fade / Shake / Chroma / Animate 모델 + 속성 UI
- [ ] 각 효과 미리보기 시각화 (§7.6)
- [ ] ASS 태그 생성 및 왕복 테스트
- [ ] 검증기 전체 (§11) + 문제 패널 UI + 자동 수정
- [ ] `W101` 근사치 표현 및 안전 마진 (§11.1)
- [ ] mitmproxy 프리뷰 — 두 역할 모두 (§14.1)
- [ ] 뷰포트 모드 (§7.8) — 측정 완료 전까지 `VideoFrame` 외 비활성

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
