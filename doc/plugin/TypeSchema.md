# Rulealize.Plugin.TypeSchema

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.TypeSchema` |
| 名前空間 | `type` |
| バージョン | `1.1.0` |
| 予約プレフィックス | なし |
| 依存 | [値モデル](../value-model.md) のみ |

`state.schema` に書くスカラ型の語彙。

**このプラグインが提供するのは式ノードではなく [スキーマノード](../value-model.md#4-ノード種別)** で、
`state.schema` の内側にのみ出現できる。式として評価されることはない。

複合型（盤面）は [Grid](Grid.md) の `grid.board` が提供する。TypeSchema は
その `cell` に入るスカラ型を担当するという分担であり、両者は値モデルを介して
のみ噛み合う（Grid は TypeSchema を参照しない。`grid.board` は `cell` に
「任意のスキーマノード」を受け取るだけ）。

## 提供ノード

| ノード | 種別 | リバーシでの使用 |
| --- | --- | --- |
| `type.enum` | スキーマ | ○ `board.cell`, `turn` |
| `type.int` | スキーマ | ○ `passes` |
| `type.bool` | スキーマ | — |
| `type.string` | スキーマ | — |
| `type.list` | スキーマ | — （1.1 で追加） |

## `type.list`

```jsonc
{
  "op": "type.list",
  "element": <スキーマノード>,   // 必須
  "minLength": <整数>,           // 省略可。静的
  "maxLength": <整数>            // 省略可。静的
}
```

値は **`Sequence`**。JSON 形は配列。

### なぜスカラの語彙にコレクションが居るのか

このプラグインが守っている線は「スカラかどうか」ではなく **「式ノードを持たない」**
である。`type.list` は読み出しの語彙をまったく必要としない——値が `Sequence` なので、
`seq.count` / `seq.any` / `seq.where` が初日から動く。

```jsonc
{ "op": "seq.count", "source": "$history" }
```

書き込みも `state.set` に `Sequence` を返す式を渡すだけで、専用の効果ノードは無い。

```jsonc
{ "op": "state.set", "path": "history",
  "value": { "op": "seq.concat", "of": ["$history", { "op": "seq.of", "of": ["@position"] }] } }
```

対照的に[レコード](Record.md)には既存の語彙が何も無く、独立したプラグインになった。
**非対称だが理由のある非対称**である（→ [コレクション設計案](../collections.md)）。

### 上限は飾りではない

状態は毎遷移で直列化されるので、上限の無い履歴は文書サイズを手数の 2 乗で膨らませる。
しかも上限は妥協ではなく規則そのものであることが多い——チェスの三回同形反復に要る
履歴は最後の非可逆手までで、それは 50 手ルールが 100 手で頭打ちにする。

### 遅延列の実体化

`type.list` に書かれた `Sequence` は遅延でありうる。遷移が確定するとき
`SchemaNode.Normalize`（Abstraction 0.2.0）が一度列挙して実体化するので、状態が
「ひとつ前の状態から自分を再計算する方法」を抱え込むことはない。

---

## スキーマノードの役割

`state.schema` は 3 つの目的を持つ。

1. **外部 State の検証** — `ApplyToState` に渡される State 文書が正しい
   形かを `CreateContext` 済みのコンテキストが検査する。手書きや外部システム
   由来の State を受け入れる以上、これは必須
2. **初期状態の検証** — `state.initial` がスキーマに適合するかを
   `CreateContext` 時に検査する
3. **将来の型推論の入力** — [Branch](Branch.md) の `branch.match` の網羅性
   検査など、静的解析の土台になりうる（現状は未実装）

`state.initial` だけでも実行はできるが、上の 1 が満たせない。

## 共通キー

すべてのスキーマノードが受け付ける。

| キー | 既定 | 説明 |
| --- | --- | --- |
| `nullable` | `false` | `Null` を許すか。静的キー |

`nullable` を型ごとの派生ではなく共通修飾子にしたのは、リバーシの
「セルは `black` / `white` / 空」という頻出パターンを 1 行で書けるようにする
ため。

---

## `type.enum`

有限の `Text` 値集合。

### 形式

```jsonc
{
  "op": "type.enum",
  "values": ["<値>", ...],   // 静的
  "nullable": <真偽>          // 省略可
}
```

### 検証規則

対象の値が `Text` であり、`values` のいずれかと完全一致すること。
`nullable` が `true` なら `Null` も許す。

`values` は空であってはならない（静的エラー）。重複も静的エラー。

### 例（リバーシ）

```jsonc
"turn": { "op": "type.enum", "values": ["black", "white"] }
```

```jsonc
"cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
```

盤面のセルは `nullable`。空マスを `Null` で表す設計であり、
[Comparison](Comparison.md) の `cmp.isNull` と [Grid](Grid.md) の `grid.at` が
この表現を前提にしている。

「空マス」を `"empty"` という第 3 の列挙値にする案もあったが、採らなかった。
`grid.at` が盤外に対して返す値と空マスの値が別物になり、`flips1` の null 伝播
（[値モデル §3](../value-model.md)）が壊れるため。**盤外も空マスも「石が無い」
という一つの概念に潰れることが、リバーシの記述を短くしている。**

### 列挙値の順序

`values` の並びに意味は持たせない。順序が必要なら `type.int` を使う。

---

## `type.int`

整数。

### 形式

```jsonc
{
  "op": "type.int",
  "min": <整数>,      // 省略可。静的
  "max": <整数>,      // 省略可。静的
  "nullable": <真偽>  // 省略可
}
```

### 検証規則

対象の値が `Number` であり、小数部を持たず、`min` 以上 `max` 以下であること。
`min` / `max` は省略可能で、省略時は無制限。

値モデルの `Number` は整数と小数を区別しないため、「整数であること」は
スキーマ側の制約として表現する。

### 例（リバーシ）

```jsonc
"passes": { "op": "type.int", "min": 0, "max": 2 }
```

`max: 2` は「連続パス 2 回で終局」というルールと重複した情報だが、意味は異なる。
`terminal.when` は「2 に達したら終局」という**遷移の規則**であり、こちらは
「3 以上の状態は存在しない」という**状態空間の制約**である。

この重複は、外部から渡された不正な State（`passes: 5` など）を検出できる形で
効いてくる。ただし両者が食い違ったときに検出する仕組みは無い（→ 未確定事項）。

---

## `type.bool`

### 形式

```jsonc
{ "op": "type.bool", "nullable": <真偽> }
```

対象の値が `Bool` であること。

---

## `type.string`

### 形式

```jsonc
{
  "op": "type.string",
  "minLength": <整数>,   // 省略可。静的
  "maxLength": <整数>,   // 省略可。静的
  "nullable": <真偽>     // 省略可
}
```

対象の値が `Text` であり、長さが範囲内であること。

正規表現による制約は **提供しない**。RuleSet の検証が正規表現エンジンの方言に
依存するのを避けるため。パターン制約が必要な場面は、多くの場合 `type.enum` で
表現できる。

---

## 検証の失敗

スキーマ検証の失敗は、発生箇所によってタイミングが異なる。

| 対象 | タイミング |
| --- | --- |
| `state.initial` | `CreateContext` 時 |
| 外部から渡された State | `ApplyToState` / `GetValidInputs` の入口 |
| `effects` 適用後の状態 | → 未確定事項 |

失敗時は、どのパスのどの値がどの制約に反したかを含むエラーを返す。

---

## 未確定事項

- **効果適用後の検証** — `effects` がスキーマ違反の状態を作った場合に検出
  するか。毎遷移で全状態を検証するのはコストが高く、`GetValidInputs` が
  数百候補を試す場面では特に問題になる。デバッグ時のみ有効化する診断モード
  として持たせるのが妥当か。
- **状態空間の制約と遷移規則の整合** — 上述の `passes` の例のように、
  同じルールが 2 箇所に現れる。整合性を静的に検査する手段は今のところない。
- ~~**`type.record` / 構造化されたスキーマ**~~ — 解決済み。将棋の持ち駒がこれを
  要求し、[`rec.of` / `rec.map`](Record.md) になった。名前空間を `type` にしなかった
  理由は[コレクション設計案](../collections.md)を参照。
- **型推論への接続** — スキーマ情報を式の静的型検査に流せば、`branch.match`
  の網羅性検査や `cmp` の種別不一致を `CreateContext` 時に検出できる。
  実装コストが大きいので、DSL が安定してから着手する。
