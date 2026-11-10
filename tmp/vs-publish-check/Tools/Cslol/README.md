# 文件换肤运行组件

运行时使用本目录的 `mod-tools.exe`、`cslol-dll.dll` 和 `hashes.game.txt`，通过外部进程执行 import / mkoverlay / runoverlay。其他三个工具只用于资源制作。

从仓库根目录执行 `./tools/prepare-cslol-resources.ps1 -SourceResources 'C:\app\PLskin\resources'`，准备本机集成文档所指的资源。脚本生成工具 SHA256 清单；大文件不进入 Git。构建和发布会把已准备资源复制到输出目录。

上游项目：[LeagueToolkit/cslol-manager](https://github.com/LeagueToolkit/cslol-manager)。本项目没有修改工具二进制。此处是本机已有组件的集成入口，公开发行这些组件前应核对所用版本的许可证和分发条件。
