# Rulealize.Plugin.Branch

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.Branch` |
| 名前空間 | `branch` |
| バージョン | `1.0.0` |
| 予約プレフィックス | なし |
| 依存 | [値モデル](../value-model.md) のみ |

条件分岐。真偽値による二分岐（`if`）と、値による多分岐（`match`）を提供する。

[Logic](Logic.md) とは別プラグイン。`logic.and` などは真偽値を返す **式** で
あり、制御構造ではない。両者を分けておくと、分岐が不要な RuleSet（純粋な
制約充足の記述など）で Branch を外せる。

## 提供ノード

| ノード | 種別 | オセロでの使用 |
| --- | --- | --- |
| `branch.if` | 式 | ○ `flips1` |
| `branch.match` | 式 | ○ `opponent`, `terminal.result` |

---

## `branch.if`

### 形式

```jsonc
{
  "op": "branch.if",
  "cond": <式:Bool>,
  "then": <式>,
  "else": <式>          // 省略可
}
```

### 評価規則

1. `cond` を評価する
2. `true` なら `then` を評価してその値を返す
3. `false` なら `else` を評価してその値を返す。`else` が無ければ `Null` を返す

**短絡評価。** 選択されなかった枝は評価しない。これは性能のためだけでなく、
評価エラーを起こしうる式を条件で守れるようにするため（例: 空列に対する
`seq.elementAt` を非空チェックで守る）。

### 型

`then` と `else` の値の種別が一致することは要求しない。ただし `branch.if` の
結果を受け取る側が種別を要求する場合、その検査は受け取る側で行われる。

### エラー

| 条件 | タイミング |
| --- | --- |
| `cond` が `Bool` でない | 評価時エラー。`Null` や `0` を偽として扱う暗黙変換は行わない |
| `then` の欠落 | 静的エラー |

真偽値への暗黙変換を認めないのは、`grid.at` が返す `Null`（＝空マス）が
うっかり偽として通ってしまう事故を防ぐため。空マス判定は
`cmp.isNull` を明示的に書かせる。

### 例（オセロ `flips1`）

```jsonc
{
  "op": "branch.if",
  "cond": {
    "op": "cmp.eq",
    "left": { "op": "grid.at", "grid": "$board",
              "coord": { "op": "seq.elementAt", "source": "@ray",
                         "index": { "op": "seq.count", "source": "@run" } } },
    "right": "#me"
  },
  "then": "@run",
  "else": { "op": "seq.empty" }
}
```

相手石の連なり `run` の直後のマスが自分の石なら、`run` が裏返し対象として確定
する。そうでなければ空列。`cond` は [値モデル §3](../value-model.md) の null
伝播に依存しており、レイが盤端で尽きた場合も自然に `else` へ落ちる。

---

## `branch.match`

値による多分岐。

### 形式

```jsonc
{
  "op": "branch.match",
  "value": <式>,
  "cases": { "<キー>": <式>, ... },   // キーは静的
  "default": <式>                     // 省略可
}
```

### 評価規則

1. `value` を評価する
2. 結果を **テキスト正規形** に変換し、`cases` のキーと完全一致で照合する
3. 一致したケースの式を評価して返す
4. 一致が無く `default` があればそれを評価して返す
5. 一致が無く `default` も無ければ **評価時エラー**

一致したケース以外は評価しない。

### キーの照合規則

`cases` のキーは JSON オブジェクトのキーなので必ず文字列である。照合のために
`value` の評価結果を次の規則でテキスト化する。

| `value` の種別 | テキスト正規形 |
| --- | --- |
| `Text` | そのまま |
| `Bool` | `"true"` / `"false"` |
| `Number` | 正規化した十進表記（`1.0` → `"1"`） |
| `Null` | `"null"` |
| `Opaque` | プラグインが定義するテキスト正規形（[値モデル §1.1](../value-model.md)） |
| `Sequence` / `Record` | 評価時エラー（照合不能） |

`Opaque` が照合できることで、座標や方向に対する分岐が書ける。

### 網羅性

`default` を省略した場合に網羅性を静的に検査できるのは、`value` が
`type.enum` に由来すると静的に判る場合に限られる。現状の設計では式の型推論を
持たないため、**網羅性検査は行わず、実行時に一致なしでエラー** とする。

これはオセロの `opponent` にとっては実質的に安全である（`turn` が
`type.enum` で `black` / `white` に制限されており、`cases` が両方を覆っている）
が、それを保証しているのは DSL ではなく `state.schema` の検証であることに注意。

型推論を導入すれば静的検査に格上げできる。→ 未確定事項。

### エラー

| 条件 | タイミング |
| --- | --- |
| `cases` が空 | 静的エラー |
| キー重複 | JSON のキー重複として静的エラー |
| 一致なし・`default` なし | 評価時エラー |
| `value` が `Sequence` / `Record` | 評価時エラー |

### 例（オセロ `opponent`）

```jsonc
{
  "op": "branch.match",
  "value": "#me",
  "cases": { "black": "white", "white": "black" }
}
```

`terminal.result` では `cmp.compare` の戻り値（`"lt"` / `"eq"` / `"gt"`）を
受けている。`cmp.compare` が Text を返す設計なので、`branch.match` と自然に
噛み合う。

---

## 未確定事項

- **パターンマッチ** — `cases` のキーは完全一致のみ。範囲や構造パターンは
  持たない。オセロでは不要だが、より複雑なルール（駒種による分岐など）で
  必要になる可能性がある。
- **網羅性の静的検査** — 上述のとおり型推論が前提。導入するなら
  [TypeSchema](TypeSchema.md) のスキーマ情報を式の型推論へ流す必要がある。
- **`branch.cond`（多段 if-else）** — `branch.if` の入れ子で書けるが、
  ネストが深くなる。オセロでは不要。
