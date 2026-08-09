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

## 配布しない語彙

上の 12 個は DLL としてフォルダから発見される。しかし `RuleRuntime.AddPlugin` は
**インスタンス**を取るので、語彙がディスク上のアセンブリである必要はない。

```csharp
RuleRuntime runtime = new RuleRuntime()
    .LoadPluginsFrom("plugins")
    .AddPlugin(new DeployVocabulary(freezeCalendar, ownershipMap));
```

ライブラリを自社の業務ルールに使うプロジェクトには、**書く価値はあるが公開する価値は
ない**演算が必ず出てくる。その場合はプラグインを仕立てて配布するのではなく、
`IRulealizePlugin` を自分のアセンブリで実装してこの経路で渡す。契約は published な
プラグインと完全に同じ — マニフェストを持ち、名前空間を主張し、RuleSet の `requires`
に名前が載る。

実例は [デプロイパイプライン](../dsl-example-deploy.md) と
[sample/Deploy/](../../sample/Deploy/)。

### なぜ「軽い登録 API」を作らないのか

マニフェスト無しで式を 1 つずつ登録できる API があれば手軽ではある。作らない理由は
`requires` にある。

`requires` が読む価値を持つのは、**すべての語彙がマニフェストを持つ**という一点に
依存している。出自が二種類になれば、`requires` に書けない語彙・`OperationTable` の
衝突検査を通らない語彙・`RuleRuntime.Plugins` に現れない語彙が生まれ、RuleSet 文書が
「読めば必要なものが分かる文書」でなくなる。

配布経路の違いは `new` かフォルダ走査かだけに留める。これなら
`Acme.Deploy.Rules` を要求する RuleSet は、その語彙を持たないランタイムでは
**プラグインがフィードに無かったときと同じ失敗**で弾かれる。

### アプリ内語彙にだけできること

フォルダ走査で発見されるプラグインは public かつ**引数なしコンストラクタ**を要求
される（`PluginProbe`）。したがって構造的に無状態である。

インスタンスを渡す経路ではこの制約が外れ、**起動時に読み込んだ不変スナップショットを
コンストラクタで注入した語彙**が書ける。祝日カレンダー、料金表、組織図、所有者
マップ — 外部が所有し、独自の頻度で更新され、個々の State に載せる筋合いのない
データが対象になる。

### 規約

| | |
| --- | --- |
| 識別子・名前空間 | **ベンダー修飾する。** `Acme.Deploy.Rules` / `acme` であって `Rules` / `deploy` ではない。素の名前を私的語彙が占めると、後に公開されるプラグインと衝突する。その頃には RuleSet が本番で動いている |
| 予約プレフィックス | **主張しない。** 1 プラグインにつき 1 文字、使える文字はごく僅か。利用者が 1 人の語彙が消費してよい資源ではない |
| バージョン | 語彙の互換性を表す。op を消す／意味を変えるならメジャーを上げる（`requires` の `^` がそう読む） |

### 純粋性 — これは様式の話ではない

**登録する演算は、引数とその不変スナップショットだけの純粋関数でなければならない。**

`GetValidInputs` はパラメータ domain の候補ごとに guard を 1 回評価する。ここで
外部を読む演算が混ざると、

- 候補数ぶんのクエリが飛ぶ（組合せ爆発がそのまま I/O 爆発になる）
- 同一呼び出しの中で同じ問いに違う答えが返る
- 「入力到着時点の State を読む」というスナップショット意味論が破れる。
  式が読むものが State だけであることに依存した保証である

変化する値は State 文書に置く。**現在日時は演算が取りに行くものではなく、State の
フィールドとして演算に渡すもの**である。`sample/Deploy` の `acme.frozen` が
`date` を引数に取っているのはこのため。
