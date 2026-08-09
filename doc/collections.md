# 状態にコレクションを置く — 設計と結果

[将棋](dsl-example-shogi.md)が突きつけた穴への回答。`state.schema` はスカラの
フラットなマップであり、多重集合も列も置けない。

**実装済み。** [`type.list`](plugins/TypeSchema.md)（TypeSchema 1.1）、
[`Rulealize.Plugin.Record`](plugins/Record.md)、[Sequence 1.2](plugins/Sequence.md)、
`SchemaNode.Normalize`（Abstraction 0.2.0）。将棋を書き直して perft は変わっていない。

- 前提: [値モデル](value-model.md)、[State プラグイン](plugins/State.md)


## 1. いま払っているもの

`shogi.json` は 925 行。うち**持ち駒の定型句が 72 行（約 8%）**を占める。

| 内訳 | 行数 |
| --- | --- |
| `state.schema` の `bP`〜`wR` 14 フィールド | 14 |
| `move` / `drop` の持ち駒更新 effects（14 × 2） | 28 |
| `held` の 2 段 `branch.match` と `handKinds` | 30 |

しかもこれは削れない。`state.set` の `path` はリテラルなので「駒種 `@kind` の
カウンタを増やす」とは書けず、14 個すべてを並べるしかない。

チェスの三回同形反復も、将棋の千日手も、非ゲーム用途の履歴・キュー・明細も、
同じ壁の向こうにある。


## 2. 設計原理 — パスはリテラルのままにする

**`state.set` に `"hand.P"` を書けるようにはしない。** [State プラグイン](plugins/State.md)
が挙げる 3 つの利点（全パスの事前検査、どのフィールドを書くかが文書から読める、
スキーマ検証を実行時に持ち越さない）を手放すことになるためである。

代わりに **`grid.board` の前例をそのまま踏む。**

> 盤の内側はパスで到達できない。盤は State から見れば 1 つの Opaque 値であり、
> マスの読み書きは `grid.at` / `grid.set` が持つ。

コレクションも同じ縫い目にする。**フィールド全体を指すパスはリテラル、内側は
そのコレクションを定義したプラグインの語彙で触る。**


## 3. List と Record は対称にしない

同じ形の 2 つを作りたくなるが、値モデルの側で事情が違う。

| | 値の種別 | 既存の語彙 | 必要なもの |
| --- | --- | --- | --- |
| List | `Sequence` | **`seq.*` がすべて使える** | スキーマノードだけ |
| Record | `Record` | **無い** | スキーマ + 読み + 書き |

列を `Sequence` として持てば、`seq.count` / `seq.any` / `seq.where` /
`seq.elementAt` がその日から動く。専用の読み出し語彙を作るのは、同じ意味の
ノードを二重に持つことにしかならない。

したがって、

- **`type.list` は [TypeSchema](plugins/TypeSchema.md) に置く。** 式ノードを
  持たない純粋なスキーマノードであり、TypeSchema の性格（「提供するのは式ノード
  ではなくスキーマノード」）を壊さない。「スカラの語彙」という説明文だけ改める。
- **レコードは新プラグイン `Rulealize.Plugin.Record`（名前空間 `rec`）。**
  スキーマ・式・効果の 3 種を提供する。Grid と同じ構えになる。

非対称は不格好に見えるが、**理由のある非対称**であり、`requires` の発見可能性
（[分解の基準](dsl-example-reversi.md)）にも合う。列を持つだけの RuleSet が
`Rulealize.Plugin.Record` を要求せずに済む。


## 4. `type.list`

### 形式

```jsonc
{
  "op": "type.list",
  "element": <スキーマノード>,      // 必須
  "minLength": <整数>,              // 省略可。静的
  "maxLength": <整数>               // 省略可。静的
}
```

### 値

`Sequence`。要素はすべて `element` を満たす。

`Sequence` にしたので、読み出しに新しい語彙は要らない。

```jsonc
{ "op": "seq.count", "source": "$history" }
{ "op": "seq.any", "source": "$queue", "as": "j",
  "predicate": { "op": "cmp.eq", "left": "@j", "right": "@target" } }
```

### JSON 形

配列。`element` のスキーマノードが各要素の形を決める。

```jsonc
"history": [ { "board": { … }, "turn": "white" }, … ]
```

### 書き込み

`state.set` に `Sequence` を返す式を渡す。専用の効果ノードは置かない。

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

`seq.concat` は現在未提供（`seq.of` + `seq.selectMany` で書けるという理由で
見送られている）。**追加を推奨する** — §7 参照。

### `maxLength` は飾りではない

`GetValidInputs` は列を全走査するし、状態は毎遷移で直列化される。上限の無い
履歴は文書サイズを手数の 2 乗で膨らませる。

チェスの三回同形反復には**原理的な上限がある**。反復は最後の非可逆手（駒取り
またはポーンの動き）以降にしか成立せず、それは 50 手ルールの 100 手で頭打ちに
なる。`maxLength: 100` は妥協ではなく規則そのものである。


## 5. `Rulealize.Plugin.Record`

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.Record` |
| 名前空間 | `rec` |
| 予約プレフィックス | なし |

### 5.1 スキーマノード 2 種

**閉じたキー集合**を前提にする。開いたマップにしない理由は、`type.enum` を
`type.string` + 正規表現より優先したのと同じで、**宣言されているものは検査
できる**からである。

```jsonc
// 異種のフィールドを持つレコード
{ "op": "rec.of", "fields": { "<名前>": <スキーマ>, … } }

// 同種の値をキーで引くレコード
{ "op": "rec.map", "keys": ["<キー>", …], "value": <スキーマ> }
```

`rec.map` を別に置くのは、それが**言えることが違う**ため。「すべての値が同じ型
である」は `rec.of` では表現できず、将棋の持ち駒はまさにそれである。

```jsonc
// 14 フィールドが 4 行になる
"hand": {
  "op": "rec.map", "keys": ["black", "white"],
  "value": { "op": "rec.map", "keys": ["P", "L", "N", "S", "G", "B", "R"],
             "value": { "op": "type.int", "min": 0 } }
}
```

キーの順序は宣言順。直列化の決定性のため（`state.schema` と同じ規則）。

### 5.2 式ノード

```jsonc
{ "op": "rec.at",   "record": <式>, "key": <式:Text> }
{ "op": "rec.has",  "record": <式>, "key": <式:Text> }
{ "op": "rec.with", "record": <式>, "key": <式:Text>, "value": <式> }
{ "op": "rec.keys", "of": <式> }
```

> **`rec.has` は実装中に足した。** 設計案には無かったが、`rec.at` を無いキーで
> エラーにすると決めた以上、**安全に問う手段が無い**ことになる。将棋が取った駒を
> 持ち駒に入れるとき、それが持ち駒になる駒種かを先に確かめる必要があり、無ければ
> RuleSet がスキーマとは別に 7 種のリストを抱えることになった。使ってみるまで
> 見えなかった穴である。

- **`rec.at`** — 値を返す。`record` が `Null` なら `Null`（`tuple.at` と同じ）。
  **宣言されていないキーは評価時エラー。** ここは `grid.at` と分ける。盤には
  「存在しないマス」が正当に存在する（盤外）が、閉じたレコードに存在しない
  キーは書き手の誤りでしかなく、それに依存する規則も無い。
- **`rec.with`** — キー 1 つを差し替えた新しいレコードを返す。純粋。
  `grid.with` と同じ役割で、**遷移後の状態を問う規則**（[チェス §3.1](dsl-example-chess.md)）
  に要る。宣言されていないキーへの書き込みは評価時エラーであり、これによって
  **レコードは構成上つねにスキーマを満たす。**
- **`rec.keys`** — キーの `Sequence`（Text）。反復の入口。

```jsonc
// 将棋の handKinds が 1 つの式になる
{ "op": "seq.where", "source": { "op": "rec.keys", "of": "#myHand" }, "as": "k",
  "predicate": { "op": "cmp.gt",
                 "left": { "op": "rec.at", "record": "#myHand", "key": "@k" }, "right": 0 } }
```

### 5.3 効果ノード

```jsonc
{ "op": "rec.set",    "target": "$<フィールド>", "key": <式>, "value": <式> }
{ "op": "rec.update", "target": "$<フィールド>", "key": <式>, "as": "<名前>", "value": <式> }
```

`target` は `grid.set` と同じ解決（`IStateLocation` からパスを取り、スキーマが
レコードであることをビルド時に検査）。

**現在の値はドラフトから読む。** `grid.set` がそうしているのと同じ理由で、
同一入力の複数の効果が積み上がる必要があるため。1 手で持ち駒が増えかつ減る
規則（将棋には無いが、一般には普通にある）はこれを要求する。

`rec.update` は現在値を `as` で束縛する。`state.update` のキー付き版。

```jsonc
// 28 ブロックが 1 つになる
{ "op": "rec.update", "target": "$hand", "key": "#me", "as": "h",
  "value": {
    "op": "branch.if",
    "cond": { "op": "def.call", "def": "isCapture", "args": { "m": "@m" } },
    "then": { "op": "rec.with", "record": "@h",
              "key": { "op": "def.call", "def": "captured", "args": { "m": "@m" } },
              "value": { "op": "math.add", "of": [
                  { "op": "rec.at", "record": "@h",
                    "key": { "op": "def.call", "def": "captured", "args": { "m": "@m" } } }, 1] } },
    "else": "@h" } }
```

### 5.4 入れ子

`rec.of` / `rec.map` / `type.list` の要素はどれも任意のスキーマノードなので、
レコードのレコード、レコードの列、列の盤面がそのまま書ける。深いところへの
書き込みは `rec.update` + `rec.with` の組み合わせで到達する（上の例が
`$hand` → 手番 → 駒種の 2 段）。


## 6. 何がどう縮んだか（実測）

`shogi.json` は **925 行 → 880 行**。

| | 前 | 後 |
| --- | --- | --- |
| スキーマ | 14 フィールド | `rec.map` 7 行 |
| 更新 effects | **28 ブロック** | **`rec.update` 2 つ** |
| `gained` / `spent` | 34 行 | 不要（削除） |
| `held` | 2 段 `branch.match` 17 行 | `rec.at` 2 段 8 行 |
| `handKinds` | `seq.of` に 7 種を直書き | `rec.keys` |

**当初「72 行 → 約 12 行」と見積もったのは楽観的すぎた。** 45 行しか減っていない。
行数が実態を映していない面はある——消えた 28 行はどれも 200 字近い 1 行だった——が、
それを差し引いても見積もりは外れている。

減り方の質のほうが大きい。`rec.has` のおかげで**7 種の駒種リストが RuleSet から
消えた**。以前は `handKinds` がスキーマとは別に 7 個を並べており、いずれ食い違う
場所だった。今はレコード自身のキーを引いている。

チェスの三回同形反復も届く。局面を「盤面 + 手番 + キャスリング権 + アンパッサン」の
`rec.of` にして `type.list` に積めば、比較は**値の構造的等価**でそのままできる
（`BoardValue` の等価性は幾何とマス目で定義済み）。局面の指紋を作るノードは要らない。

チェスの三回同形反復も届く。局面を「盤面 + 手番 + キャスリング権 + アンパッサン」
の `rec.of` にして `type.list` に積めば、比較は**値の構造的等価**でそのまま
できる（`BoardValue` の等価性は幾何とマス目で定義済み）。局面の指紋を作る
ノードは要らない。


## 7. 波及して必要になるもの

### 7.1 Sequence（1.2）

| ノード | 用途 |
| --- | --- |
| `seq.concat` | 履歴への追加。現在は `seq.of` + `selectMany` で書けるが、最も普通の操作がいちばん読みにくい |
| `seq.take` / `seq.skip` | 履歴を直近 N 手に切り詰める。`maxLength` を守るために要る |

### 7.2 `SchemaNode.Normalize`（Abstraction 0.2.0）— 採用

```csharp
public virtual RuleValue Normalize(RuleValue value) => value;
```

`StateDraft.Commit` が各フィールドに対して呼ぶ。狙いは 2 つ。

1. **遅延列の実体化。** `type.list` に書かれる `Sequence` は遅延でありうる。
   状態に遅延列が入ると、その値は評価コンテキスト（＝ひとつ前のスナップショット）
   を捕まえたままになる。現在の実装では遷移のたびに文書へ直列化されるので
   実害は出ないが、**誰も書き留めていない性質に依存している。**
2. **`maxLength` のような制約を書き込み時に検査する余地。** 現状スキーマ検証は
   読み込み時にしか走らず（[TypeSchema の未確定事項](plugins/TypeSchema.md)）、
   壊れた状態は呼び出し側に返ってから次の読み込みで初めて露見する。

既定実装は恒等なので、**既存プラグインは無改修**。`StateDraft.Commit` が書かれた
フィールドにだけ呼ぶ（誰も触らなかったフィールドは、文書か前回の commit で一度
settle 済みである）。


## 8. 決めたこと・決めていないこと

### 決めた

- パスはリテラルのまま。内側はプラグインが持つ（`grid.board` の縫い目を踏襲）
- `type.list` は `Sequence` を保持し、TypeSchema に置く。読み出し語彙は作らない
- レコードは閉じたキー集合。`rec.with` が未宣言キーを拒むことで、**構成上つねに
  スキーマを満たす**
- `rec.at` の未宣言キーは評価時エラー（`grid.at` の寛容さは継がない）
- 効果ノードはドラフトから読む（`grid.set` と同じ）

### 実装して分かったこと

- **`rec.has` が要る**（§5.2）。`rec.at` を厳格にした帰結で、設計時には見落として
  いた。
- **`rec.keys` は序数順**にした。レコードは値であり、どのスキーマも見ていない
  リテラルから作られうるので、宣言順は常に存在するとは限らない。
- **見積もりは外れた**（§6）。72 → 12 のつもりが 925 → 880 行。

### 未確定事項

- **開いたキー集合** — 外部データを写すような非ゲーム用途では、キーを事前に
  宣言できない状態が現れうる。閉じた形で始め、必要が出てから考える。
- **レコードと入力引数** — `Record` は正規テキストを持たないので、domain が
  レコードを返すと[引数の解決](dsl-example-chess.md)が「テキスト形が無い」で
  落ちる。正しい挙動だが、複合の入力が要るなら `tuple` を使うことになる、と
  明記しておく必要がある。
- **列の要素の等価性コスト** — 局面の列に対する `seq.any` は盤面同士の比較を
  繰り返す。三回同形反復には十分だが、長い履歴では効いてくる。
- **`rec.of` と型推論** — 異種フィールドを持つレコードは、`rec.at` の戻り値の型
  が静的に決まる数少ない場所である。型推論を入れるならここが足がかりになる。
