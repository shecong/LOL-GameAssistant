# 增幅 OCR 离线对比

运行：`dotnet run --project tools/OcrBenchmark -- E:/code/weather-app`

历史 Tesseract 对比工具，生产程序已改用 SimdPaddleOCR。本工具保留 Tesseract 5.2.0 和移动到 tessdata/ 的旧中英文模型，不参与应用发布；名称匹配器仍链接当前实现。旧 results-after/production.json 是迁移前生产代码的历史记录，当前生产验证请使用 ../SimdOcrBenchmark。输入位置及人工核对的标题保存在 samples.json；截图目前引用用户提供的本机临时文件，文件移动后需要更新 Path。

窗口截图人工标注客户区为 (8,31,2544,1353)，全屏截图为 (0,0,2544,1353)。这是离线评估的输入标注，不代表新增自动窗口检测。截图标题按人工观察填写，没有根据 OCR 输出生成答案。

比较现有扫描顺序、提前停止、候选歧义检查和去重逻辑，以及标题高度范围 40%～43%、宽度 15% 或 10% 的预处理组合。窄裁剪参数是在此批截图上探索的，需要其他分辨率及 UI 缩放样本验证。

原图、原有反色对比度处理、固定亮度阈值 150、局部均值阈值（31×31 窗口，亮度超过局部均值 20 判为文字）分别搭配 SparseText、SingleLine、SingleBlock。未测试 PaddleOCR、颜色分类、多帧确认或稀有度过滤。

results/details.json 保存各卡片 OCR 原文、OCR 平均置信度、名称匹配分及最终名称；results/summary.json 保存汇总。名称匹配分不是 OCR 置信度。

results/ 是修改前的探索结果，results-after/ 是匹配规则及生产流程修改后的结果。复测时请传入独立输出路径，避免覆盖历史结果：`dotnet run --project tools/OcrBenchmark -- E:/code/weather-app E:/code/weather-app/tools/OcrBenchmark/results-after`。

计时包含裁剪、放大、预处理、PNG 转换、OCR 和名称匹配，不含引擎初始化、截图及推荐数据加载。预处理实验使用 GetPixel/SetPixel，性能数字仅供本机实验比较。组合方案通过已有单项结果离线模拟回退，耗时为对应步骤的测量值之和。ExactText 是整个 OCR 输出规范化后等于标题的数量；若输出有其他字母，即使标题本身正确也不计入，因此不能直接解释为标题字符准确率。

这批仅有 4 张截图、12 张卡片：黄金 6 张、棱彩 6 张，没有白银、无卡片画面和多帧序列。同一批用于探索和评估，没有独立验证集，不代表整体准确率。
