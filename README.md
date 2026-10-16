# 听转 · 批量音频转换器

一个 Windows 桌面工具，用于将本地 NCM、FLAC、WAV、M4A 等音频批量转换成 MP3。拖入文件或文件夹即可开始，不需要登录音乐平台。

**[下载 Windows 版本](https://github.com/3339868874-web/yrf/releases/latest/download/tingzhuan-windows-x64.zip)** · [所有发行版](https://github.com/3339868874-web/yrf/releases) · [构建进度](https://github.com/3339868874-web/yrf/actions)

首次上传后，GitHub Actions 会自动测试、构建并发布下载包。若下载暂时不可用，请先查看构建进度。

## 功能

- 支持 `.ncm`、`.flac`、`.wav`、`.mp3`、`.m4a`、`.aac`、`.ogg`、`.opus`、`.wma`、`.aiff`、`.aif`、`.ape`。
- 文件夹递归扫描、重复输入去重、状态与总进度显示。
- V0、320、192、128 kbps 输出品质可选。
- 已有 MP3，以及 NCM 内本来就是 MP3 的音频，复制音频流，避免重复压缩。
- 原文件保持不变，重名输出自动编号，支持停止与导出记录。
- 尽量保留可识别的标题、歌手、专辑文字标签；当前版本不保留封面或歌词。
- 音频处理在本机完成，运行时不上传音频。

## 使用

运行环境：Windows 10 / 11 x64、系统自带 .NET Framework 4.x。

下载发行版后，解压并打开 `听转音频转换器.exe`。添加文件、选择输出目录，再点击“开始批量转换”。发行版包含转换引擎，不需要另装 Python 或 FFmpeg。首次运行会向 Windows 临时目录展开引擎。

转换到 MP3 是有损转码，不能提升源音频质量。已为 MP3 的输入不受品质选项影响。再次点击开始会重新处理列表中的全部文件。

## 从源码构建

需要 Windows x64、Python 3.12、.NET Framework C# 编译器。

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-build.txt
.\build.ps1 -PythonPath .\.venv\Scripts\python.exe
```

产物：`dist\听转音频转换器.exe` 和 `dist\听转-windows-x64.zip`。源码不包含 FFmpeg 二进制；构建时从已安装的 imageio-ffmpeg 包中取用，相关许可随发行包提供。

## 测试

```powershell
$env:PYTHONPATH = (Resolve-Path src).Path
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

覆盖 WAV/FLAC 转码、MP3 音频保持、NCM 的 MP3/FLAC 载荷、中文路径、封面预留区、重名保护、损坏输入及取消清理。NCM 使用生成样本测试，尚不保证所有格式变体或未来版本都兼容。

此前本地发行版已通过原生窗口启动检查，以及打包引擎转换后 MP3 的解码检查。

## 项目结构

```text
src/Window.cs     Windows Forms 界面
src/engine.py     音频转换与 NCM 解析
src/worker.py     转换任务进程
tests/           自动化测试
build.ps1        Windows 构建脚本
licenses/        第三方许可
```

## 许可与来源

本项目自编写代码采用 [MIT 许可](LICENSE)。第三方组件保留各自许可，包括 FFmpeg GPLv3 构建、CPython、cryptography 和 PyInstaller bootloader，详见 [组件说明](THIRD_PARTY_NOTICES.txt) 与 `licenses/`。

NCM 容器解析参考公开格式资料：[taurusxin/ncmdump](https://github.com/taurusxin/ncmdump)、[allenfrostline/pyNCMDUMP](https://github.com/allenfrostline/pyNCMDUMP)。本项目不是网易云音乐官方产品，请仅处理你有权转换的音频。
