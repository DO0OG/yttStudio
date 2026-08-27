namespace YttStudio.App;

/// <summary>편집기가 제공하는 언어다.</summary>
public enum AppLanguage
{
    Korean,
    English,
    Japanese,
}

/// <summary>제공하는 모든 언어의 문자열 하나다.</summary>
public sealed record LocalizedText(string Korean, string English, string Japanese)
{
    /// <summary><paramref name="language"/> 에 해당하는 문자열을 가져온다.</summary>
    public string For(AppLanguage language) => language switch
    {
        AppLanguage.English => English,
        AppLanguage.Japanese => Japanese,
        _ => Korean,
    };
}

/// <summary>
/// 메모리 문자열 테이블이다. .resx 대신 코드로 두어 누락된 키가 평범한
/// 딕셔너리 조회로 드러나고 세 언어가 한 곳에 나란히 놓인다.
/// </summary>
public sealed class Localizer : System.ComponentModel.INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, LocalizedText> Table =
        new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
    {
        ["OpenSubtitle"] = new("자막 열기", "Open Subtitle", "字幕を開く"),
        ["OpenVideo"] = new("영상 열기", "Open Video", "動画を開く"),
        ["SaveYtt"] = new("YTT 저장", "Save YTT", "YTT を保存"),
        ["Undo"] = new("Undo", "Undo", "元に戻す"),
        ["Redo"] = new("Redo", "Redo", "やり直す"),
        ["CheckerboardBg"] = new("체커보드 배경", "Checkerboard Background", "チェッカーボード背景"),
        ["ViewportVideo"] = new("영상", "Video", "動画"),
        ["ViewportYouTube"] = new("YouTube 일반", "YouTube Normal", "YouTube 通常"),
        ["ViewportTheater"] = new("극장", "Theater", "シアター"),
        ["ViewportFullscreen"] = new("전체화면", "Fullscreen", "全画面"),
        ["ViewportMobile"] = new("모바일", "Mobile", "モバイル"),
        ["ViewportPending"] = new("측정 대기 중", "Awaiting measurement", "計測待ち"),
        ["ViewportModeNote"] = new("자막 공간은 플레이어 전체", "Subtitle space is the full player", "字幕空間はプレーヤー全体"),
        ["PlaybackQuality"] = new("재생 화질", "Playback quality", "再生画質"),
        ["PlaybackQualityHint"] = new(
            "재생 부하를 줄이려고 영상을 낮은 해상도로 받습니다. 내보내는 자막에는 영향이 없습니다.",
            "Decodes the video at a lower resolution to lighten playback. Exported subtitles are unaffected.",
            "再生負荷を下げるため映像を低い解像度で受け取ります。書き出す字幕には影響しません。"),
        ["StylePresets"] = new("스타일 프리셋", "Style Presets", "スタイルプリセット"),
        ["StyleName"] = new("스타일 이름", "Style name", "スタイル名"),
        ["Add"] = new("+ 추가", "+ Add", "+ 追加"),
        ["Rename"] = new("이름 변경", "Rename", "名前変更"),
        ["Delete"] = new("삭제", "Delete", "削除"),
        ["SaveCueFormat"] = new("선택 큐 형식 저장", "Save Selected Cue Format", "選択キューの書式を保存"),
        ["ApplySelectedStyle"] = new("선택 스타일 적용", "Apply Selected Style", "選択スタイルを適用"),
        ["StyleListHint"] = new("스타일 목록 선택과 큐 적용은 분리되어 있습니다.", "Selecting a style in the list is separate from applying it to cues.", "一覧での選択とキューへの適用は別操作です。"),
        ["Properties"] = new("속성", "Properties", "プロパティ"),
        ["PositionAndAlign"] = new("위치 & 정렬", "Position & Alignment", "位置と整列"),
        ["AnchorPoint"] = new("앵커 포인트 (ap)", "Anchor Point (ap)", "アンカーポイント (ap)"),
        ["BoxJustify"] = new("박스 내부 정렬 (ju)", "Box Justification (ju)", "ボックス内整列 (ju)"),
        ["MultilineOnly"] = new("여러 줄일 때 적용됨", "Applies to multi-line cues", "複数行のときに適用"),
        ["AlignLeft"] = new("왼쪽 맞춤", "Align Left", "左揃え"),
        ["AlignCenter"] = new("가운데 맞춤", "Align Center", "中央揃え"),
        ["AlignRight"] = new("오른쪽 맞춤", "Align Right", "右揃え"),
        ["AlignTop"] = new("위쪽 맞춤", "Align Top", "上揃え"),
        ["AlignMiddle"] = new("세로 가운데 맞춤", "Align Middle", "上下中央揃え"),
        ["AlignBottom"] = new("아래쪽 맞춤", "Align Bottom", "下揃え"),
        ["DistributeH"] = new("가로 균등", "Distribute H", "水平等間隔"),
        ["DistributeV"] = new("세로 균등", "Distribute V", "垂直等間隔"),
        ["DistributeHFull"] = new("가로 균등 분배", "Distribute Horizontally", "水平方向に等間隔"),
        ["DistributeVFull"] = new("세로 균등 분배", "Distribute Vertically", "垂直方向に等間隔"),
        ["AlignHint"] = new("정렬·스타일은 속성 패널에서도 적용할 수 있습니다.", "Alignment and styles can also be applied from the properties panel.", "整列とスタイルはプロパティパネルからも適用できます。"),
        ["Left"] = new("좌", "L", "左"),
        ["Center"] = new("중", "C", "中"),
        ["Right"] = new("우", "R", "右"),
        ["Top"] = new("상", "T", "上"),
        ["Bottom"] = new("하", "B", "下"),
        ["TextSection"] = new("텍스트", "Text", "テキスト"),
        ["TextEditHint"] = new("텍스트 (더블클릭 편집 대상)", "Text (double-click to edit)", "テキスト（ダブルクリックで編集）"),
        ["Style"] = new("스타일", "Style", "スタイル"),
        ["Font"] = new("폰트 · YTT 고정 8종", "Font - 8 fixed YTT faces", "フォント・YTT 固定 8 種"),
        ["SizePercent"] = new("크기 % (하한 75, 슬라이더 권장 상한 200)", "Size % (min 75, slider max 200)", "サイズ %（下限 75・スライダー上限 200）"),
        ["Foreground"] = new("전경", "Foreground", "前景"),
        ["ForegroundOpacity"] = new("전경 불투명도", "Foreground Opacity", "前景の不透明度"),
        ["Background"] = new("배경", "Background", "背景"),
        ["BackgroundOpacity"] = new("배경 불투명도", "Background Opacity", "背景の不透明度"),
        ["EdgeShadow"] = new("엣지 / 그림자", "Edge / Shadow", "エッジ / 影"),
        ["TextDirection"] = new("문자 방향", "Text Direction", "文字方向"),
        ["CharOffset"] = new("문자 오프셋", "Character Offset", "文字オフセット"),
        ["Pack"] = new("Pack", "Pack", "Pack"),
        ["CompatPcNote"] = new("호환성: PC 중심 · 검증일 2026-08-25", "Compatibility: PC-focused - verified 2026-08-25", "互換性: PC 中心・検証日 2026-08-25"),
        ["CompatSpecNote"] = new("플랫폼 호환성 관찰 기준, 2026-08-25 검증", "Platform compatibility observation, verified 2026-08-25", "プラットフォーム互換性の観察基準・2026-08-25 検証"),
        ["Effects"] = new("효과", "Effects", "エフェクト"),
        ["EffectMove"] = new("이동 · 시작점에서 끝점까지 선형 보간", "Move - linear interpolation from start to end", "移動・始点から終点まで線形補間"),
        ["EffectFade"] = new("페이드 · 시작/종료 알파", "Fade - start/end alpha", "フェード・開始/終了アルファ"),
        ["EffectShake"] = new("흔들림 · cueId + frameIndex 결정적 시드", "Shake - deterministic cueId + frameIndex seed", "シェイク・cueId + frameIndex の決定的シード"),
        ["EffectChroma"] = new("색수차 · RGB 복제본 수렴/발산", "Chroma - RGB copies converge/diverge", "色収差・RGB 複製の収束/発散"),
        ["EffectAnimate"] = new("애니메이션 · pow(progress, accel)", "Animate - pow(progress, accel)", "アニメーション・pow(progress, accel)"),
        ["EffectDefaultNote"] = new("기본 파라미터로 추가됩니다. 가라오케 편집은 M4 범위입니다.", "Added with default parameters. Karaoke editing is M4 scope.", "既定パラメータで追加されます。カラオケ編集は M4 の範囲です。"),
        ["NoRotation"] = new("회전 — YTT 포맷 미지원", "Rotation - not supported by YTT", "回転 — YTT 形式は非対応"),
        ["NoFreeScale"] = new("자유 스케일 — YTT 포맷 미지원", "Free scale - not supported by YTT", "自由スケール — YTT 形式は非対応"),
        ["ValidationIssues"] = new("검증 문제", "Validation Issues", "検証の問題"),
        ["RunCheck"] = new("검사 실행", "Run Check", "検査を実行"),
        ["AutoFix"] = new("자동 수정", "Auto Fix", "自動修正"),
        ["GoToCue"] = new("큐로 이동", "Go to Cue", "キューへ移動"),
        ["W101Note"] = new("W101은 실제 JSON3와 다른 gzip XML 근사치입니다. 업로드 후 확인하세요.", "W101 is a gzip XML approximation, not the real JSON3 metric. Verify after upload.", "W101 は実際の JSON3 とは異なる gzip XML の近似値です。アップロード後に確認してください。"),
        ["Karaoke"] = new("가라오케 편집", "Karaoke Editing", "カラオケ編集"),
        ["AutoSplit"] = new("자동 음절 분할", "Auto Split Syllables", "自動音節分割"),
        ["Split"] = new("분할", "Split", "分割"),
        ["Merge"] = new("병합", "Merge", "結合"),
        ["Apply"] = new("적용", "Apply", "適用"),
        ["KaraokeTapHint"] = new("재생 속도 0.5x 권장 · 이 영역에서 Space=탭 입력, Backspace=직전 탭 취소", "0.5x playback recommended - here Space taps, Backspace undoes the last tap", "再生速度 0.5x 推奨・この領域では Space でタップ、Backspace で直前のタップを取消"),
        ["WaveformUnavailable"] = new("오디오 샘플 경로가 없어 파형을 표시하지 않습니다. 가짜 파형은 생성하지 않습니다.", "No audio sample source, so no waveform is drawn. A fake waveform is never generated.", "オーディオサンプルが無いため波形は表示しません。偽の波形は生成しません。"),
        ["TimelineHint"] = new("타임라인 · Ctrl/Alt+휠 확대 · Shift+휠 좌우 이동 · 가운데 드래그 팬 · 블록 이동 / 끝 트림", "Timeline - Ctrl/Alt+Wheel zoom - Shift+Wheel scroll - middle-drag pan - drag blocks or trim edges", "タイムライン・Ctrl/Alt+ホイールで拡大・Shift+ホイールで左右移動・中ボタンドラッグでパン・本体移動 / 端トリム"),
        ["SeekFailed"] = new("시크 실패", "Seek failed", "シーク失敗"),
        ["Volume"] = new("볼륨", "Volume", "音量"),
        ["Mute"] = new("음소거", "Mute", "ミュート"),
        ["Unmute"] = new("음소거 해제", "Unmute", "ミュート解除"),
        ["Play"] = new("재생", "Play", "再生"),
        ["Pause"] = new("일시정지", "Pause", "一時停止"),
        ["Track"] = new("Track", "Track", "トラック"),
        ["StartMs"] = new("시작(ms)", "Start (ms)", "開始 (ms)"),
        ["EndMs"] = new("끝(ms)", "End (ms)", "終了 (ms)"),
        ["Duration"] = new("길이", "Duration", "長さ"),
        ["Duplicate"] = new("복제", "Duplicate", "複製"),
        ["BringToFront"] = new("맨 앞으로", "Bring to Front", "最前面へ"),
        ["SendToBack"] = new("맨 뒤로", "Send to Back", "最背面へ"),
        ["AddCueAtTime"] = new("+ 현재 시각에 큐", "+ Cue at Current Time", "+ 現在位置にキュー"),
        ["Alignment"] = new("정렬", "Alignment", "整列"),
        ["OpenProject"] = new("프로젝트 열기", "Open Project", "プロジェクトを開く"),
        ["SaveProject"] = new("프로젝트 저장", "Save Project", "プロジェクトを保存"),
        ["Search"] = new("검색", "Search", "検索"),
        ["SearchReplace"] = new("검색 / 치환", "Search / Replace", "検索 / 置換"),
        ["FindWhat"] = new("찾을 내용", "Find what", "検索する文字列"),
        ["ReplaceWith"] = new("바꿀 내용", "Replace with", "置換後の文字列"),
        ["UseRegex"] = new("정규식", "Regular expression", "正規表現"),
        ["MatchCase"] = new("대소문자 구분", "Match case", "大文字と小文字を区別"),
        ["ReplaceAll"] = new("모두 치환", "Replace All", "すべて置換"),
        ["TimeShift"] = new("시간 이동", "Time Shift", "時間シフト"),
        ["ShiftMs"] = new("이동량(ms)", "Shift (ms)", "シフト量 (ms)"),
        ["ShiftSelected"] = new("선택 큐 이동", "Shift Selected", "選択キューをシフト"),
        ["ShiftAll"] = new("전체 이동", "Shift All", "すべてシフト"),
        ["Settings"] = new("설정", "Settings", "設定"),
        ["SettingsGeneralTab"] = new("일반", "General", "一般"),
        ["SettingsAppearanceTab"] = new("모양", "Appearance", "外観"),
        ["SettingsVideoTab"] = new("영상", "Video", "動画"),
        ["ThemeLabel"] = new("테마", "Theme", "テーマ"),
        ["ThemeDefault"] = new("시스템 기본", "System default", "システムの既定"),
        ["ThemeLight"] = new("밝은 테마", "Light", "ライト"),
        ["ThemeDark"] = new("어두운 테마", "Dark", "ダーク"),
        ["MpvPathLabel"] = new("libmpv 경로", "libmpv path", "libmpv のパス"),
        ["MpvPathPlaceholder"] = new("파일 또는 폴더 경로", "Library file or folder", "ライブラリファイルまたはフォルダー"),
        ["MpvBrowse"] = new("찾아보기", "Browse", "参照"),
        ["MpvApply"] = new("적용 및 다시 찾기", "Apply and rescan", "適用して再検索"),
        ["MpvGuide"] = new("설치 안내 열기", "Open installation guide", "インストール案内を開く"),
        ["MpvAutoInstall"] = new("자동 설치", "Install automatically", "自動インストール"),
        ["MpvInstallPreparing"] = new("libmpv 설치 준비 중", "Preparing libmpv installation", "libmpv のインストールを準備中"),
        ["MpvAutoInstallFailed"] = new("libmpv 자동 설치에 실패했습니다. 공식 안내에서 수동 설치를 확인하세요.", "libmpv automatic installation failed. Review the official guide for manual installation.", "libmpv の自動インストールに失敗しました。公式案内で手動インストールを確認してください。"),
        ["MpvStageFetchingRelease"] = new("최신 릴리스 정보 가져오는 중", "Fetching the latest release", "最新リリースを取得中"),
        ["MpvStageDownloadingArchive"] = new("libmpv 아카이브 다운로드 중", "Downloading the libmpv archive", "libmpv アーカイブをダウンロード中"),
        ["MpvStageExtractingArchive"] = new("libmpv 아카이브 압축 해제 중", "Extracting the libmpv archive", "libmpv アーカイブを展開中"),
        ["MpvStageInstalling"] = new("libmpv 설치 중", "Installing libmpv", "libmpv をインストール中"),
        ["MpvStageCompleted"] = new("libmpv 설치 완료", "libmpv installation completed", "libmpv のインストール完了"),
        ["MpvAutoInstallCanceled"] = new("libmpv 자동 설치가 취소되었습니다.", "The libmpv installation was canceled.", "libmpv の自動インストールをキャンセルしました。"),
        ["MpvAutoInstallUnavailable"] = new("현재 플랫폼에서는 자동 설치를 지원하지 않습니다. 공식 안내를 확인하세요.", "Automatic installation is unavailable on this platform. Review the official guide.", "このプラットフォームでは自動インストールを利用できません。公式案内を確認してください。"),
        ["MpvPackageManagerLabel"] = new("패키지 매니저 명령", "Package manager commands", "パッケージマネージャーのコマンド"),
        ["MpvStatusLabel"] = new("상태", "Status", "状態"),
        ["MpvLicenseLabel"] = new("라이선스", "License", "ライセンス"),
        ["MpvLicenseNote"] = new("공식 Windows mpv 빌드는 GPLv2+입니다. YttStudio는 libmpv 바이너리를 배포하지 않으며, 출처와 라이선스를 확인한 뒤 사용자 컴퓨터에 설치하세요.", "Official Windows mpv builds are GPLv2+. YttStudio does not distribute libmpv binaries; review the source and license before installing locally.", "公式 Windows mpv ビルドは GPLv2+ です。YttStudio は libmpv バイナリを配布しないため、出典とライセンスを確認してローカルにインストールしてください。"),
        ["MpvInstallSource"] = new("공식 mpv 배포 안내", "Official mpv distributions", "公式 mpv 配布物"),
        ["MpvInstallNote"] = new(
            "Windows x64에서는 자동 설치 버튼으로 공식 mpv 빌드를 사용자 로컬에 설치할 수 있습니다. 그 외 플랫폼은 패키지 매니저를 사용하거나 호환 파일/폴더를 직접 선택하세요.",
            "On Windows x64, the automatic install button places an official mpv build in your local profile. On other platforms, use a package manager or select a compatible file or folder.",
            "Windows x64 では自動インストールボタンで公式 mpv ビルドをローカルに配置できます。その他のプラットフォームではパッケージマネージャーを使うか、互換ファイルまたはフォルダーを選択してください。"),
        ["MpvPathInvalid"] = new("libmpv 경로를 찾을 수 없습니다.", "The libmpv path does not exist.", "libmpv のパスが見つかりません。"),
        ["MpvReloaded"] = new("libmpv를 다시 찾았습니다.", "libmpv was rescanned.", "libmpv を再検索しました。"),
        ["MpvReloadFailed"] = new("libmpv를 찾지 못했습니다. 선택한 빌드와 경로를 확인하세요.", "libmpv was not found. Check the selected build and path.", "libmpv が見つかりません。ビルドとパスを確認してください。"),
        ["LanguageLabel"] = new("언어", "Language", "言語"),
        ["LanguageKorean"] = new("한국어", "Korean", "韓国語"),
        ["LanguageEnglish"] = new("English", "English", "英語"),
        ["LanguageJapanese"] = new("日本語", "Japanese", "日本語"),
        ["SettingsRestartNote"] = new("변경 사항은 즉시 적용되며 다음 실행에도 유지됩니다.", "Changes apply immediately and persist for the next launch.", "変更はすぐに適用され、次回起動にも保存されます。"),
        ["SnapThreshold"] = new("스냅 임계값 (px)", "Snap threshold (px)", "スナップしきい値 (px)"),
        ["AutosaveEnabled"] = new("자동 저장 사용", "Enable autosave", "自動保存を使用"),
        ["AutosaveInterval"] = new("자동 저장 간격", "Autosave interval", "自動保存の間隔"),
        ["Autosave15Seconds"] = new("15초", "15 seconds", "15秒"),
        ["Autosave30Seconds"] = new("30초", "30 seconds", "30秒"),
        ["Autosave1Minute"] = new("1분", "1 minute", "1分"),
        ["Autosave2Minutes"] = new("2분", "2 minutes", "2分"),
        ["Autosave5Minutes"] = new("5분", "5 minutes", "5分"),
        ["Autosave10Minutes"] = new("10분", "10 minutes", "10分"),
        ["Ruby"] = new("루비", "Ruby", "ルビ"),
        ["RubyRole"] = new("루비 역할", "Ruby role", "ルビの役割"),
        ["RubyText"] = new("루비 텍스트", "Ruby text", "ルビテキスト"),
        ["PcOnlyBadge"] = new("PC 전용", "PC only", "PC 専用"),
        ["RecoveryTitle"] = new("복구", "Recovery", "復元"),
        ["RecoveryPrompt"] = new("비정상 종료로 저장되지 않은 작업이 남아 있습니다. 복구할까요?", "Unsaved work from an unclean shutdown was found. Recover it?", "異常終了により未保存の作業が残っています。復元しますか？"),
        ["Recover"] = new("복구", "Recover", "復元"),
        ["VideoMissingTitle"] = new("영상을 찾을 수 없음", "Video not found", "動画が見つかりません"),
        ["VideoMissingPrompt"] = new("프로젝트에 기록된 영상 경로를 찾을 수 없습니다. 다시 찾으시겠습니까?", "The video path stored in the project could not be found. Locate it again?", "プロジェクトに記録された動画パスが見つかりません。再指定しますか？"),
        ["Relink"] = new("다시 찾기", "Locate", "再指定"),
        ["MenuFile"] = new("파일(_F)", "_File", "ファイル(_F)"),
        ["MenuEdit"] = new("편집(_E)", "_Edit", "編集(_E)"),
        ["MenuView"] = new("보기(_V)", "_View", "表示(_V)"),
        ["MenuSubtitle"] = new("자막(_S)", "_Subtitle", "字幕(_S)"),
        ["MenuTools"] = new("도구(_T)", "_Tools", "ツール(_T)"),
        ["MenuHelp"] = new("도움말(_H)", "_Help", "ヘルプ(_H)"),
        ["MenuExit"] = new("끝내기", "Exit", "終了"),
        ["MenuAbout"] = new("정보", "About", "情報"),
        ["AboutVersion"] = new("버전", "Version", "バージョン"),
        ["MenuUserGuide"] = new("사용자 가이드", "User Guide", "ユーザーガイド"),
        ["MenuSafeArea"] = new("세이프 에어리어 표시", "Show Safe Area", "セーフエリアを表示"),
        ["MenuAnchors"] = new("앵커 마커 표시", "Show Anchor Markers", "アンカーマーカーを表示"),
        ["AboutBody"] = new("YTT 자막 전용 WYSIWYG 편집기입니다. 영상 위에서 직접 배치하고 스타일링해 .ytt 를 출력합니다.", "A WYSIWYG editor for YouTube timed text. Place and style subtitles directly over the video and export .ytt.", "YouTube タイムドテキスト専用の WYSIWYG エディタです。動画上で直接配置してスタイルを整え .ytt を書き出します。"),
        ["Close"] = new("닫기", "Close", "閉じる"),
    };

    private AppLanguage language = AppLanguage.Korean;

    /// <summary><see cref="Language"/> 가 바뀔 때 발생해 뷰가 모든 바인딩을 다시 읽게 한다.</summary>
    public event Action? LanguageChanged;

    /// <summary>
    /// <c>Item[]</c> 인덱서 이름으로 발생한다. 인덱서 바인딩은
    /// 소유자가 인덱서를 무효화할 때만 다시 평가되므로 언어 전환이 여기서 알려야 한다.
    /// </summary>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>활성 언어를 가져오거나 설정한다.</summary>
    public AppLanguage Language
    {
        get => language;
        set
        {
            if (language == value)
            {
                return;
            }

            language = value;
            PropertyChanged?.Invoke(
                this,
                new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// 활성 언어에서 <paramref name="key"/> 를 찾는다. 모르는 키는
    /// 키 자체를 돌려주어 누락 항목이 빈 라벨 대신 UI 에 드러나게 한다.
    /// </summary>
    public string this[string key] =>
        Table.TryGetValue(key, out LocalizedText? text) ? text.For(language) : key;

    /// <summary>테이블의 모든 키를 가져온다. 테스트가 전수 확인에 사용한다.</summary>
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Table.Keys;

    /// <summary><paramref name="key"/> 항목을 가져온다. 없으면 null 이다.</summary>
    public static LocalizedText? Find(string key) =>
        Table.TryGetValue(key, out LocalizedText? text) ? text : null;
}
