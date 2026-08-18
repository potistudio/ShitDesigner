# VJプロジェクトの保存と復旧

## 状態

プロジェクト構造、形式バージョン、原子的保存、バックアップおよび破損復旧方式は確定。

## フォルダー構造

```text
ProjectName/
├─ project.json
├─ Assets/
│  └─ {MediaAssetId}/source.ext
└─ Backups/
```

- `project.json` はUTF-8 JSONとし、初期 `ProjectFormatVersion` は整数 `1` とする。
- ノード、接続、BaseValue、論理コントロール、ControlMapping、プリセット、素材メタデータおよびUI配置を含める。
- `EffectiveValue`、GPUリソース、診断履歴およびUndo履歴は保存しない。
- 保存順に意味がない辞書や接続は安定IDで正規化し、差分を再現しやすくする。

## 保存

- 同じフォルダーへ `project.json.tmp` を完全に書き、読戻し検証に成功してから置き換える。
- 置換前の正常な `project.json` を `project.json.bak` として1世代保持する。
- 保存失敗時は既存 `project.json` を変更せず、一時ファイルを診断対象として残す。
- 素材コピー完了前に、その素材参照を含むマニフェストを確定しない。

## 読込と復旧

- `project.json` の構文、ProjectFormatVersion、必須IDおよび参照を読込前に検証する。
- 主ファイルが破損し `.bak` が有効な場合は、バックアップをメモリへ読み込んだ `Recovered` 状態で開く。
- `Recovered` プロジェクトはDirtyとし、主ファイルを自動上書きしない。
- `.bak` も無効ならプロジェクトを開かず、両方の診断を表示する。
- ノード単位の未知型や移行失敗はUnknownNodeへ隔離し、プロジェクト全体は開く。

## 設計意図

- 保存途中の終了で最後の正常プロジェクトを失わない。
- JSONの可読性と安定順序によって、個人開発で差分を追いやすくする。
- 復旧読込だけで破損ファイルを上書きしない。
