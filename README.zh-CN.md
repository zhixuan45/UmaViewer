# Uma Viewer (2)

[English](README.md) | [简体中文](README.zh-CN.md)

⚠️ 如果你看到 **“Failed to load il2cpp”**、DLL 加载失败，或者程序无法在 Windows 上启动，可能是被 **Windows Smart App Control（智能应用控制）** 阻止了。

如果启动失败，请关闭 Smart App Control：

`Windows Security → App & browser control → Smart App Control settings → Off`

也就是：

`Windows 安全中心 → 应用和浏览器控制 → 智能应用控制设置 → 关闭`

这个问题通常是因为 Windows 阻止了未签名的程序。将程序移动到本地文件夹，或者关闭 Smart App Control 后，程序通常就可以正常启动。

Uma Viewer 是一个 Unity 应用程序，可以方便地查看《赛马娘 Pretty Derby》的游戏资源。

| 版本 | 支持情况 |
|------|----------|
| JP (DMM) | ✅ |
| JP (Steam) | ✅ |
| KR | ✅ |
| Global | ✅ |

---

## ⚠️ 🌍 EN / Global 用户 ⚠️

在 UmaViewer 的 **Other** 设置页面中，将 **WorkMode** 设置为 **Default**，并将 **Region** 设置为 **Global**。

目前只支持 Default 工作模式。你需要先在游戏设置中使用 **Download All** 按钮下载全部资源，Viewer 才能正常工作。

---

### 要求 / 安装

1. 运行 Viewer 前，需要安装 [Uma Musume: Pretty Derby](https://dmg.umamusume.jp/) 并完整下载游戏数据。

2. 根据你的游戏版本和更新情况，游戏数据可能存放在 **不同的位置**：

   - **DMM / Steam 旧版安装：**

     ```text
     C:\Users\<你的用户名>\AppData\LocalLow\Cygames\umamusume(?)\
     ```

   - **DMM / Steam 新版安装：**

     ```text
     ...\<Umamusume 安装目录>\Umamusume_Data\Persistent(?)\
     ```

   - 无论是哪一种情况，请确认目标文件夹中的文件结构大致如下：

     ```text
     Target Folder\
       meta
       master\
         master.mdb
       dat\
         2A\
         2B\
         ...
     ```

3. 在 [Releases](https://github.com/zuoy865-stack/UmaViewer/releases) 页面下载最新的 `UmaViewer.zip`。

4. 将压缩包解压到任意位置，也可以直接覆盖旧版本。

5. 运行 `UmaViewer.exe`。

6. UmaViewer 会尝试自动检测游戏数据文件夹。

   - 如果自动检测失败或出现错误，请前往：

     **Settings → Other → Change DataPath**

     然后手动选择目标文件夹。

---

## 开发者 / Contributors

1. 推荐使用 [Unity Hub](https://unity3d.com/get-unity/download) 和 [Unity Engine 2022.3.62f1](https://unity.com/releases/editor/archive)。

   较新的 Unity 2022.3.X 版本理论上也应该可以运行。

2. Clone 本仓库，或者下载并解压本仓库。

3. 使用 Unity Hub 导入并打开项目，缺失的文件通常会自动修复。

4. 打开：

   ```text
   Assets/Scenes/Version2
