<div align="center">

<img src="docs/assets/logo.png" width="112" alt="yttStudio" />

# yttStudio

**YouTube YTT (SRV3) 字幕専用の WYSIWYG エディタ**

映像の上で直接配置・装飾して `.ytt` を書き出すデスクトップアプリ

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Tests](https://img.shields.io/badge/tests-361%20passing-brightgreen)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/bd1f9a95330940aeab504f29f2e57d1a)](https://app.codacy.com/gh/DO0OG/yttStudio/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

[한국어](README.md) · [English](README.en.md) · **日本語**

</div>

---

YouTube 標準の字幕エディタには装飾機能がありません。しかしプレイヤー自体は
**YTT (YouTube Timed Text、内部名称 SRV3)** という XML 形式を通じて、色・縁取り・グロー・
位置指定・カラオケタイミング・ルビ・縦書きに対応しています。既存のエコシステムには
**コンバータ**はあっても、**映像の上で直接配置できるエディタ**がありませんでした。
yttStudio はその空白を埋めます。

<div align="center">
<img src="docs/render-comparison/m2-canvas.png" width="880" alt="編集キャンバス" />
</div>

<div align="center"><sub>
プレビュー上で字幕をその場で編集します。左はスタイルプリセット、右はプロパティ
パネル、下はトラックタイムラインと字幕一覧です。
</sub></div>

## 主な機能

| | |
|---|---|
| **映像上での編集** | 再生しながら字幕をドラッグ・複数選択・スナップ整列し、その場でテキストを直します |
| **YouTube アドレスで開く** | アドレスを貼るだけでダウンロードせずストリーミングでプレビューします |
| **タイムライン** | 拡大・左右パン、ブロック移動と端のトリム。パネル幅は境界をドラッグして調整 |
| **形式ルールの強制** | 座標変換・フォント倍率の圧縮・不透明度の上限など YTT の制約をコードで検査 |
| **カラオケ** | 音節の自動分割 (ハングル・かな・ラテン・漢字)、再生中のタップ入力、5 種類の進行方式 |
| **エフェクト** | 移動・フェード・揺れ・色収差・アニメーション。揺れは決定的なので巻き戻しても同じ結果 |
| **検証** | YouTube の制約 17 ルールの検査と、取り消せる自動修正 |
| **相互変換** | `.ytt` / `.srv3` / `.ass` の読み込みと書き出し。欠落は警告で明示 |
| **プロジェクト** | `.yttproj` パッケージ、間隔を選べる自動保存、異常終了からの復旧 |
| **ビューポートモード** | 通常・シアター・全画面・モバイル。YouTube プレイヤーの実測値に基づきます |
| **環境設定** | 言語・テーマ・libmpv パス・自動保存をウィンドウで設定し、次回起動まで保持します |
| **3 言語** | 한국어 · English · 日本語 をリアルタイムで切り替え |

`Space` で再生と一時停止を切り替えます。`.ytt` / `.srv3` / `.ass` 字幕と
`.mp4` / `.mkv` / `.webm` / `.mov` / `.avi` / `.m4v` 映像をウィンドウにドロップして開けます。

操作方法の全体は[ユーザーガイド](docs/USER_GUIDE.md)にあります。

## 最近の変更

| | |
|---|---|
| **YouTube アドレスでプレビュー** | アドレスを入れるとストリーミング再生し、その上で字幕を編集できます。アドレスの解析に `yt-dlp` が必要です |
| **再生ショートカットの整理** | ウィンドウのどこにフォーカスがあっても `Space` が再生・一時停止として働きます。コマ送りボタンは `⏮` `⏭` に変えました |
| **ビューポートモード** | YouTube プレイヤーを実測し、通常・シアター・全画面・モバイルの比率を再現します |
| **インストーラの配布** | Windows インストーラ、macOS `.dmg`、Linux AppImage |
| **環境設定ウィンドウとテーマ** | ライト・ダーク・システム既定を再起動なしで切り替えます |

## はじめに

[最新リリース](https://github.com/DO0OG/yttStudio/releases/latest)からダウンロードしてください。
.NET ランタイムを同梱しているため、別途インストールは不要です。

| プラットフォーム | ファイル | インストール方法 |
|---|---|---|
| Windows | `yttStudio-v*-win-x64-setup.exe` | 実行するとスタートメニュー登録・アンインストーラ・ファイル関連付けまで処理します |
| Windows (インストール不要) | `yttStudio-v*-win-x64.zip` | 展開して `YttStudio.App.exe` を実行 |
| macOS (Apple Silicon) | `yttStudio-v*-osx-arm64.dmg` | 開いて `yttStudio.app` を `Applications` にドラッグ |
| Linux | `yttStudio-v*-linux-x86_64.AppImage` | `chmod +x` して実行 |

> **コード署名のない配布物です。** 証明書がないため初回起動時に警告が出ます。
> Windows は SmartScreen の警告で**詳細情報 → 実行**、macOS は `yttStudio.app` を
> 右クリックして**開く**を選んでください。

### 任意の依存関係

| 項目 | 必要になる場面 |
|---|---|
| libmpv 2.0 以上 | **映像再生。** なくても字幕の編集・検証・保存は正常に動作します |
| yt-dlp | **YouTube アドレスで開く場合。** アプリのディレクトリと `PATH` から探します |

libmpv は**ツール → 設定 → 映像**タブでパスを選ぶか、Windows ではその場でダウンロード
できます。探索順序とライセンスの事情は[依存関係の文書](docs/DEPENDENCIES.md)にあります。

### ソースからのビルド

.NET 10 SDK が必要です。

```bash
git clone --recursive https://github.com/DO0OG/yttStudio.git
cd yttStudio
dotnet build -c Release
dotnet run --project src/YttStudio.App
```

字幕ファイルを引数に渡すとそのまま開きます。

```bash
dotnet run --project src/YttStudio.App -- samples/showcase.ass
```

## 知っておくとよいこと

- **プレビューは編集用の近似です。** YouTube の実際のレンダラは DOM/CSS ベースのため、
  グローの半径や改行位置がわずかに異なります。最終確認は実際のアップロードで
  行ってください。
- **作業ファイルは `.yttproj` で保存してください。** `.ytt` はエフェクトがキーフレームに
  展開された結果なので、読み戻してもエフェクトには復元されません。
- **回転と自由なスケールハンドルがないのは意図した設計です。** YTT 形式に回転も任意の
  ボックススケールもありません。リサイズハンドルはドラッグを `SizePercent` の
  フォント倍率に変換します。
- **全画面とモバイル縦のビューポートはまだ実測できていません。** 通常モードの比例式を
  そのまま使っています。測定記録は[ビューポートモード](docs/viewport-modes.md)にあります。

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [ユーザーガイド](docs/USER_GUIDE.md) | 機能ごとの使い方と制約 |
| [依存関係](docs/DEPENDENCIES.md) | 固定バージョン、配布方針、ローカルパッチ |
| [性能](docs/PERFORMANCE.md) | 解像度ごとの実測値とバックエンド選定の根拠 |
| [形式の検証記録](docs/YTT-VERIFICATION.md) | YTT ルールの根拠と確度の等級 |
| [手動 QA](docs/MANUAL_QA.md) | 自動化できない検証項目 |
| [サードパーティ告知](docs/THIRD-PARTY-NOTICES.md) | 同梱フォントとライブラリ |

## 技術スタック

.NET 10 · C# 14 · Avalonia 12 · SkiaSharp · libmpv · xUnit

字幕形式の入出力は [YTSubConverter](https://github.com/arcusmaximus/YTSubConverter) (MIT)
を使用しています。`ap` と `ju` を独立して扱うためのローカルパッチが 1 つ当たっています。

## ライセンス

[LICENSE](LICENSE) を参照してください。同梱フォントと外部ライブラリの告知は
[サードパーティ告知](docs/THIRD-PARTY-NOTICES.md)にあります。

## 貢献者

- [DO0OG](https://github.com/DO0OG)
