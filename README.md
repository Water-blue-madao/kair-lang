# KAIR (Kernel Assembly IR)

**CPUとOSの差異だけを吸収する雑IR。** 

超単純なレジスタマシンとしての抽象化を与えたい。
- `[base + offset]` でアクセス、演算、終わり！笑
- 完全ナイーブ実装(各命令ごとにload→演算→store)。最適化は後でやります。
- アラインメントすら自己責任です。

## クイックスタート
```bash
# ビルド
dotnet build KAIR/kairc/kairc.csproj -c Debug -r win-x64 --self-contained false

# 使う
./build.ps1 hello.kir # ビルド（KIR → EXE）
./hello.exe # 実行
echo "Exit code: $LASTEXITCODE"  # 30

# `samples/` フォルダに各 `.kir` ファイルがあります。
# 冒頭コメントに解説が書かれてます。
./build.ps1 samples/test-operators.kir
./samples/test-operators.exe
```

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

## ドキュメント

- **[文法メモ.md](文法メモ.md)** : 言語仕様の詳細（ころころ変わる）
- **[命令対応表.md](命令対応表.md)** : x64/ARM64での対応パターン
- **[CLAUDE.md](CLAUDE.md)** : メイン開発者Claude様のお台所


## 検証環境

- ハード:
  - intel 12th CPU
  - Windows 11
- ソフト:
  - .NET 8.0
  - NASM 3.00
  - GoLink 1.0.4.6

## ライセンス

（未定）

