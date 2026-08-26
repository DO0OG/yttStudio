<div align="center">

<img src="docs/assets/logo.png" width="112" alt="YttStudio" />

# YttStudio

**YouTube の YTT (SRV3) 字幕専用 WYSIWYG エディタ**

映像の上で直接配置・装飾して `.ytt` を書き出すデスクトップアプリ

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Tests](https://img.shields.io/badge/tests-150%20passing-brightgreen)

[한국어](README.md) · [English](README.en.md) · **日本語**

</div>

---

## 開発の動機

YouTube 標準の字幕エディタには装飾機能がありません。しかしプレイヤー自体は
**YTT (YouTube Timed Text、内部名称 SRV3)** という XML 形式を通じて、色・縁取り・グロー・
位置指定・カラオケタイミング・ルビ・縦書きに対応しています。MV やカバー曲で見かける
「凝った字幕」はこの形式で作られています。

既存のエコシステムには**コンバータ**はあっても、**映像の上で直接配置できるエディタ**が
ありませんでした。YttStudio はその空白を埋めます。

## スクリーンショット

<div align="center">
<img src="docs/render-comparison/m2-canvas.png" width="880" alt="編集キャンバス" />
</div>

<div align="center"><sub>
映像に字幕を合成し、マウスで配置します。左がスタイルプリセット、右がプロパティパネル、
下がトラックタイムラインと字幕リストです。
</sub></div>

<br />

<table>
<tr>
<td width="50%"><img src="docs/render-comparison/m1-render.png" alt="レンダリングパイプライン" /></td>
<td width="50%"><img src="docs/render-comparison/m3-effects.png" alt="エフェクト" /></td>
</tr>
<tr>
<td align="center"><sub>YTT の規則に従った字幕描画</sub></td>
<td align="center"><sub>移動・フェード・シェイク・色収差</sub></td>
</tr>
</table>

## 主な機能

| | |
|---|---|
| **映像上での編集** | libmpv で再生しながら字幕をドラッグ・複数選択・スナップ整列 |
| **再生とファイルドロップ** | 動画を開いた後に `Space` で再生/一時停止を切り替え、対応する字幕・動画ファイルをウィンドウへドロップして開けます |
| **タイムライン** | 拡大・左右のパン・スクロールバーのドラッグ、ブロックの移動と端のトリム。パネルは境界をドラッグしてサイズ変更 |
| **形式の制約を強制** | 座標変換、フォント倍率のクランプ、不透明度の上限などをコード側で検査 |
| **カラオケ** | 音節の自動分割 (ハングル・かな・ラテン・漢字)、再生中のタップ入力、5 種類の進行方式 |
| **エフェクト** | 移動・フェード・シェイク・色収差・アニメーション。シェイクは決定的なので巻き戻しても同じ結果 |
| **バリデータ** | YouTube の制約 17 規則を検査し、取り消し可能な自動修正を提供 |
| **相互変換** | `.ytt` / `.srv3` / `.ass` の読み書き。非可逆な変換は警告として明示 |
| **プロジェクト** | `.yttproj` パッケージ、間隔を変更できる自動保存、異常終了からの復旧 |
| **環境設定** | 言語・テーマ・libmpv のパス・自動保存をウィンドウで設定し、次回起動にも引き継ぎ |
| **テーマ** | システム既定・ライト・ダーク。再起動なしで切り替え |
| **3 言語対応** | 한국어 · English · 日本語 を実行中に切り替え |

ファイルドロップは字幕 `.ytt` / `.srv3` / `.ass` と動画 `.mp4` / `.mkv` / `.webm` / `.mov` / `.avi` / `.m4v` に対応します。`.yttproj` はプロジェクトを開く操作を使ってください。

## はじめかた

### 必要環境

| 項目 | 備考 |
|---|---|
| .NET 10 SDK | ビルドに必要 |
| libmpv 2.0 以上 | **映像再生のみに必要。** なくても字幕の編集・検証・保存は動作します |

### ビルドと実行

```bash
git clone --recursive https://github.com/DO0OG/YttStudio.git
cd YttStudio
dotnet build -c Release
dotnet run --project src/YttStudio.App
```

字幕ファイルを引数に渡すとそのまま開きます。

```bash
dotnet run --project src/YttStudio.App -- samples/showcase.ass
```

### libmpv の指定

いちばん簡単なのは **ツール → 設定 → 映像** タブです。パスを自分で選ぶか、Windows では
ダウンロードして導入できます。設定したパスは次回の起動にも引き継がれます。

環境変数でも指定できます。探索順は設定に保存されたパス →
`YTTSTUDIO_MPV_PATH` → 実行ディレクトリ → OS の標準パスです。

```bash
# Windows
set YTTSTUDIO_MPV_PATH=C:\path\to\libmpv-2.dll

# macOS / Linux
export YTTSTUDIO_MPV_PATH=/usr/lib/libmpv.so.2
```

2.0 未満のバージョンは拒否します。見つからない場合は映像機能のみを無効化し、
単色またはチェッカーボードの背景にフォールバックします。

> **ライセンスについて。** 公式の Windows 向け mpv ビルドは GPLv2+ です。そのため YttStudio は
> libmpv のバイナリを配布物に含めません。設定ウィンドウの自動インストールは、利用者が出典と
> ライセンスを確認してボタンを押したときにのみ実行され、利用者自身の環境に導入します。
> macOS と Linux では自動インストールの代わりにパッケージマネージャのコマンドを案内します。

## 知っておくとよいこと

**プレビューは編集用の近似です。** YouTube の実際のレンダラは DOM/CSS ベースのため、
グローの半径や改行位置がわずかに異なります。最終確認は実際のアップロードで行ってください。

**作業ファイルは `.yttproj` で保存してください。** `.ytt` はエフェクトがキーフレームに
展開された出力なので、読み戻してもエフェクトには復元されず、トラックや描画順も保持されません。

**回転とサイズ変更のハンドルがないのは意図的な設計です。** YTT 形式に回転と自由な
スケールが存在しないためです。用意すると画面上では変形するのに書き出し結果に反映されず、
かえって混乱を招きます。

**ビューポートモード (通常・シアター・全画面・モバイル) は無効です。** 各モードの実際の
座標の挙動を計測するまでは、推測で実装しません。

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [ユーザーガイド](docs/USER_GUIDE.md) | 機能ごとの使い方と制約 |
| [依存関係](docs/DEPENDENCIES.md) | 固定バージョン、配布方針、ローカルパッチ |
| [パフォーマンス](docs/PERFORMANCE.md) | 解像度ごとの実測値とバックエンド選定の根拠 |
| [形式の検証記録](docs/YTT-VERIFICATION.md) | YTT 各規則の根拠と確度 |
| [手動 QA](docs/MANUAL_QA.md) | 自動化できない検証項目 |
| [サードパーティ表記](docs/THIRD-PARTY-NOTICES.md) | 同梱フォントとライブラリ |

## 技術スタック

.NET 10 · C# 14 · Avalonia 12 · SkiaSharp · libmpv · xUnit

字幕形式の入出力には [YTSubConverter](https://github.com/arcusmaximus/YTSubConverter) (MIT) を使用しています。
`ap` と `ju` を独立して扱うためのローカルパッチを 1 つ適用しており、詳細は
[依存関係のドキュメント](docs/DEPENDENCIES.md) にまとめています。

## ライセンス

[LICENSE](LICENSE) を参照してください。同梱フォントと外部ライブラリの表記は
[サードパーティ表記](docs/THIRD-PARTY-NOTICES.md) にあります。

## コントリビューター

- [DO0OG](https://github.com/DO0OG)

---

## キャンバスのクイック編集

- プレビューの空白部分をダブルクリックすると、現在の再生時刻から2秒間のcueをクリック位置に作成し、すぐに文字を入力できます。
- cueはシングルクリックで選択し、ドラッグで位置を移動します。既存のcueをダブルクリックするとインライン編集になります。
- `Ctrl+Z`で元に戻し、`Ctrl+Y`または`Ctrl+Shift+Z`でやり直します。テキストボックスにフォーカスがある場合は、標準の文字編集履歴を使用します。