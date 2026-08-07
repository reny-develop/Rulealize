# Rulealize.Plugin.Grid

| 項目 | 値 |
| --- | --- |
| 識別子 | `Rulealize.Plugin.Grid` |
| 名前空間 | `grid` |
| バージョン | `1.0.0` |
| 予約プレフィックス | なし |
| 依存 | [値モデル](../value-model.md) のみ |

二次元盤面、座標、方向、レイ走査。

**リバーシ専用の語彙は一つも含まない。** 「石を挟む」「裏返す」といった概念は
Grid には無く、それらは RuleSet 側が `grid.ray` と `seq.takeWhile` の組み合わせ
として記述する。この境界を守れているかが、プラグイン設計の妥当性を測る基準に
なっている。

同じ Grid で、五目並べ（`grid.ray` + 連続長）、チェッカー、ライフゲームなどが
記述できるはず。将棋・チェスには持ち駒／成りを扱う別プラグインの追加が要る。

**3 種類のノードをすべて提供する唯一のプラグイン。**

## 提供ノード

| ノード | 種別 | リバーシでの使用 |
| --- | --- | --- |
| `grid.board` | スキーマ | ○ `state.schema.board` |
| `grid.at` | 式 | ○ `flips1`, `canPlace`, `terminal.when` |
| `grid.coords` | 式 | ○ `hasAnyMove`, `inputs.place.params`, `terminal.when` |
| `grid.cells` | 式 | ○ `terminal.result` |
| `grid.ray` | 式 | ○ `flips1` |
| `grid.directions` | 式 | ○ `flips` |
| `grid.set` | 効果 | ○ `inputs.place` |
| `grid.setMany` | 効果 | ○ `inputs.place` |

## 導入する Opaque 型

| 型タグ | 意味 | テキスト正規形 |
| --- | --- | --- |
| `grid/coord` | 盤上の位置 | 盤の `coord` 記法に従う（例: `"d3"`） |
| `grid/direction` | 方向ベクトル | `"<dx>,<dy>"`（例: `"1,-1"`） |

[値モデル §1.1](../value-model.md) のとおり、Opaque はテキスト正規形との相互
変換を持たねばならない。座標については、これが `GetValidInputs` の出力
（`{ "at": "d3" }`）と InputRule の入力を成立させている。

---

## `grid.board`

盤面のスキーマ。

### 形式

```jsonc
{
  "op": "grid.board",
  "width": <整数>,          // 静的
  "height": <整数>,         // 静的
  "coord": "<記法>",        // 静的。省略時 "index"
  "cell": <スキーマノード>   // 静的
}
```

**スキーマノード。** `state.schema` の内側にのみ出現できる。

| キー | 説明 |
| --- | --- |
| `width` / `height` | 盤の寸法。1 以上 |
| `coord` | 座標のテキスト正規形。下記 |
| `cell` | 各マスの型。任意のスキーマノード |

### `coord` 記法

| 値 | 形式 | 例（8×8 の左上／右下） |
| --- | --- | --- |
| `"algebraic"` | 列を英小文字、行を 1 始まりの数字 | `"a1"` / `"h8"` |
| `"index"` | `"<x>,<y>"`（0 始まり） | `"0,0"` / `"7,7"` |

`"algebraic"` は `width` が 26 以下の場合のみ使用できる（静的エラー）。

原点と軸の向きは `"algebraic"` の場合、**左下が `a1`、y は上向き** とする
（チェス／リバーシの慣行）。`"index"` は **左上が `0,0`、y は下向き**。
両者で向きが違うのは混乱の元だが、それぞれの記法の慣行に従うほうが誤解が
少ないと判断した。

### `cell` と空マス

```jsonc
"cell": { "op": "type.enum", "values": ["black", "white"], "nullable": true }
```

Grid は [TypeSchema](TypeSchema.md) を参照しない。`cell` に来るのは
「何らかのスキーマノード」であり、その解釈はコアのノード構築機構が行う。

**Grid は `nullable` を要求しないが、`grid.at` が盤外に `Null` を返す以上、
`cell` が `nullable` でない盤面では「盤外」と「正常なセル値」の区別がつく**
（正常なセル値は決して `Null` にならないため）。リバーシは `nullable` にして
両者をあえて同一視している（下記 `grid.at` 参照）。

### JSON 表現

盤面の状態は **sparse なオブジェクト** として直列化する。キーは座標の
テキスト正規形、値はセルの値。

```jsonc
"board": { "d4": "white", "e4": "black", "d5": "black", "e5": "white" }
```

`Null` のマスはキーごと省略する。8×8 の全マスを書き下すより短く、差分も読み
やすい。**この表現は Grid の内部事情であり、[State](State.md) プラグインは
関知しない。** dense な配列表現へ変えたければ、Grid の実装だけを差し替える。

内部表現（メモリ上）が sparse である必要はない。8×8 の配列で保持して、直列化
時に sparse へ落とすのが自然。

---

## 座標の受理形式

座標を受け取るすべてのノード（`grid.at` / `grid.set` / `grid.ray` など）は、
次の 2 つを受理する。

| 種別 | 例 | 用途 |
| --- | --- | --- |
| `Opaque(grid/coord)` | — | `grid.coords` / `grid.ray` の出力を渡す場合 |
| `Text` | `"d3"` | InputRule の `args` 由来。盤の `coord` 記法で解釈する |

`Text` の解釈に失敗した場合（記法違反）は評価時エラー。**盤の範囲外を指す
テキストは、解釈には成功する** — 範囲外の扱いは各ノードが定める（下記）。

`Null` を受け取った場合はノードごとに定める。

この二形式受理が、`inputs.place.params.at` の domain が `Opaque` の列を返す
のに、InputRule には `"at": "d3"` と書ける理由。

---

## `grid.at`

マスの値を読む。

### 形式

```jsonc
{
  "op": "grid.at",
  "grid": <式:盤面>,
  "coord": <式:座標>
}
```

### 評価規則

| `coord` | 戻り値 |
| --- | --- |
| 盤内 | そのマスの値（空マスなら `Null`） |
| **盤外** | **`Null`** |
| **`Null`** | **`Null`** |

### 盤外と `Null` 座標を許す理由

これが Grid の設計上もっとも重要な判断。

リバーシの `flips1` は、レイの終端を越えた位置を読む。

```jsonc
"coord": { "op": "seq.elementAt", "source": "@ray",
           "index": { "op": "seq.count", "source": "@run" } }
```

レイが盤端まで相手石で埋まっていれば、`seq.elementAt` は範囲外となり `Null`
を返す。ここで `grid.at` が `Null` 座標をエラーにすると、DSL 記述者は
「レイの長さと `run` の長さを比較する」境界チェックを明示的に書かねばならない。

**盤外・`Null` 座標・空マスの 3 つがすべて `Null` に潰れることで、
「そこに自分の石は無い」という一つの判定に統一される。** これが
[Comparison](Comparison.md) の null 安全な `cmp.eq` と噛み合って、`flips1` の
条件式が 1 段で済んでいる。

ただしこの設計は、**タイプミスした座標が静かに `Null` になる** という代償を
伴う。座標が静的に書かれることは稀（ほぼ常に `grid.coords` や `grid.ray`
由来）なので、実害は小さいと判断した。厳格版が必要なら `grid.atStrict` を
別途追加する。→ 未確定事項。

---

## `grid.coords`

盤上の全座標。

### 形式

```jsonc
{ "op": "grid.coords", "of": <式:盤面> }
```

### 評価規則

盤上の全座標を `Opaque(grid/coord)` の列として返す。長さは `width × height`。

**列挙順序は決定的でなければならない。** 順序は左上から行優先
（`"index"` 記法での `(0,0), (1,0), …`）とする。`GetValidInputs` の出力順が
実行ごとに変わらないようにするため。

### 例（リバーシ）

```jsonc
// inputs.place.params — 候補生成の domain
"at": { "domain": { "op": "grid.coords", "of": "$board" } }

// hasAnyMove — 全マス走査
{ "op": "seq.any", "source": { "op": "grid.coords", "of": "$board" }, "as": "c",
  "predicate": { "op": "def.call", "def": "canPlace", "args": { "at": "@c" } } }
```

domain としての用法が `GetValidInputs` の起点。8×8 なら 64 候補で、
validationLimit に十分収まる。

---

## `grid.cells`

盤上の全セル値。

### 形式

```jsonc
{ "op": "grid.cells", "of": <式:盤面> }
```

座標ではなく値の列を返す。順序は `grid.coords` と同じ。

### 例（リバーシ `terminal.result`）

```jsonc
{ "op": "seq.count", "source": { "op": "grid.cells", "of": "$board" },
  "as": "c", "where": { "op": "cmp.eq", "left": "@c", "right": "black" } }
```

`grid.coords` + `grid.at` でも書けるが、値だけが要る場合に短くなる。

---

## `grid.ray`

ある座標からある方向へ伸びる座標列。

### 形式

```jsonc
{
  "op": "grid.ray",
  "grid": <式:盤面>,
  "from": <式:座標>,
  "dir": <式:方向>,
  "length": <式:Number>   // 省略可
}
```

### 評価規則

`from` から `dir` 方向へ 1 歩ずつ進んだ座標の列を返す。

- **`from` 自身は含まない**（1 歩目から始まる）
- 盤の外へ出た時点で終了する
- `length` があればその歩数で打ち切る

`from` が盤外または `Null` の場合は空列を返す（エラーにしない）。

戻り値は `Opaque(grid/coord)` の列。

### `from` を含まない理由

含める設計だと、リバーシの `flips1` は毎回先頭を読み飛ばす必要がある。
「隣から先を見る」がレイの主用途であり、含めないほうが記述が短い。
起点自体が要るなら `from` を直接使えばよい。

### 例（リバーシ `flips1`）

```jsonc
{ "op": "grid.ray", "grid": "$board", "from": "@at", "dir": "@dir" }
```

8×8 の盤で、この列の長さは最大 7。`seq.takeWhile` と `seq.elementAt` から
2 回列挙されるため、[Sequence](Sequence.md) の再列挙可能性が要件になる。

---

## `grid.directions`

方向の集合。

### 形式

```jsonc
{
  "op": "grid.directions",
  "of": <式:盤面>,
  "kind": "<種別>"       // 静的
}
```

### `kind`

| 値 | 内容 | 個数 |
| --- | --- | --- |
| `"orthogonal"` | 上下左右 | 4 |
| `"diagonal"` | 斜め 4 方向 | 4 |
| `"eight"` | 上記すべて | 8 |

戻り値は `Opaque(grid/direction)` の列。列挙順序は決定的
（`"eight"` は `(-1,-1)` から行優先）。

`of` を取るのは、盤の座標系（軸の向き）に整合した方向を返すため。

### 個別の方向を書く手段

現状、`{ "op": "grid.directions" }` の集合からしか方向を得られない。
特定の 1 方向（駒の前方など）を指定する手段は未提供。→ 未確定事項。

リバーシは 8 方向すべてを等しく扱うため、これで足りている。

---

## `grid.set`

1 マスに書き込む。

### 形式

```jsonc
{
  "op": "grid.set",
  "target": <式:盤面>,
  "coord": <式:座標>,
  "value": <式>
}
```

**効果ノード。** `inputs.*.effects` の要素としてのみ出現できる。

### 適用規則

1. `target` / `coord` / `value` を **スナップショットに対して** 評価する
2. ドラフト上の該当盤面の `coord` に `value` を書き込む

`target` は書き込み先の盤面を指す式で、実際には `$board` のような
`state.get` になる。State プラグインの `path` と対応づけて、ドラフトの
どこへ書くかを解決する。

### 範囲外・`Null` 座標

**`grid.at` と異なり、評価時エラー。**

読みが寛容で書きが厳格、という非対称は意図的である。読みの `Null` は
「そこには何も無い」という有意味な答えになるが、書きの範囲外に対応する
有意味な動作は「無視する」しかなく、それはルールの誤りを静かに握り潰す。

リバーシの `place` は `when`（`canPlace`）で座標が盤内かつ空であることを確認済み
なので、この厳格さに抵触しない。

---

## `grid.setMany`

複数マスに同じ値を書き込む。

### 形式

```jsonc
{
  "op": "grid.setMany",
  "target": <式:盤面>,
  "coords": <式:Sequence>,
  "value": <式>
}
```

**効果ノード。**

### 適用規則

`coords` を列挙し、各座標へ `value` を書き込む。`value` は **1 回だけ評価** し、
全座標へ同じ値を書く（座標ごとに再評価しない）。

空列なら何もしない。範囲外・`Null` 座標は `grid.set` と同じくエラー。

### 例（リバーシ `inputs.place.effects`）

```jsonc
{ "op": "grid.setMany", "target": "$board",
  "coords": { "op": "def.call", "def": "flips", "args": { "at": "@at" } },
  "value": "#me" }
```

裏返し対象をまとめて自分の色にする。`coords` はスナップショットに対して評価
されるので、直前の `grid.set`（着手位置への書き込み）の影響を受けない。
これは [値モデル §5](../value-model.md) のスナップショット意味論が実際に
効いている箇所。

座標ごとに異なる値を書く手段（`grid.setEach` のようなもの）は未提供。リバーシ
では不要。→ 未確定事項。

---

## リバーシで使わなかった概念

Grid が意図的に持たない、あるいは未提供のもの。

- **隣接（`grid.neighbors`）** — `grid.ray` の `length: 1` で代替できる
- **領域・パターンマッチ** — 五目並べの「5 連」は `grid.ray` +
  `seq.takeWhile` で書ける想定
- **盤面の回転・反転** — 対称性を使った探索の最適化に有用だが、ルール記述には
  不要
- **複数盤面の関係** — 盤面が 2 つ以上ある状態（表裏など）は、State に
  フィールドを 2 つ持てば表現できる

---

## 未確定事項

- **`grid.atStrict`** — 範囲外をエラーにする厳格版。上述の代償への対処。
- **個別方向の指定** — 駒の前方のような特定方向を書く手段。将棋・チェスで
  必須になる。`{ "op": "grid.direction", "dx": 0, "dy": 1 }` のような形か。
- **手番相対の方向** — 「自分から見て前」は手番によって向きが変わる。
  Grid が手番を知るのは越権なので、RuleSet 側で `branch.match` するか、
  方向を反転する `grid.flip` を置くか。
- **`grid.setEach`** — 座標ごとに異なる値を書く効果。
- **非矩形の盤** — 六角盤、穴あき盤など。`width` / `height` の矩形前提を
  崩すことになるため、別プラグイン（`Rulealize.Plugin.HexGrid` など）に
  するのが妥当か。
- **座標の順序** — [Comparison](Comparison.md) の未確定事項と連動。
  `Opaque(grid/coord)` に順序を与えれば `seq.orderBy` で並べ替えられる。
- **`grid.coords` の絞り込み版** — `GetValidInputs` の候補数を domain の
  段階で減らす `grid.coordsWhere`。リバーシ（64 候補）では不要だが、
  より大きな盤や複数パラメータの入力では必須になる。
