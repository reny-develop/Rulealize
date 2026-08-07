# Rulealize.Plugin.State

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.State` |
| 名前空間 | `state` |
| バージョン | `1.0.0` |
| 予約プレフィックス | `$` |
| 依存 | [値モデル](../value-model.md) のみ |

状態の読み取りと書き込み。

**読み（式ノード）と書き（効果ノード）を同一プラグインに置く。** 両者は同じ
パス解決規則を共有しており、片方だけロードする意味がないため。読み書きで
プラグインを分けると、パス構文の仕様が 2 箇所に分裂する。

このプラグインは状態の **構造** を知らない。`board` が盤面であることも、
`turn` が列挙値であることも解釈しない。パスで指した位置の値を値モデルの値
として出し入れするだけであり、盤面としての解釈は [Grid](Grid.md) が行う。

## 提供ノード

| ノード | 種別 | リバーシでの使用 |
| --- | --- | --- |
| `state.get` | 式 | ○ 糖衣 `$` として全域 |
| `state.set` | 効果 | ○ `inputs.place`, `inputs.pass` |
| `state.update` | 効果 | — |

---

## パス

### 構文

ドット区切りのフィールド名列。

```
"turn"
"board"
"players.black.score"
```

パスは **静的**。式で組み立てることはできない。理由は 3 つ。

- `CreateContext` 時に `state.schema` と突き合わせて存在検証ができる
- 静的解析で「この入力がどのフィールドを書き換えるか」が判る
- 動的パスを許すと、スキーマ検証を実行時まで持ち越すことになる

### 解決規則

`state.schema` のトップレベルのフィールド名から始め、`Record` を辿る。

現状 `state.schema` の直下は「フィールド名 → スキーマノード」のマップに限られ、
入れ子の Record は書けない（[TypeSchema](TypeSchema.md) の未確定事項）。
したがってリバーシで有効なパスは `board` / `turn` / `passes` の 3 つのみ。
ドット記法は将来の入れ子に備えた予約。

### 盤面の内部へはパスで到達しない

`"board.d3"` のようなパスは **提供しない**。盤面の内部構造は
[Grid](Grid.md) の管轄であり、State プラグインは `board` を 1 つの不透明な値
として扱う。マスの参照は `grid.at`、書き込みは `grid.set` を使う。

これは分解の要。State が盤面の内部表現（sparse か dense か、座標記法は何か）を
知ってしまうと、Grid を差し替えられなくなる。

---

## `state.get`

### 形式

```jsonc
{ "op": "state.get", "path": "<パス>" }   // path は静的
```

糖衣: `"$<パス>"`

### 評価規則

現在の **状態スナップショット** から `path` の値を読んで返す。

「現在のスナップショット」の意味は文脈で決まる。

| 文脈 | 読む対象 |
| --- | --- |
| `inputs.*.when` | `GetValidInputs` / `ApplyToState` に渡された State |
| `inputs.*.effects` | **同上**（効果適用前の State） |
| `definitions` の本体 | 呼び出し元の文脈に従う |
| `terminal` | 判定対象の State |

`effects` の中で `state.get` が **効果適用前** の値を読むのは、
[値モデル §5](../value-model.md) のスナップショット意味論による。リバーシの
`inputs.pass` がこれに依存している。

```jsonc
{ "op": "state.set", "path": "passes",
  "value": { "op": "math.add", "of": ["$passes", 1] } }
```

同じ `effects` 配列内の他の要素が `passes` を書き換えていても、`$passes` は
入力時の値を返す。

### エラー

| 条件 | タイミング |
| --- | --- |
| `path` がスキーマに存在しない | 静的エラー |
| `path` が式 | 静的エラー |

値が `Null` であることはエラーではない（スキーマが `nullable` を許していれば
正常）。

### 例（リバーシ）

```jsonc
"me": { "op": "state.get", "path": "turn" }
```

`$board` は `grid.*` ノードの `grid` / `of` / `target` キーに渡される。
State プラグインは盤面値を取り出すだけで、中身は見ない。

---

## `state.set`

### 形式

```jsonc
{ "op": "state.set", "path": "<パス>", "value": <式> }   // path は静的
```

**効果ノード。** `inputs.*.effects` の要素としてのみ出現できる。

### 適用規則

1. `value` を **スナップショットに対して** 評価する
2. 評価した値を、ドラフトの `path` へ書き込む

同一パスへの書き込みが複数回あれば **後勝ち**。

### スキーマ検証

書き込む値がパスのスキーマに適合するかを検証するかは未確定
（[TypeSchema](TypeSchema.md) の「効果適用後の検証」）。`GetValidInputs` が
数百候補を試す場面ではコストが問題になる。

### 例（リバーシ `inputs.place.effects`）

```jsonc
[
  { "op": "grid.set", ... },
  { "op": "grid.setMany", ... },
  { "op": "state.set", "path": "passes", "value": 0 },
  { "op": "state.set", "path": "turn", "value": "#opponent" }
]
```

`#opponent` は `#me`（＝`$turn`）から導かれるので、スナップショット意味論に
より入力時の手番の相手が入る。逐次適用だと、直前の効果が `turn` を書き換えて
いた場合に結果が変わりうる。

---

## `state.update`

現在値を参照して更新する。

### 形式

```jsonc
{
  "op": "state.update",
  "path": "<パス>",     // 静的
  "as": "<名前>",       // 静的
  "value": <式>
}
```

**効果ノード。**

### 適用規則

1. スナップショットから `path` の値を読み、`as` の名前に束縛する
2. その束縛下で `value` を評価する
3. 結果をドラフトの `path` へ書き込む

`state.set` に `state.get` を組み合わせても同じことができる。リバーシの
`inputs.pass` は後者の形で書いている。

```jsonc
// 等価
{ "op": "state.set", "path": "passes",
  "value": { "op": "math.add", "of": ["$passes", 1] } }

{ "op": "state.update", "path": "passes", "as": "n",
  "value": { "op": "math.add", "of": ["@n", 1] } }
```

パスが長い場合に重複を避けられる、という程度の利点しかない。リバーシでは
使用しない。

---

## 状態文書の形式

`ApplyToState` が受け取り、返す JSON。

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

`data` の直下のキーが `state.schema` のフィールドに対応する。

各フィールドの値をどう JSON へ落とすかは、そのフィールドのスキーマノードを
提供したプラグインが決める。`board` の sparse 表現は `grid.board`
（[Grid](Grid.md)）の責務であり、State プラグインは関知しない。

State プラグインが定めるのは `$schema` / `ruleSet` / `data` という外枠だけ。

### `ruleSet` の照合

State 文書の `ruleSet` が `RuleContext` の RuleSet と一致しない場合はエラー。
バージョンの互換性判定規則は未確定（→ 未確定事項）。

---

## 未確定事項

- **バージョン互換** — `reversi@1.0.0` で作られた State を `reversi@1.1.0` の
  コンテキストで読めるか。RuleSet のバージョニング方針と合わせて決める。
- **入れ子 Record へのパス** — ドット記法は予約済みだが、
  [TypeSchema](TypeSchema.md) が入れ子スキーマを持たないため現状使えない。
- **効果適用後のスキーマ検証** — 上述。
- **状態の差分表現** — `ApplyToState` が状態全体を返す設計。長い対局で
  状態を積み上げる用途では差分が欲しくなるが、盤面が小さいリバーシでは不要。
- **読み取り専用の派生フィールド** — 石数のような値を状態に持たせるか、
  `definitions` で都度計算するか。現状は後者（`terminal.result` が
  `seq.count` で数えている）。
