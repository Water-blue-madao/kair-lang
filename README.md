# KAIR (Kernel Assembly IR)
**CPU・OSの差異を吸収するためだけのIR。**  
超単純なレジスタマシンとしての抽象化を与えたい。
- `[base + offset]` でアクセス、演算、終わり！笑
- 完全ナイーブ変換(各命令ごとにload→演算→store)。最適化は後でやります。
- 8/16byteアラインメントすら自己責任です。

⚠ **まだアルファ版です！**  
- 機能不全です。  
- 検証不全です。  
- ベータに到達するかもわかりません！  

claude様のお戯れに過ぎないのさ

## 依存ツールの準備

- **.NET 8.0** (<https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0>)
- **LLVM Toolchain** (llvm-mc, lld-link)
  公式リリースから入手: <https://github.com/llvm/llvm-project/releases>
  `tools/llvm/bin/` に以下のファイルを配置（約100MB、DLL不要）:
  - `llvm-mc.exe` (アセンブラ)
  - `lld-link.exe` (リンカ)
  - `llvm-dlltool.exe` (Import library生成用)

  `tools/llvm/lib/kernel32.lib` も必要（llvm-dlltoolで生成可能）

## クイックスタート
```powershell
# ビルドのみ
./build.ps1 samples/fibonacci.kir

# ビルド＆実行＆終了コード表示を一発で
./build.ps1 samples/fibonacci.kir -Run  # Exit code: 55

# 全サンプルをテスト
./workspace/test-all.ps1

# 同じノリで`samples/` からkirを幾つか試せます。解説コメント付き
# Win11 + intelしか試してない
```

## ドキュメント
- **[文法メモ.md](文法メモ.md)** : 言語仕様の詳細（ころころ変わる）
- **[命令対応表.md](命令対応表.md)** : x64/ARM64での対応パターン
- **[CLAUDE.md](CLAUDE.md)** : メイン開発者の台所

## 基本文法
```kir
// メモリアクセス
[address]                // 基本
s[0] = 10                // スタックアクセス用の糖衣 [sp + 0] = 10
d[8] = 20                // データセクション用の糖衣 [data + 8] = 20
c[16] = 30               // 静的データ用の糖衣 [const + 16] = 30 (init only)

// スタック操作
// 重要: アセンブリと同じく、sp を減らすことでスタックを確保します
// x64/ARM64 ではスタックは下方向（低位アドレス）に成長します
sp -= 40                 // スタックを40バイト確保（RSP を減らす）
sp += 16                 // スタックを16バイト解放（RSP を増やす）

// アラインメント（システムコール前に必須）
align 16                 // スタックを16バイト境界に整列

// ベースアドレス値（システムコール用）
s[8] = data              // データセクションのアドレスを取得
s[8] += 32               // アドレスにオフセットを加算

// 演算
s[0] = s[8] + s[16]      // 加算
s[0] = s[8] - s[16]      // 減算
s[0] = s[8] * s[16]      // 乗算
s[0] = s[8] /s s[16]     // 符号付き除算
s[0] = s[8] & s[16]      // AND
s[0] = s[8] << s[16]     // 左シフト

// 制御フロー
# loop
s[0] -= 1
goto loop if s[0] >s 0   // 条件付きジャンプ
goto END                 // プログラム終了

// システムコール
align 16
syscall ExitProcess, 42                    // 終了コード42で終了
align 16
s[0] = syscall GetStdHandle, -11           // 標準出力ハンドル取得
```

## その他
- 検証環境 :  
  - intel 12th CPU  
  - Windows 11  
- ライセンス : [MITライセンス](LICENSE)  
-  余談 : Claude Code神だけど金が溶けて危ない。