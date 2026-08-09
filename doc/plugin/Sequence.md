# Rulealize.Plugin.Sequence

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.Sequence` |
| 名前空間 | `seq` |
| バージョン | `1.2.0` |
| 予約プレフィックス | なし |
| 依存 | [値モデル](../value-model.md) のみ |

値モデルの `Sequence` に対する生成・変換・集約。

**このプラグインは列がどこから来たかを知らない。** リバーシでは
[Grid](Grid.md) の `grid.ray` / `grid.coords` / `grid.directions` が返した列を
扱うが、Sequence は Grid を参照していない。両者が噛み合うのは、値モデルが
`Sequence` を共有種別として定義しているからである。

**差し替え動機が最も明確なプラグイン。** 評価戦略（即時／遅延）、並列化、
中間結果のバッファリング方針は、このプラグインの実装を入れ替えるだけで変えられる
——ただし下記「再列挙可能性」を満たす限りにおいて。

## 提供ノード

| ノード | 種別 | リバーシでの使用 |
| --- | --- | --- |
| `seq.empty` | 式 | ○ `flips1` |
| `seq.of` | 式 | — （1.1 で追加） |
| `seq.any` | 式 | ○ `canPlace`, `hasAnyMove`, `terminal.when` |
| `seq.count` | 式 | ○ `flips1`, `terminal.result` |
| `seq.elementAt` | 式 | ○ `flips1` |
| `seq.takeWhile` | 式 | ○ `flips1` |
| `seq.selectMany` | 式 | ○ `flips` |
| `seq.where` | 式 | — |
| `seq.select` | 式 | — |
| `seq.concat` | 式 | — （1.2 で追加） |
| `seq.take` | 式 | — （1.2 で追加） |
| `seq.skip` | 式 | — （1.2 で追加） |

---

## 列の性質

### 有限性

すべての列は有限。無限列を生成するノードは提供しない。

`GetValidInputs` が列の全走査を伴うため、停止性は仕様として保証したい。
[Definition](Definition.md) が再帰を禁じているのと同じ理由。

### 再列挙可能性（必須）

[値モデル §1.2](../value-model.md) のとおり、**同一の列値を複数回列挙したとき
同じ結果を返さなければならない。**

リバーシの `flips1` が実際にこれを要求している。

```jsonc
"bind": {
  "ray": { "op": "grid.ray", ... },
  "run": { "op": "seq.takeWhile", "source": "@ray", ... }   // ← 1 回目
},
"in": {
  ...
  "coord": { "op": "seq.elementAt", "source": "@ray", ... }  // ← 2 回目
}
```

`@ray` は `bind.let` で 1 回だけ評価されるが、その結果の列は 2 回列挙される。
遅延列を「1 回しか列挙できないイテレータ」として実装すると、2 回目が空になって
ルールが壊れる。

実装の選択肢は 2 つ。

- 列挙のたびに元の計算を再実行する（純粋なので結果は同じ）
- 初回列挙時にバッファリングする

どちらでもよいが、**「使い捨てイテレータ」は許されない。**

### 要素の種別

列の要素は任意の値。同一の列に異なる種別が混在してもよい（値モデルは
均質性を要求しない）。リバーシでは座標の列（`Opaque`）、方向の列（`Opaque`）、
セル値の列（`Text` / `Null`）が現れる。

---

## 反復ノードの共通形式

述語や射影を取るノードは、要素を束縛する名前を `as` で導入する。

```jsonc
{
  "op": "seq.<名前>",
  "source": <式:Sequence>,
  "as": "<名前>",        // 静的。省略可
  "<述語または射影>": <式>
}
```

- `as` で導入した名前は、**そのノードの述語／射影の式の内側でのみ可視**
- 外側の同名束縛をシャドーイングする
- 参照は [Binding](Binding.md) の `bind.local`（糖衣 `@`）で行う

**Sequence プラグインは束縛を導入するが、参照の語彙は持たない。** これは
分解の帰結。スコープ機構そのものは評価コンテキスト（Abstraction）が提供する。

`as` を省略できるのは、述語／射影が要素を参照しない場合のみ。省略した状態で
`@` 参照を書けば [Binding](Binding.md) 側の未束縛エラー（静的）になる。

### 評価順序

要素の処理順は **列の順序に従う**。短絡するノード（`seq.any` / `seq.takeWhile`）
の意味論がこれに依存する。並列実装であっても、観測される結果は逐次実行と
一致しなければならない。

---

## `seq.empty`

### 形式

```jsonc
{ "op": "seq.empty" }
```

空列を返す。

リバーシの `flips1` で「この方向には裏返せる石が無い」を表す。
`seq.selectMany`（`flips`）が空列を素通りさせるため、8 方向のうち成立しない
方向は自然に消える。

---

## `seq.of`

### 形式

```jsonc
{ "op": "seq.of", "of": [ <式>, … ] }
```

要素を書き並べた列を返す。空配列は空列。

### 列を書き下す唯一の手段

値モデルは `Sequence` に JSON リテラルを与えていない（[§1](../value-model.md)）。
したがってこのノードが無いと、**RuleSet 中のすべての列は他プラグイン由来でしか
作れない**。リバーシは列がすべて `grid.*` から出てくるので気づかなかったが、
これは「Sequence だけをロードして意味が通るか」という[分解の基準 A](../dsl-example-reversi.md)
を Sequence 自身が満たしていなかったということでもある。

チェスのナイトで露見した。8 つのオフセットは `grid.directions` のどの `kind`
にも該当せず、単に 8 つの方向であって、RuleSet がそう言えなければならない。

```jsonc
"knightDirs": { "op": "seq.of",
  "of": ["1,2","2,1","2,-1","1,-2","-1,-2","-2,-1","-2,1","-1,2"] }
```

`seq.selectMany` と組み合わせると連結にもなる。`seq.concat` を別に置かないのは
このため。

```jsonc
// run ++ [next]
{ "op": "seq.selectMany",
  "source": { "op": "seq.of", "of": ["@run", { "op": "seq.of", "of": ["@next"] }] },
  "as": "part", "select": "@part" }
```

### 評価

要素の式は**列挙のたびに**評価する。再列挙可能性が構成上満たされ、全ノードが
純粋なので結果は変わらない。

---

## `seq.any`

### 形式

```jsonc
{
  "op": "seq.any",
  "source": <式:Sequence>,
  "as": "<名前>",           // 省略可
  "predicate": <式:Bool>    // 省略可
}
```

### 評価規則

- `predicate` がある場合: 各要素に対して評価し、`true` が出た時点で `true` を
  返す（短絡）。すべて `false` なら `false`
- `predicate` が無い場合: 列が非空なら `true`

空列は常に `false`。

### 短絡の重要性

`hasAnyMove` は 64 マスすべてについて `canPlace` を評価しうるが、打てる手が
一つ見つかった時点で止まる。`inputs.pass.when` が
`logic.not(#hasAnyMove)` である以上、パスが非合法な局面（＝ほとんどの局面）
では早期に確定する。短絡しない実装だと、パス判定のたびに常に 64 マス × 8 方向
のレイ走査が走る。

### 例（リバーシ）

```jsonc
// canPlace: この座標から裏返せる石があるか
{ "op": "seq.any",
  "source": { "op": "def.call", "def": "flips", "args": { "at": "@at" } } }

// hasAnyMove: 打てる手が一つでもあるか
{ "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
  "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } } }
```

前者は `predicate` 省略形（非空判定）、後者は述語付き。

---

## `seq.count`

### 形式

```jsonc
{
  "op": "seq.count",
  "source": <式:Sequence>,
  "as": "<名前>",      // 省略可
  "where": <式:Bool>   // 省略可
}
```

### 評価規則

`where` があれば、それが `true` になる要素の個数を返す。無ければ列の長さ。
戻り値は `Number`。

短絡しない（全要素を走査する）。

### 例（リバーシ）

```jsonc
// flips1: 相手石の連なりの長さ = レイ上のその次の位置
{ "op": "seq.count", "source": "@run" }

// terminal.result: 黒石の数
{ "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
  "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } }
```

前者の使い方が巧妙で、`run` の長さがそのまま「レイ上で連なりの直後にある要素の
インデックス」になる（0 始まりのため）。

---

## `seq.elementAt`

### 形式

```jsonc
{
  "op": "seq.elementAt",
  "source": <式:Sequence>,
  "index": <式:Number>
}
```

### 評価規則

`index` 番目（0 始まり）の要素を返す。

**範囲外なら `Null` を返す。エラーにしない。**

これは [値モデル §3](../value-model.md) の null 伝播連鎖の起点であり、
リバーシの `flips1` が「レイが盤端まで相手石で埋まっている」ケースを明示的な
境界チェック無しに扱えている理由。

```
seq.elementAt(範囲外) → null → grid.at(null) → null → cmp.eq(null, "black") → false
```

`index` が負、または小数部を持つ場合は評価時エラー（範囲外とは区別する。
「列の外を指した」のではなく「インデックスとして不正」なため）。

### 計算量

列がランダムアクセス可能とは限らないため、実装は先頭から `index + 1` 要素を
走査してよい。`flips1` ではレイの長さが最大 7 なので問題にならない。

---

## `seq.takeWhile`

### 形式

```jsonc
{
  "op": "seq.takeWhile",
  "source": <式:Sequence>,
  "as": "<名前>",
  "predicate": <式:Bool>
}
```

### 評価規則

先頭から順に `predicate` を評価し、初めて `false` になった要素の **手前まで**
を列として返す。`false` になった要素自身は含まない。以降は評価しない。

`predicate` が最初の要素で `false` なら空列。すべて `true` なら元の列全体。

### なぜ `takeWhile` がリバーシの中核か

リバーシの「挟む」判定は、レイ上で

1. 相手石が 1 個以上連続し
2. その直後に自分の石がある

という形をしている。1 が `seq.takeWhile`、2 が `seq.elementAt` + `cmp.eq`。
条件 1 の「1 個以上」は、`flips1` が返した列を `flips` が連結した後、
`canPlace` の `seq.any` が非空判定することで担保される。

```jsonc
{
  "op": "seq.takeWhile", "source": "@ray", "as": "c",
  "predicate": { "op": "cmp.eq",
                 "left": { "op": "grid.at", "grid": "$board", "coord": "@c" },
                 "right": "#opponent" }
}
```

空マス（`Null`）に当たった時点でも `cmp.eq` が `false` になって止まる。
「相手石でない」に空マスと自分の石と盤端がすべて含まれるので、3 通りの
終了条件を 1 つの述語で書けている。

---

## `seq.selectMany`

### 形式

```jsonc
{
  "op": "seq.selectMany",
  "source": <式:Sequence>,
  "as": "<名前>",
  "select": <式:Sequence>
}
```

### 評価規則

各要素に対して `select` を評価し、得られた列を **元の順序を保って連結** した
列を返す。

`select` が `Sequence` 以外を返した場合は評価時エラー（単一値の自動ラップは
行わない）。

### 例（リバーシ `flips`）

```jsonc
{
  "op": "seq.selectMany",
  "source": { "op": "grid.directions", "of": "$board", "kind": "eight" },
  "as": "d",
  "select": { "op": "def.call", "def": "flips1", "args": { "at": "@at", "dir": "@d" } }
}
```

8 方向それぞれの裏返し対象を求め、平坦化して 1 本の列にする。成立しない方向は
`seq.empty` を返すので、連結の結果から自然に消える。

### 重複

連結時に重複除去は行わない。リバーシでは 8 方向のレイが互いに素なので重複は
起きないが、これはルールの性質であってプラグインが保証するものではない。

---

## `seq.where` / `seq.select`

### 形式

```jsonc
{ "op": "seq.where",  "source": <式:Sequence>, "as": "<名前>", "predicate": <式:Bool> }
{ "op": "seq.select", "source": <式:Sequence>, "as": "<名前>", "select": <式> }
```

`seq.where` は述語を満たす要素のみからなる列、`seq.select` は各要素を射影した
列を返す。いずれも順序を保存する。

リバーシでは使用しない。`seq.count` の `where` と `seq.any` の `predicate` で
足りているため。汎用語彙として提供する。

---

## `seq.concat` / `seq.take` / `seq.skip`（1.2）

```jsonc
{ "op": "seq.concat", "of": [ <式:Sequence>, … ] }
{ "op": "seq.take", "source": <式:Sequence>, "count": <式:Number> }
{ "op": "seq.skip", "source": <式:Sequence>, "count": <式:Number> }
```

`seq.concat` は「`seq.of` + `seq.selectMany` で書けるから不要」として一度見送った。
実際に書けるが、**列に対する最も普通の操作がいちばん読みにくくなる**のは、置かない
理由ではなく置く理由だった。[`type.list`](TypeSchema.md) への追記がこれを要求した。

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

`take` / `skip` は上限のある履歴を保つため。範囲を超える `count` はエラーにしない
（列の長さは局面次第であり、3 個しかない列に 10 個求めるのは妥当な問いである）。
負の `count` は評価時エラー——`seq.elementAt` の負インデックスと同じ扱いで、
「範囲外」ではなく「個数として不正」。

---

## 未確定事項

- **集約ノード（`seq.sum` / `seq.min` / `seq.max` / `seq.minBy`）** — 未提供。
  `seq.count` 以外の集約が必要になった時点で追加する。
  [Arithmetic](Arithmetic.md) 側ではなくこちらに置くのが値モデル上は自然
  （列を受け取る演算のため）だが、未整理。
- **`seq.distinct`** — 未提供。等価性は値モデルが定義しているので実装は可能。
  `seq.selectMany` の重複が問題になる規則が出たら追加する。
- **`seq.orderBy`** — 未提供。順序付けには `Opaque` の比較が必要になり、
  [Comparison](Comparison.md) の未確定事項と連動する。
  `GetValidInputs` の出力順を決定的にしたい場合にも関わる。
- **`seq.zip` / インデックス付き反復** — 未提供。`as` が要素だけを束縛する
  現在の形では、要素の位置を参照できない。`flips1` は `seq.count` で位置を
  代替している。
- **列の長さに対する静的な上界** — `GetValidInputs` のコスト見積もりを
  精密化するなら、`grid.coords` のような有界な列の長さを静的に知る仕組みが
  要る。
