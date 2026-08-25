# MANUAL_QA.md

자동화할 수 없는 검증 항목. 대부분 실제 유튜브 업로드가 필요한 `[EMPIRICAL]` 작업이다.
수행하면 결과를 `docs/YTT-VERIFICATION.md`에 반영하고 체크박스를 켠다.

**증거 없이 체크하지 마라.** 스크린샷·영상 링크·측정 수치를 남긴다.

---

## M1 — 렌더러

- [ ] **tolerance 확정용 유튜브 스크린샷 측정** — `docs/render-comparison/README.md`의 7단계 절차.
      앵커·박스 tolerance는 현재 **잠정값**이며 이 측정 전까지 확정이 아니다
- [ ] 번들 폰트(Roboto / Liberation 3종)가 Windows·macOS·Linux에서 동일하게 렌더되는지 육안 확인
- [ ] 대체 불가 폰트(`fs=3` Lucida Console, `fs=5` Comic Sans MS, `fs=6` Monotype Corsiva)가
      로컬에 없을 때 **"근사 표시 중" 상태가 실제로 노출**되는지 확인. 조용한 폴백이면 결함이다
- [ ] `.ytt` → 프로젝트 → `.ytt` 왕복 결과를 실제 업로드해 원본과 동일하게 보이는지 확인
- [ ] **`ap`/`ju` 독립 조합** (`YTT-VERIFICATION.md` V-13) — 예: `ap=7` + `ju=0`을
      업로드해 실제로 왼쪽 정렬로 표시되는지. 현재 export 경로의 제한을 실증한다

## M2 — 영상

- [ ] libmpv 로드 (Windows / macOS / Linux) — M0에서 **미설치로 확인 불가** 처리된 항목
- [ ] 1920×1080@60 / 2560×1440@60 / 3840×2160@30 재생 중 frame drop
- [ ] VFR 영상에서 frame step과 exact seek이 decoder timestamp 기준으로 동작하는지
- [ ] 리사이즈·전체화면 전환 중 크래시 없음
- [ ] 0.5x / 2x 재생에서 자막 타이밍 어긋남 없음

## M3 — 효과 · 뷰포트 · 검증기

- [ ] **뷰포트 모드 실측** (E-8) — 일반 / 극장 / 전체화면에서 알려진 `ah`/`av`의 자막이
      실제로 어디에 그려지는지 측정. **측정 전 §7.8 구현 금지**
- [ ] shake / chroma 자막의 시작·종료 시각과 이동 경로가 실제 플레이어와 실사용상 동등한지
- [ ] `W101` 파일 크기 경고 근처 케이스 — 경고가 뜬 자막이 실제로 브라우저에서 안 뜨는지,
      경고가 없는데 안 뜨는 경우는 없는지 (근사치임을 실증)
- [ ] mitmproxy 프리뷰 — 자막 트랙이 **없는** 영상에서도 CC 버튼이 뜨는지
      (`ensure_subtitle_selector`가 실제로 동작하는지)
- [ ] `ytInitialPlayerResponse` regex가 깨졌을 때 graceful 실패하고 편집·export는 계속 되는지

## M4 — 가라오케

- [ ] 3분짜리 곡의 가라오케 자막을 앱만으로 제작 후 업로드해 타이밍 확인
- [ ] 인접 음절 동일 오프셋 자동 +1ms 처리가 가라오케를 깨뜨리지 않는지

## M5 — 배포

- [ ] self-contained publish 산출물이 libmpv 없는 깨끗한 머신에서 어떻게 동작하는지
- [ ] macOS codesign / notarization
- [ ] Linux AppImage에서 GPU / OpenGL / Wayland / X11
- [ ] crash log에 libmpv 버전이 실제로 기록되는지

---

## 플랫폼 호환성 재검증 (§5.8)

SPEC §5.8 표는 **2026-08-24 시점의 관찰**이지 영구 규격이 아니다.
아래 시점에 재검증하고 검증일을 갱신한다:

- 호환성 경고를 근거로 사용자 선택을 제약할 때
- 사용자로부터 "경고와 실제가 다르다"는 리포트가 올 때
- YTSubConverter pin을 갱신할 때

| 기능 | PC | iOS | Android | 재검증일 |
|---|:--:|:--:|:--:|---|
| 전경 투명도 `fo` | ✓ | ✓ | ✗ | 미수행 |
| 엣지/그림자 `et`/`ec` | ✓ | ✗ | ✗ | 미수행 |
| 루비 `rb` | ✓ | ✗ | ✗ | 미수행 |
| 세로쓰기 `pd`≥2 | ✓ | ✗ | ✗ | 미수행 |

## `[EMPIRICAL]` 미검증 목록

`docs/YTT-VERIFICATION.md` 말미 E-1 ~ E-8이 원본이다. 특히:

- **E-8 뷰포트 좌표 동작** — M3 §7.8 구현 전 **필수 게이트**
- E-6 JSON3 압축 10240 bit/s 한계의 정확한 측정법 — `W101`이 근사치인 이유
- E-7 모바일 효과 과다 시 자막 선택지 미표시 조건 — 정량 기준 없음
