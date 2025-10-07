# KAIR (Kernel Assembly IR)
**CPU・OSの差異を吸収するためだけのIR。**  
超単純なレジスタマシンとしての抽象化を与えたい。
- `[base + offset]` でアクセス、演算、終わり！笑
- 完全ナイーブ変換(各命令ごとにload→演算→store)。最適化は後でやります。
- 8/16byteアラインメントすら自己責任です。

## 依存ツールの準備
単体ではkirファイルからアセンブリを吐くだけなので、そのまま動かすには別途アセンブリのコンパイラ`NASM`とリンカ`GoLink`が要ります。
- **NASM** (<https://www.nasm.us/pub/nasm/releasebuilds/>)  
  `tools/nasm/` 直下にexeが来るように展開。
-  **GoLink** (<https://www.godevtool.com/> )  
  `tools/Golink/` 直下にexeが来るように展開。

## クイックスタート
```powershell
# dotnet runして、kir->asm->obj->exe->サブシステム変更まで
./build.ps1 samples/fibonacci.kir
# 実行して終了コードを見てみる
samples/fibonacci.exe ; echo "Exit code: $LASTEXITCODE" # 55

# 同じノリで`samples/` からkirを幾つか試せます。解説コメント付き
# Win11 + intelしか試してない
```

## ドキュメント
- **[文法メモ.md](文法メモ.md)** : 言語仕様の詳細（ころころ変わる）
- **[命令対応表.md](命令対応表.md)** : x64/ARM64での対応パターン
- **[CLAUDE.md](CLAUDE.md)** : メイン開発者Claude様のお台所

## 基本文法
```kir
// メモリアクセス
[address]                // 基本
s[0] = 10                // スタックアクセス用の糖衣 [sp + 0] = 10
d[8] = 20                // データセクション用の糖衣 [data + 8] = 20
c[16] = 30               // 静的データ用の糖衣 [const + 16] = 30 (init only)

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
syscall ExitProcess, 42                    // 終了コード42で終了
s[0] = syscall GetStdHandle, -11           // 標準出力ハンドル取得
```

## 検証環境

- ハード:
  - intel 12th CPU
  - Windows 11
- ソフト:
  - .NET 8.0
  - NASM 3.00
  - GoLink 1.0.4.6

## ライセンス

このプロジェクトは [MIT License](LICENSE) の下で配布されます。

### 余談
Claude Code神だけど金が溶けて危ない。