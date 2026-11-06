# 资源包加载组件

`HID.dll` 来自用户提供的 `E:\迅雷下载\换肤\HID.dll`，本地文件 SHA256：

`E4FBBF9671303712A764D505D9E9A6C5FAE7004873899255AB798F759D23E69B`

通过 `tools/prepare-resource-skins.ps1 -Source <HID.dll 路径>` 准备；构建和发布自动复制至同名 Tools 目录。

点击「安装加载组件」或首次挂载时，助手仅在对局外将经过哈希校验的文件复制到所选 Game 目录。已有不同内容的 HID.dll 会被保留并报错，不覆盖。资源清理不删除加载 DLL。仅安装文件，不修改注册表或游戏的其他 DLL；兼容性需实机验证。
