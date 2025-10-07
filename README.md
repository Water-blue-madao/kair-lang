# KAIR (Kernel Assembly IR)

**クロスプラットフォーム対応のアセンブリ抽象レイヤー**

KAIRは、x64とARM64の差異を吸収する最小限のIR言語です。完全ナイーブ実装により、各命令ごとにload→演算→storeを完結し、バグを最小化します。

## 特徴

- ✅ **完全ナイーブ実装**: 最適化なし、デバッグ容易
- ✅ **Windows x64完全対応**: 呼び出し規約、スタックアライメントを正しく実装
- ✅ **統一メモリアクセス**: `[base + offset]` 構文でシンプルに
- ✅ **動作確認済み**: 各種演算、システムコールが動作

## クイックスタート

### インストール

```bash
dotnet build KAIR/kairc/kairc.csproj -c Debug -r win-x64 --self-contained false
```

### 最初のプログラム

**hello.kir**:
```kir
// データ初期化
[data + 0] = 10
[data + 8] = 20

// 演算
s[0] = d[0] + d[8]    // 10 + 20 = 30

// 終了（終了コードとして30を返す）
goto END
```

### ビルド・実行

```bash
# ビルド（KIR → EXE）
./build.ps1 hello.kir

# 実行
./hello.exe
echo "Exit code: $LASTEXITCODE"  # 30
```

## 基本文法

### メモリアクセス

```kir
s[0] = 10              // スタック: [sp + 0] = 10
d[8] = 20              // データセクション: [data + 8] = 20
c[16] = 30             // 定数セクション: [const + 16] = 30（初期化時のみ）
```

### 演算

```kir
s[0] = s[8] + s[16]    // 加算
s[0] = s[8] - s[16]    // 減算
s[0] = s[8] * s[16]    // 乗算
s[0] = s[8] /s s[16]   // 符号付き除算
s[0] = s[8] & s[16]    // AND
s[0] = s[8] << s[16]   // 左シフト
```

### 制御フロー

```kir
# loop
s[0] -= 1
goto loop if s[0] >s 0   // 条件付きジャンプ

goto END                  // プログラム終了
```

### システムコール

```kir
syscall ExitProcess, 42                    // 終了コード42で終了
s[0] = syscall GetStdHandle, -11           // 標準出力ハンドル取得
```

## ドキュメント

- **[文法メモ.md](文法メモ.md)**: 言語仕様の詳細（頻繁に更新）
- **[命令対応表.md](命令対応表.md)**: x64/ARM64での命令実装パターン
- **[CLAUDE.md](CLAUDE.md)**: プロジェクト構成、アーキテクチャ、開発ガイド

## サンプル

サンプルプログラムは `samples/` フォルダにあります。各 `.kir` ファイルの冒頭コメントに、狙い・解説・期待される結果が記載されています。

```bash
./build.ps1 samples/test-operators.kir
./samples/test-operators.exe
```

## 動作環境

- Windows 11
- .NET 8.0
- NASM 3.00
- GoLink 1.0.4.6

## ライセンス

（未定）


