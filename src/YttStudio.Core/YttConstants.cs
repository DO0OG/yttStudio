namespace YttStudio.Core;

/// <summary>YTT 형식과 편집기가 공유하는 기준 상수다.</summary>
public static class YttConstants
{
    // [UPSTREAM] 유튜브는 지정 좌표에 0.96 을 곱하고 2 를 더한다.
    public const double CoordinateScale = 0.96;

    // [UPSTREAM] 유튜브는 변환된 좌표를 2 퍼센트만큼 이동시킨다.
    public const double CoordinateOffset = 2.0;

    // [UPSTREAM] YTT 폰트 배율은 저장된 차이의 4분의 1만큼 변한다.
    public const double FontScaleDivisor = 4.0;

    // [UPSTREAM] 불투명도 255 는 업로드 시 제거되므로 254 가 안전 상한이다.
    public const byte MaximumOpacity = 254;

    // [UPSTREAM] t=0 은 안드로이드에서 신뢰할 수 없으므로 1 ms 로 보정해야 한다.
    public const long MinimumStartTimeMilliseconds = 1;

    // [UPSTREAM] 인접한 가라오케 섹션은 오프셋이 엄격히 증가해야 한다.
    public const long KaraokeOffsetStepMilliseconds = 1;

    // [UPSTREAM] 렌더 계산은 upstream 의 1280×720 기준 프레임을 사용한다.
    public const int ReferenceWidth = 1280;

    // [UPSTREAM] 렌더 계산은 upstream 의 1280×720 기준 프레임을 사용한다.
    public const int ReferenceHeight = 720;

    // [PRODUCT] acceleration exponent가 1에서 벗어난 정도를 판정하는 무차원 허용오차다.
    // 이 값 안의 차이는 1280 px 진행에서 0.001 px 이하의 기존 테스트 오차로 남는다.
    public const double MotionAccelerationExponentTolerance = 1e-6;

    // [PRODUCT] 편집기는 720p 기준 32 px 을 100 퍼센트 폰트 기준으로 삼는다.
    public const double DefaultFontSizePixels = 32.0;

    // [PRODUCT] 박스 좌우 여백은 해석된 폰트 크기의 4분의 1이다.
    public const double HorizontalBoxPaddingFactor = 0.25;

    // [PRODUCT] 박스 상하 여백은 해석된 폰트 크기의 15 퍼센트다.
    public const double VerticalBoxPaddingFactor = 0.15;

    // [PRODUCT] 아래첨자와 위첨자는 65 퍼센트 크기 글리프를 쓴다.
    public const double ScriptFontScale = 0.65;

    // [PRODUCT] 첨자 글리프는 기준 폰트 크기의 30 퍼센트만큼 이동한다.
    public const double ScriptBaselineOffsetFactor = 0.30;

    // [PRODUCT] 하드 섀도 오프셋은 폰트 크기의 6 퍼센트다.
    public const double HardShadowOffsetFactor = 0.06;

    // [PRODUCT] 글로우 외곽선 두께는 폰트 크기의 8 퍼센트다.
    public const double GlowStrokeWidthFactor = 0.08;

    // [PRODUCT] 소프트 섀도 블러는 폰트 크기의 10 퍼센트다.
    public const double SoftShadowBlurFactor = 0.10;

    // [PRODUCT] 밑줄 두께는 폰트 크기의 16분의 1이다.
    public const double UnderlineThicknessFactor = 1.0 / 16.0;

    // [PRODUCT] 캔버스 스냅 기본 임계값은 8 픽셀이다.
    public const double DefaultSnapThresholdPixels = 8.0;

    // [PRODUCT] 편집기 기본 세이프 에어리어는 각 변에서 5 퍼센트다.
    public const double DefaultSafeAreaPercent = 5.0;

    // [PRODUCT] 실행 취소와 재실행 기록은 최대 200개까지 유지한다.
    public const int MaximumUndoDepth = 200;

    // [PRODUCT] 정규식을 만들기 전에 검색 패턴 길이를 제한한다.
    public const int MaximumSearchPatternLength = 4096;

    // [PRODUCT] 정규식 검색에 유한 타임아웃을 두어 편집기 반응성을 지킨다.
    public const int SearchRegexTimeoutMilliseconds = 250;

    // [UPSTREAM] t=0 은 안드로이드에서 신뢰할 수 없어 1 ms 로 보정한다.
    public const long MinimumCueStartMilliseconds = 1;

    // [UPSTREAM] 직렬화된 YTT 크기는 75% 미만을 표현할 수 없다.
    public const int MinimumFontSizePercent = 75;

    // [PRODUCT] 200% 는 권장 UX 상한이지 포맷 상한이 아니다.
    public const int RecommendedFontSizePercent = 200;

    // [UPSTREAM] 브라우저 한계는 압축 기준 초당 10240 비트다.
    public const double UpstreamCompressedBitsPerSecondLimit = 10240.0;

    // [PRODUCT] 추정치가 근사이므로 upstream 한계의 70% 지점에서 미리 경고한다.
    public const double SizeRiskSafetyMargin = 0.70;

    // [PRODUCT] 0.70 × 10240 = 7168 bit/s.
    public const double SizeRiskBitsPerSecondThreshold =
        UpstreamCompressedBitsPerSecondLimit * SizeRiskSafetyMargin;

    // [PRODUCT] 이 값보다 어두운 휘도는 어두운 텍스트로 간주한다.
    // 측정된 렌더 지표가 없을 때 쓰는 보수적인 가독성 휴리스틱이다.
    public const double DarkTextLuminanceThreshold = 0.25;
}
