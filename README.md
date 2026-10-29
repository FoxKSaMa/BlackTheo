# BlackTheo

把 Celeste 里的 Theo 水晶替换成一个带引力透镜和吸积盘的黑洞，并额外提供可选的“玛德琳黑洞”效果。

适用于 Celeste 1.4.0.0（Everest / FNA，Windows x64）。

- 当前版本：**1.3.1**
- GitHub：[FoxKSaMa/BlackTheo](https://github.com/FoxKSaMa/BlackTheo)
- 下载：[Releases](https://github.com/FoxKSaMa/BlackTheo/releases)

## 效果

### Theo 黑洞

- 隐藏原版 `theo_crystal` 精灵和白色 `VertexLight`
- 在 Theo 的位置绘制事件视界、光子环和吸积盘
- 吸积盘不是贴图椭圆，而是逐像素积分光线后记录与盘面的交点：
  - 近侧盘面从黑洞前方穿过，形成细带
  - 远侧盘面被引力弯折到阴影上下，形成弧
  - 盘面有程序化流线、噪声和多普勒明暗
- 屏幕空间引力透镜会扭曲背景，但不会影响 HUD
- 支持多个 Theo，最多同时处理 4 个
- 死亡、重生、切板、持有、掉落都会自动跟随或清理

### 玛德琳黑洞模式

- **关闭**：不显示
- **自选**：固定强度 0–10
- **速度**：强度随玛德琳速度变化
- 径向对称的透镜，没有切向漩涡
- 中心黑色核心会随强度平滑变大，最大半径 16px
- 冲刺冻结、抓水母/Theo、撞墙等临时零速状态会沿用之前的强度，不会瞬间闪烁消失

## 安装

1. 从 [Releases](https://github.com/FoxKSaMa/BlackTheo/releases) 下载 `BlackTheo-1.3.1.zip`
2. 解压出 `BlackTheo` 文件夹
3. 放到：

```text
<Celeste>/Mods/BlackTheo
```

4. 完全退出 Celeste 再启动

需要安装 Everest。当前 `everest.yaml` 要求 Everest `1.5986.0` 或更高。

> 更新 DLL 后必须完全重启游戏；只在房间里重载不会重新加载 C#。

## 选项

在主菜单或游戏内的 **Mod Options → BlackTheo** 中调整。

| 选项 | 可选值 | 默认 |
|---|---|---|
| 开关 | 开 / 关 | 开 |
| 黑洞引力透镜强度 | 0–10（0 关闭，2 为 Low，10 为 High） | 2 |
| 吸积盘颜色 | 蓝色 / 冰蓝 / 红橙色 / 紫色 / 幽绿色 / 尘土黄 / 白色 / 粉色 / 棕色 / 深红色 / 灰色 / 自定义 | 蓝色 |
| 自定义吸积盘颜色 | 主菜单输入，见下文 | `FF7A45` |
| 多普勒效应 | 开 / 关 | 开 |
| 星尘 | 开 / 关 | 开 |
| 玛德琳黑洞模式 | 关闭 / 自选 / 速度 | 关闭 |
| 玛德琳透镜强度 | 0–10 | 2 |

### 黑洞引力透镜强度

- `0`：关闭背景扭曲，但黑洞和吸积盘仍显示
- `2`：原来的 Low
- `10`：原来的 High
- 中间档位会连续插值强度、影响半径、衰减和边缘过渡

### 多普勒效应

只改变亮度，不改变色相：

- 朝向观察者的一侧更亮
- 远离的一侧更暗
- 整体平均亮度保持 1.0，不会让整张盘变暗

### 星尘

事件视界内部会漂浮少量彩色像素点，颜色跟随当前盘色。关闭后完全隐藏。

### 玛德琳速度曲线

| 玛德琳速度（px/s） | 玛德琳透镜档位 |
|---|---|
| 0–200 | 0（不新增透镜） |
| 200–800 | 0 → 4 |
| 800–1300 | 4 → 6 |
| 1300–3000 | 6 → 10 |
| > 3000 | 10 |

- 档位是连续浮动的，不会在分段边界跳变
- 速度下降时会平滑淡出
- 冲刺冻结帧会保持冻结前的强度
- 抓水母、抓 Theo、撞墙等短暂零速不会让透镜瞬间消失

## 自定义颜色

自定义颜色使用和 MoreDasheline 相同的普通字符串设置：

1. 打开 **主菜单 → Mod Options → BlackTheo**
2. 选择 **自定义吸积盘颜色**
3. 用游戏键盘输入颜色

支持格式：

```text
RRGGBB
RRGGBBAA
#RRGGBB
#RRGGBBAA
```

- `#` 可省略
- 8 位格式的最后两位是 alpha
- 非法输入会回退到冰蓝色 `#50B4FF`
- 该字符串选项只在主菜单显示；游戏内 Everest 会禁用文本输入，所以游戏内请使用预设颜色

## 构建

需要：

- Windows x64
- .NET 8 SDK
- Celeste + Everest 的 DLL
- 本机有 `d3dx9_43.dll`（Windows 自带）

设置 Celeste 路径：

```powershell
$env:CELESTE_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Celeste'
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Deploy
```

也可以显式传入：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 `
  -CelestePath 'C:\Program Files (x86)\Steam\steamapps\common\Celeste' -Deploy
```

脚本会自动尝试常见的 Steam 安装位置。构建结果在：

```text
dist/BlackTheo
```

运行时不读取 `Effects/`；shader 已经以 `Resources/BlackHole.cso` 的形式嵌入 DLL，因此发布包里不需要 `Effects` 文件夹。

## 项目结构

```text
BlackTheo/
├─ src/
│  ├─ BlackHole.fx                    # 光线积分、盘面、透镜、星尘
│  ├─ BlackHole.cso                   # 编译后的 FX9 bytecode
│  └─ BlackTheo/
│     ├─ BlackHoleConfiguration.cs    # 设置、档位、调色板
│     ├─ BlackHoleComponent.cs        # 每个 Theo 的组件
│     ├─ BlackHoleRenderer.cs         # RT、Theo/玛德琳跟踪、参数
│     ├─ BlackTheoModule.cs           # Everest Hook
│     ├─ HexColor.cs                  # HEX → RGBA
│     ├─ Dialog/                      # 中英文选项文本
│     └─ Resources/BlackHole.cso      # 嵌入资源
├─ tools/EffectCompiler/              # 不依赖 Windows SDK 的 fxc 替代
├─ docs/                              # 实现原理文档和示意图
├─ build.ps1
├─ LICENSE
└─ README.md
```

## 实现说明

更完整的原理解说在：

- [实现原理（PDF）](docs/BlackTheo-实现原理.pdf)
- [实现原理（Markdown）](docs/BlackTheo-实现原理.md)
- [实现原理（HTML）](docs/BlackTheo-实现原理.html)

文档主要解释渲染管线、RenderTarget ping-pong、光线积分、吸积盘交点和屏幕空间透镜；部分细节以早期版本为例，最新选项以本 README 为准。

## 参考

- [sayed1145/glsl-black-hole](https://github.com/sayed1145/glsl-black-hole)（MIT）：盘面亮度和流线参考
- [Gitee：black-hole-recycling-station](https://gitee.com/the-journey-ahead-is-long/black-hole-recycling-station)：屏幕空间偏折透镜参考
- Luminet (1979)、James et al. (2015)：薄盘透镜几何参考

## 许可证

MIT，见 [LICENSE](LICENSE)。
