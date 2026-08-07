# Rulealize.Plugin.Comparison

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.Comparison` |
| 名前空間 | `cmp` |
| バージョン | `1.0.0` |
| 予約プレフィックス | なし |
| 依存 | [値モデル](../value-model.md) のみ |

等価判定・順序比較・null 判定。

[Arithmetic](Arithmetic.md) とは別プラグイン。比較は `Text` や `Opaque` にも
適用されるため、数値演算とは適用範囲が異なる。リバーシは `math.add` を 1 箇所で
しか使わないのに対し、`cmp` は全域で使う。

三方比較 `cmp.compare` をここに置いた（`math` ではなく）のも同じ理由。

## 提供ノード

| ノード | 種別 | リバーシでの使用 |
| --- | --- | --- |
| `cmp.eq` | 式 | ○ `flips1`, `terminal.result` |
| `cmp.ne` | 式 | — |
| `cmp.lt` / `cmp.lte` / `cmp.gt` / `cmp.gte` | 式 | ○ `gte` を `terminal.when` |
| `cmp.compare` | 式 | ○ `terminal.result` |
| `cmp.isNull` | 式 | ○ `canPlace`, `terminal.when` |
| `cmp.coalesce` | 式 | — |

---

## 等価判定 — `cmp.eq` / `cmp.ne`

### 形式

```jsonc
{ "op": "cmp.eq", "left": <式>, "right": <式> }
{ "op": "cmp.ne", "left": <式>, "right": <式> }
```

### 評価規則

両辺を評価し、[値モデル §2](../value-model.md) の等価性規則で判定する。
`cmp.ne` は `cmp.eq` の否定。

**null 安全。** どちらかが `Null` でもエラーにならない。

| 左 | 右 | `cmp.eq` |
| --- | --- | --- |
| `Null` | `Null` | `true` |
| `Null` | 任意の非 `Null` | `false` |
| `"black"` | `"black"` | `true` |
| `1` | `"1"` | `false`（種別が異なる） |

### null 安全であることの意味

これはリバーシの `flips1` が依存する中核の性質。

```jsonc
{
  "op": "cmp.eq",
  "left": { "op": "grid.at", "grid": "$board",
            "coord": { "op": "seq.elementAt", "source": "@ray",
                       "index": { "op": "seq.count", "source": "@run" } } },
  "right": "#me"
}
```

レイが盤端まで相手石で埋まっている場合、`seq.elementAt` が範囲外となって
`Null` を返し、`grid.at` も `Null` を返す。ここで `cmp.eq` がエラーになると、
DSL 記述者は「レイの長さが `run` の長さより大きいか」という境界チェックを
明示的に書かねばならない。null 安全にすることで、その 1 段が消える。

**境界チェックを DSL から追い出すのが、この設計の狙い。** 順序比較を null
非安全にしている（下記）のと対照的だが、これは意図的な非対称である。等価判定
の「値が無いことは、その値と一致しないこと」という解釈には曖昧さがないのに
対し、順序の「値が無いものは大きいのか小さいのか」には答えが無いため。

### 種別をまたぐ比較

種別が異なれば無条件に非等価。エラーにはしない。`grid.at` が返すセル値
（`Text` または `Null`）と `Text` を比較する用途で、種別チェックを都度書かずに
済ませるため。

---

## 順序比較 — `cmp.lt` / `cmp.lte` / `cmp.gt` / `cmp.gte`

### 形式

```jsonc
{ "op": "cmp.gte", "left": <式>, "right": <式> }
```

### 評価規則

両辺を評価し、順序関係を判定して `Bool` を返す。

### 比較可能な種別

| 種別 | 順序 |
| --- | --- |
| `Number` | 数値順 |
| `Text` | 序数（コードポイント）順。カルチャ非依存 |
| `Bool` | `false` < `true` |
| その他 | 評価時エラー |

`Text` の比較を序数順に固定するのは、実行環境のロケールで RuleSet の意味が
変わることを防ぐため。

### null の扱い

**どちらかが `Null` なら評価時エラー。** 等価判定と異なり、null 安全にしない。

順序は全順序でなければ意味を成さないが、`Null` を含めた全順序には自然な選択が
存在しない（SQL は `NULLS FIRST` / `NULLS LAST` を明示させ、多くの言語は
そもそも比較を禁じる）。任意の選択を仕様に埋め込むより、エラーにして記述者に
`cmp.isNull` や `cmp.coalesce` を書かせる。

### 種別が異なる場合

**評価時エラー。** 等価判定と異なり、種別をまたぐ比較を許さない。
`cmp.lt(1, "a")` に意味のある答えは無い。

### 例（リバーシ `terminal.when`）

```jsonc
{ "op": "cmp.gte", "left": "$passes", "right": 2 }
```

`passes` は `type.int`（min 0 / max 2）なので `Null` にならず、上の制限に
抵触しない。

---

## `cmp.compare`

三方比較。

### 形式

```jsonc
{ "op": "cmp.compare", "left": <式>, "right": <式> }
```

### 評価規則

順序比較と同じ規則で両辺を比較し、結果を `Text` で返す。

| 関係 | 戻り値 |
| --- | --- |
| `left` < `right` | `"lt"` |
| `left` = `right` | `"eq"` |
| `left` > `right` | `"gt"` |

`Number` ではなく `Text` を返すのは、[Branch](Branch.md) の `branch.match` と
直接噛み合わせるため。`-1` / `0` / `1` を返す設計だと `cases` のキーが
`"-1"` のようになり読めない。

null・種別不一致の扱いは順序比較と同じ（評価時エラー）。

### 例（リバーシ `terminal.result`）

```jsonc
{
  "op": "branch.match",
  "value": { "op": "cmp.compare", "left": "@b", "right": "@w" },
  "cases": { "gt": "black", "lt": "white", "eq": "draw" }
}
```

黒石数と白石数を比較して勝者を決める。`cmp.gt` と `cmp.lt` を別々に書いて
`branch.if` を入れ子にするより、三方比較 1 回のほうが「引き分けを書き忘れる」
事故が起きにくい。

---

## `cmp.isNull`

### 形式

```jsonc
{ "op": "cmp.isNull", "value": <式> }
```

### 評価規則

`value` を評価し、`Null` なら `true`、それ以外なら `false` を返す。

### 例（リバーシ `canPlace`）

```jsonc
{ "op": "cmp.isNull", "value": { "op": "grid.at", "grid": "$board", "coord": "@at" } }
```

「そのマスが空である」の表現。`grid.at` が空マスに対して `Null` を返す設計
（[Grid](Grid.md)）と対になっている。

`cmp.eq(x, null)` でも同じ判定ができるが、専用ノードを置くのは意図の明示の
ため。JSON の `null` リテラルが式として書かれると読み手が構造か値か迷う。

---

## `cmp.coalesce`

### 形式

```jsonc
{ "op": "cmp.coalesce", "of": [<式>, ...] }
```

### 評価規則

`of` の各要素を先頭から順に評価し、最初の非 `Null` 値を返す。短絡する
（非 `Null` が見つかった時点で以降を評価しない）。

すべて `Null`、または空配列なら `Null` を返す。

主な用途は、順序比較や算術の前に `Null` を既定値へ潰すこと。リバーシでは
使用しない。

---

## 未確定事項

- **`Sequence` / `Record` の順序比較** — 現状エラー。辞書順比較を定義する
  動機は今のところない。
- **`Opaque` の順序** — 現状エラー。座標に順序を入れると `GetValidInputs` の
  出力を安定ソートできるが、順序はプラグイン定義になるため値モデル側に
  「順序を提供する Opaque」という概念が要る。→ 出力の決定性が要求されたら
  再検討する。
- **`cmp.between`** — `logic.and` と `cmp.lte` 2 つで書ける。範囲判定が頻出
  するようなら追加を検討。
