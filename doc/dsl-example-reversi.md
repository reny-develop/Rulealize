# JSON DSL 設計メモ — リバーシを題材にした検討

`Rulealize.Abstraction` の API を設計する前段として、「どんな JSON が書けれ
ば十分か」をリバーシで具体化した検討メモ。ここで確定した DSL の形から、
Abstraction 側のインタフェースを逆算する。

- 対象: RuleSet / State / InputRule の 3 文書
- 前提: `Rulealize.Abstraction` は未実装（この設計の帰結として今後開発する）


## 1. 構造原理

CLAUDE.md の制約から、DSL の形はかなり絞られる。

| 制約 | DSL への帰結 |
| --- | --- |
| コアはプラグイン固有型を一切知らない | **ノードは「`op` キーを持つ JSON オブジェクト」だけがコアの知識**。`op` の値でレジストリを引き、あとはプラグインの Factory に丸投げする |
| プラグインは Rulealize を参照しない | Factory が依存するのは `Rulealize.Abstraction` のみ |
| `GetValidInputs(state, limit)` が成立する | **入力は「名前 + パラメータ」であり、各パラメータは列挙可能な domain を持つ**必要がある。ここが DSL 設計の最大の制約になる |

コアが構造的に予約するキーは以下だけとする。

```
$schema / id / version / requires / state / definitions / inputs / terminal
```

加えて、ノード判別用の `op`。**それ以外はすべてプラグインの語彙**。

### 参照記法（糖衣）

```jsonc
"$board"    // = { "op": "state.get",  "path": "board" }
"@at"       // = { "op": "bind.local", "name": "at" }
"#opponent" // = { "op": "def.ref",    "name": "opponent" }
```

この desugar もコアには持たせない。**プラグインが「先頭 1 文字」を予約宣言
して文字列糖衣を登録する**方式にすれば、コアは糖衣の存在すら知らずに済む
（`$` は State プラグイン、`@` と `#` は Binding / Definition プラグインが
それぞれ登録する）。プレフィックスの衝突検出はロード時のランタイムの責務。


## 2. プラグイン分解

### 2.1 分解の判断基準

「Core」のような包括名を避けるため、次の 3 基準で切る。

- **基準 A — 独立ロード可能性**: その語彙だけをロードして意味が通るか。
  分岐だけあって束縛が無い構成は成立するので、両者は別プラグイン。
- **基準 B — 差し替え動機**: 別実装に置き換えたくなるか。`seq` を遅延評価版
  や並列評価版に差し替えたい動機は現実にあるので、独立させる価値がある。
- **基準 C — 値モデルのみで相互運用できるか**: プラグイン間が互いの CLR 型に
  依存していないか。`grid.coords` が返す列を `seq.any` が受け取れるのは、
  Abstraction 側に共有値モデルがあるからで、Grid が Sequence を参照している
  わけではない（→ 6.1）。

そして最も実務的な理由として、**`requires` を読めばその RuleSet がどんな語彙
を使うか把握できる**こと。`Rulealize.Plugin.Core` という名前はこの発見可能性
を丸ごと潰してしまう。

### 2.2 一覧

各プラグインの詳細仕様は [plugins/](plugins/README.md) にある。

| プラグイン | 名前空間 | 提供するもの |
| --- | --- | --- |
| [`Rulealize.Plugin.Binding`](plugins/Binding.md) | `bind` | `let` / `local`（糖衣 `@`）— スコープ付き束縛 |
| [`Rulealize.Plugin.Branch`](plugins/Branch.md) | `branch` | `if` / `match` — 分岐 |
| [`Rulealize.Plugin.Definition`](plugins/Definition.md) | `def` | `ref`（糖衣 `#`）/ `call` — `definitions` の参照と適用 |
| [`Rulealize.Plugin.Logic`](plugins/Logic.md) | `logic` | `and` / `or` / `not` / `xor` |
| [`Rulealize.Plugin.Comparison`](plugins/Comparison.md) | `cmp` | `eq` / `ne` / `lt` / `lte` / `gt` / `gte` / `compare` / `isNull` / `coalesce` |
| [`Rulealize.Plugin.Arithmetic`](plugins/Arithmetic.md) | `math` | `add` / `sub` / `mul` / `div` / `mod` / `min` / `max` / `abs` |
| [`Rulealize.Plugin.TypeSchema`](plugins/TypeSchema.md) | `type` | `enum` / `int` / `bool` / `string` — `state.schema` を書くための語彙 |
| [`Rulealize.Plugin.Sequence`](plugins/Sequence.md) | `seq` | `any` / `count` / `empty` / `takeWhile` / `elementAt` / `select` / `selectMany` / `where` |
| [`Rulealize.Plugin.State`](plugins/State.md) | `state` | `get`（糖衣 `$`）/ `set` / `update` — 状態の読み書き |
| [`Rulealize.Plugin.Grid`](plugins/Grid.md) | `grid` | `board` / `at` / `set` / `setMany` / `coords` / `cells` / `ray` / `directions` |

プラグイン識別子と名前空間は 1 対 1 に対応させ、対応表はプラグイン側のマニ
フェストが宣言する。名前空間の衝突検出はロード時に行う。

### 2.3 配布単位はプラグイン単位と分ける

`requires` が 10 行になるのは冗長だが、これは**配布の問題であって DSL の問題
ではない**。`Rulealize.Plugin.StandardLibrary` のような NuGet メタパッケージ
（実体を持たず上記を参照するだけのパッケージ）を用意すれば、導入は 1 行で済む。
`requires` に「プロファイル名」を書けるようにする案もあるが、それは 2.1 で
挙げた発見可能性を再び潰すので採らない。


## 3. RuleSet JSON（リバーシ）

```jsonc
{
  "$schema": "rulealize/ruleset/v1",
  "id": "reversi",
  "version": "1.0.0",

  "requires": [
    { "plugin": "Rulealize.Plugin.Binding",    "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Branch",     "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Definition", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Logic",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Arithmetic", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Sequence",   "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.State",      "version": "^1.0" },
    { "plugin": "Rulealize.Plugin.Grid",       "version": "^1.0" }
  ],

  // ── 状態のスキーマと初期値 ────────────────────────────────
  "state": {
    "schema": {
      "board": {
        "op": "grid.board",
        "width": 8, "height": 8, "coord": "algebraic",
        "cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
      },
      "turn":   { "op": "type.enum", "values": ["black", "white"] },
      "passes": { "op": "type.int", "min": 0, "max": 2 }
    },
    "initial": {
      "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
      "turn": "black",
      "passes": 0
    }
  },

  // ── 再利用可能な式（非再帰の純粋関数）──────────────────────
  "definitions": {
    "me":       { "op": "state.get", "path": "turn" },
    "opponent": {
      "op": "branch.match",
      "value": "#me",
      "cases": { "black": "white", "white": "black" }
    },

    // 1 方向ぶんの裏返し対象セル列
    "flips1": {
      "params": ["at", "dir"],
      "body": {
        "op": "bind.let",
        "bind": {
          "ray": { "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" },
          "run": {
            "op": "seq.takeWhile", "source": "@ray", "as": "c",
            "predicate": {
              "op": "cmp.eq",
              "left":  { "op": "grid.at", "grid": "$board", "coord": "@c" },
              "right": "#opponent"
            }
          }
        },
        "in": {
          "op": "branch.if",
          // 相手石の連なりの「次」が自分の石なら、その連なりが確定する
          "cond": {
            "op": "cmp.eq",
            "left": {
              "op": "grid.at", "grid": "$board",
              "coord": {
                "op": "seq.elementAt", "source": "@ray",
                "index": { "op": "seq.count", "source": "@run" }
              }
            },
            "right": "#me"
          },
          "then": "@run",
          "else": { "op": "seq.empty" }
        }
      }
    },

    // 8 方向の合算
    "flips": {
      "params": ["at"],
      "body": {
        "op": "seq.selectMany",
        "source": { "op": "grid.directions", "of": "$board", "kind": "eight" },
        "as": "d",
        "select": { "op": "def.call", "def": "flips1", "args": { "at": "@at", "dir": "@d" } }
      }
    },

    "canPlace": {
      "params": ["at"],
      "body": {
        "op": "logic.and",
        "all": [
          { "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "@at" } },
          { "op": "seq.any", "source": { "op": "def.call", "def": "flips", "args": { "at": "@at" } } }
        ]
      }
    },

    "hasAnyMove": {
      "body": {
        "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
        "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } }
      }
    }
  },

  // ── 入力（＝状態遷移の入口）────────────────────────────────
  "inputs": {
    "place": {
      "actor": "#me",
      "params": {
        "at": { "domain": { "op": "grid.coords", "of": "$board" } }   // ← 列挙可能性の源
      },
      "when": { "op": "def.call", "def": "canPlace", "args": { "at": "@at" } },
      "effects": [
        { "op": "grid.set", "target": "$board", "coord": "@at", "value": "#me" },
        { "op": "grid.setMany", "target": "$board",
          "coords": { "op": "def.call", "def": "flips", "args": { "at": "@at" } },
          "value": "#me" },
        { "op": "state.set", "path": "passes", "value": 0 },
        { "op": "state.set", "path": "turn", "value": "#opponent" }
      ]
    },

    "pass": {
      "actor": "#me",
      "params": {},
      "when": { "op": "logic.not", "value": "#hasAnyMove" },
      "effects": [
        { "op": "state.set", "path": "passes",
          "value": { "op": "math.add", "of": ["$passes", 1] } },
        { "op": "state.set", "path": "turn", "value": "#opponent" }
      ]
    }
  },

  // ── 終局判定と結果 ─────────────────────────────────────────
  "terminal": {
    "when": {
      "op": "logic.or",
      "any": [
        { "op": "cmp.gte", "left": "$passes", "right": 2 },
        { "op": "logic.not", "value": {
            "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
            "predicate": { "op": "cmp.isNull",
                           "value": { "op": "grid.at", "grid": "$board", "coord": "@c" } } } }
      ]
    },
    "result": {
      "op": "bind.let",
      "bind": {
        "b": { "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
               "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } },
        "w": { "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
               "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "white" } }
      },
      "in": {
        "op": "branch.match",
        "value": { "op": "cmp.compare", "left": "@b", "right": "@w" },
        "cases": { "gt": "black", "lt": "white", "eq": "draw" }
      }
    }
  }
}
```

**この RuleSet にリバーシ専用プラグインは 1 つも登場しない。** 汎用語彙だけで
リバーシが記述できている点が、この設計の妥当性の主な根拠。将棋であれば、
`grid` に加えて「持ち駒」を表す汎用プラグイン（多重集合）を足す、という
積み上げになるはず。


## 4. State / InputRule

### State JSON

```jsonc
{
  "$schema": "rulealize/state/v1",
  "ruleSet": "reversi@1.0.0",
  "data": {
    "board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" },
    "turn": "black",
    "passes": 0
  }
}
```

`board` は sparse 表現。密表現（8×8 配列）にするかは `grid.board` プラグイン
の責務であり、コアは関知しない。

### InputRule JSON

```jsonc
{
  "$schema": "rulealize/input/v1",
  "ruleSet": "reversi@1.0.0",
  "input": "place",
  "args": { "at": "d3" }
}
```

パスは `{ "input": "pass", "args": {} }`。

### `ApplyToState` の戻り値

```jsonc
{
  "data": {
    "board": { "d3": "black", "d4": "black", "e4": "black", "d5": "black", "e5": "white" },
    "turn": "white",
    "passes": 0
  },
  "terminal": false
}
```

### `GetValidInputs` の戻り値（初期局面）

```jsonc
[
  { "input": "place", "args": { "at": "d3" }, "actor": "black" },
  { "input": "place", "args": { "at": "c4" }, "actor": "black" },
  { "input": "place", "args": { "at": "f5" }, "actor": "black" },
  { "input": "place", "args": { "at": "e6" }, "actor": "black" }
]
```


## 5. validationLimit の意味づけ

候補は「各パラメータ domain の直積」で生成され、`when` で篩われる。
リバーシの場合は `place` が 64、`pass` が 1 で計 65 候補。

なお domain と `when` はどちらも規則であり、`ApplyToState` は両方を検査する
（[値モデル §4.1](value-model.md)）。リバーシは規則をすべて `when` に置いて
いるので、この RuleSet に限れば domain は実質ヒントとして働く。チェスは逆に
規則の大半を domain に置いており、そちらが検査の必要性を出した。

limit は **「`when` を評価する候補数の上限」** と定義するのが素直で、超過時は
`Truncated = true` を返す。ただし将棋の `move(from, to, promote)` は
81 × 81 × 2 ≒ 13k になるため、domain 側で事前に絞り込める仕組み
（例: `grid.coordsWhere`）を用意できるかが実用性の分かれ目になる。


## 6. 未確定の設計判断

### 6.1 プラグイン間相互運用は共有値モデルが担保する（→ Abstraction の要件）

→ [値モデルとノード種別](value-model.md) に分離した。以下は要旨。

`grid.coords` が返した列を `seq.any` が受け取れる必要がある。ここで Grid が
Sequence を参照すると分解の意味が失われるので、**Abstraction 側に共有値モデル**
を置く。最小構成は `null / bool / number / string / sequence / record / opaque`。
`opaque` はプラグイン固有値（座標、方向など）の格納先で、コアは中身を見ない。

null 伝播の規則も値モデルの責務。リバーシの `flips1` はこれに依存している。

- `seq.elementAt` の範囲外 → `null`
- `grid.at` に `null` 座標 → `null`
- `cmp.eq(null, "black")` → `false`

これにより「レイの終端まで相手石が続く」ケースが自然に `else` へ落ちる。
明示的な境界チェックを DSL に書かずに済んでいるので、この規則は仕様として
固定する価値がある。

### 6.2 effects はスナップショット意味論にすべき

`place` は「石を置く」→「裏返す」の順だが、逐次適用だと 2 番目の `flips` が
変更後の盤面を再走査してしまう。**全 effect の式は入力時の状態に対して評価し、
書き込みはドラフトに溜めて一括適用**、と決めるのが安全。

逐次意味論にするなら `flips` を `bind.let` で先に束縛させる必要があり、DSL
記述者への負担が増える。

### 6.3 `flips` が `when` と `effects` で二重評価される

`Internal/Node` の評価器に、同一ノード＋同一環境のメモ化（共通部分式除去）を
入れる余地がここに出る。`GetValidInputs` では 64 候補 × 8 方向のレイ走査が
走るので、効き方が大きい。

### 6.4 パスを明示入力にするか自動にするか

本来のリバーシは「打てる手がなければ自動的に手番が飛ぶ」。上記は明示入力モデル
（`GetValidInputs` が `pass` だけを返す）を採っている。自動化するなら
遷移後フック（`"after"` フェーズのようなもの）を RuleSet に足す必要があり、
これは DSL の表現力に関わる分岐点。

### 6.5 `definitions` は再帰を許すか

`params` を持たせた時点で実質は純粋関数。再帰を許すと停止性が保証できない。
リバーシ程度なら**非再帰に限定**で十分で、そのほうが `GetValidInputs` のコスト
見積もりも立つ。

### 6.6 `state.schema` は必須か

`initial` だけでも動くが、domain 推論と外部入力 State の検証のために宣言が
あったほうが堅牢。スキーマ記述自体もプラグイン語彙（`grid.board` /
`type.enum`）になっている点は一貫している。


## 7. Abstraction への逆算メモ

この DSL を成立させるために Abstraction が最低限持つべきもの。

- **値モデル** — 6.1 の型と null 伝播規則
- **`INodeFactory`** — `op` 名の宣言と、JSON オブジェクトからノードを構築
- **`INodeBuilder`** — Factory に渡される再帰ビルダ（子ノードの構築を委譲）
- **`IRuleNode`** — 評価コンテキストを受け取り値を返す
- **評価コンテキスト** — 状態への読み取りアクセス、ローカル束縛スコープ、
  `definitions` の解決
- **効果適用インタフェース** — 6.2 のドラフトへの書き込み
- **文字列糖衣の登録** — プレフィックス予約（`$` / `@` / `#`）
- **プラグインマニフェスト** — 識別子・バージョン・提供名前空間の宣言
