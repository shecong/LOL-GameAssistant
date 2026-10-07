# SimdPaddleOCR 增幅识别验证

运行：`dotnet run --project tools/SimdOcrBenchmark -c Release -- E:/code/weather-app`

输入使用 ../OcrBenchmark/samples.json 的四张原始截图和人工标题标注，截图目前位于用户提供的临时路径。工具比较 Tiny 和 Small 对原始窄标题区域的识别，之后直接编译生产 AugmentImageRecognizer / AugmentOcrEngine，对完整中央截图执行生产流程。`--production-only` 跳过模型对比。模型、核心版本固定为 1.0.0、1.4.2。

results/comparison.json 保存逐卡结果；results/production.json 保存直接调用生产代码的结果。空画面及取消操作也会验证；生产结果不符合标注或验证失败时返回非零退出码。只有存在模型对比结果时才覆盖 comparison.json。

单文件验证命令：

```powershell
dotnet publish tools/SimdOcrBenchmark -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/ocr-single-file
```

把生成的 exe 单独复制到隔离目录；输入目录只需按原目录结构放置 samples.json 和 augment-names.json，无需 OCR DLL、ONNX 文件或 tessdata。调用 exe 并传入该输入目录与 --production-only。runtime.json 记录 OCR 程序集是否位于单文件内，以及整个测试进程工作集峰值（不等于模型的增量内存）。

results/report.md 记录本次结论。四张图只有黄金和棱彩，同一批用于选择模型及回归，尚无独立验证集或白银卡片，不代表整体准确率。
