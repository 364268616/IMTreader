# 广哥古籍阅读器

本机识读，原图对照。

把古籍扫描件变成可对照、可检索、可批注的文本。识别在本机完成，竖排按阅读顺序转为横排。

个人开发，开源免费，为爱发电。

[GitHub](https://github.com/364268616/IMTreader) · [Gitee](https://gitee.com/QinGuang/imtreader)

## 能做什么

- 本机识别 PDF 与图片，竖排按阅读顺序转为横排
- 原图与文本左右对照，点击互相定位，识别结果可改
- 繁简一键切换显示；检索时简繁、异体互通
- 高亮、书签、笔记与出处引用
- 拓片反白、框选补识、按页范围识别
- 竖排仿古书版式沉浸阅读
- 年号、干支与公元互转
- 导出全文、高亮摘录与笔记
- 可选接入大模型做标点、翻译与注释

默认引擎为随程序分发的 PaddleOCR PP-OCRv5（离线）。也可改用 Windows 内置 OCR，或自行配置 OpenAI 兼容的视觉模型。

## 运行环境

- Windows 10/11 x64（最低 1809 / 内部版本 17763）
- 安装包为自包含发布，**不必再单独安装 .NET**
- 从源码编译需要 [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)

本仓库含 OCR 模型，克隆后即可编译，无需再下载模型。

## 下载安装

给普通用户：到 [GitHub Releases](https://github.com/364268616/IMTreader/releases) 下载 `广哥古籍阅读器-1.0.0-安装包.exe`，双击安装。安装过程中请阅读并勾选同意[用户协议](用户协议.txt)。

- 默认安装到当前用户目录，一般不需要管理员权限
- 默认创建桌面快捷方式
- 卸载：开始菜单中的「卸载 广哥古籍阅读器」，或 Windows 的「应用和功能」
- 卸载不会删除您的文献库（`%LocalAppData%\IMTReader`），以免误删整理成果

Gitee 若因附件大小限制无法上传安装包，请使用 GitHub 下载。

## 从源码编译

```powershell
git clone https://github.com/364268616/IMTreader.git
cd IMTreader
dotnet build IMTReader.sln -c Release
```

可执行文件：

`src\IMTReader.App\bin\Release\net8.0-windows10.0.19041.0\广哥古籍阅读器.exe`

文献库、设置和日志写在 `%LocalAppData%\IMTReader`，不占用源码目录。

## 工程结构

```
src/IMTReader.App     界面（WPF）
src/IMTReader.Core    识别、文献库、检索、导出
tests/IMTReader.Tests 单元测试与 OCR 流水线测试
```

## 许可证与用户协议

- 源码许可证：[MIT](LICENSE)
- 安装与使用：[用户协议与免责声明](用户协议.txt)（导入文献的版权、识别误差、AI 与免责条款请仔细阅读）
- 第三方组件：[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

本地制作安装包（需已安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)）：

```powershell
powershell -ExecutionPolicy Bypass -File setup\build-release.ps1
```

产物在 `dist\` 目录，该目录不入库。

## 请我喝杯咖啡

这是我个人做的开源项目，一直免费。用得顺手的话，请我喝杯咖啡就好。不请也完全没问题。

<img src="收款码.jpg" width="220" alt="微信扫码请广哥喝杯咖啡">

邮箱：[364268616@qq.com](mailto:364268616@qq.com)
