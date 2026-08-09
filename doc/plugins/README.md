# プラグイン仕様書

リバーシの RuleSet（[../dsl-example-reversi.md](../dsl-example-reversi.md)）を
記述するために必要なプラグインの詳細仕様。11 番目の Tuple と Sequence / Grid の
1.1 は[チェス](../dsl-example-chess.md)が、12 番目の Record と `type.list` /
Sequence 1.2 は[将棋](../dsl-example-shogi.md)が足りないと示したもの
（→ [コレクション設計案](../collections.md)）。

全プラグインが [値モデルとノード種別](../value-model.md) を前提とする。
先にそちらを読むこと。

| プラグイン | 名前空間 | 提供内容 |
| --- | --- | --- |
| [Binding](Binding.md) | `bind` | スコープ付き束縛（`let`）とローカル参照 |
| [Branch](Branch.md) | `branch` | 条件分岐（`if` / `match`） |
| [Definition](Definition.md) | `def` | `definitions` の参照と適用 |
| [Logic](Logic.md) | `logic` | 真偽値演算 |
| [Comparison](Comparison.md) | `cmp` | 等価・順序比較と null 判定 |
| [Arithmetic](Arithmetic.md) | `math` | 算術演算 |
| [TypeSchema](TypeSchema.md) | `type` | `state.schema` を記述するスカラ型語彙 |
| [Sequence](Sequence.md) | `seq` | 列の生成・変換・集約 |
| [State](State.md) | `state` | 状態の読み取りと書き込み |
| [Grid](Grid.md) | `grid` | 二次元盤面・座標・方向 |
| [Tuple](Tuple.md) | `tuple` | 正規テキストを持つ複合値 |
| [Record](Record.md) | `rec` | 状態のレコードと、計算されたキーでの読み書き |

## 共通事項

### 仕様書の読み方

各ノードの「形式」に現れる記法。

- `<式>` — 任意の式ノード、または式として評価される JSON リテラル
- `<式:T>` — 評価結果が種別 `T` であることを要求する
- `?` 付きのキー — 省略可能
- 「静的」と注記されたキー — 式ではなくリテラルのみを許し、`CreateContext` 時に読む

### 検証のタイミング

- **`CreateContext` 時（静的）** — 未知の `op`、必須キーの欠落、ノード種別の
  出現位置違反、静的キーへの式の混入、`def.call` の引数個数不一致
- **評価時（動的）** — 型不一致、null に対する順序・算術、ゼロ除算

静的に検出できるものは実行時まで持ち越さない。

### プラグインマニフェスト

各プラグインは識別子・バージョン・提供名前空間・予約プレフィックスを宣言する。
名前空間とプレフィックスの衝突はロード時に検出する。
