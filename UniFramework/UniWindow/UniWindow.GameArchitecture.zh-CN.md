## UniWindow + YooAsset 游戏架构设计说明（对话整理版）

> 本文基于与作者的一轮完整对话整理而成，目标是：  
> **把 UniWindow 的源码意图、使用方式、UI 分层规划、YooAsset 场景管理方案、以及一个传统单机游戏的整体架构，系统、完整地记录下来。**  
> Unity 版本约定为：**2022.3.61f1c1**。

---

## 0. 目标与背景

- 做一款 **传统单机游戏**，包括：
  - 主界面：`New Game / Load / Save / Settings / Exit`
  - 游戏内 HUD：显示玩家信息 + 返回主菜单、Save/Load/Settings 按钮
  - Save / Load 对话框 + MessageBox 确认
  - Settings 窗口：全屏/音量/多语言
  - 将来可能有 Guide、Loading 等特殊 UI 层
- 希望：
  - 使用 **UniWindow** 管 UI 窗口的创建/销毁/层级/显隐；
  - 使用 **YooAsset** 管资源和场景加载（Offline 模式即可）；
  - 使用 **Persistent 场景 + 其他场景 Additive** 的结构；
  - HUD 作为“全局 UI”，可以跨场景存在。

---

## 1. UniWindow 源码架构与设计意图

### 1.1 `UIWindow`：纯逻辑窗口基类

关键点（来源：`Runtime/UIWindow.cs`）：

- **不继承 `MonoBehaviour`**，只是一个普通 C# 抽象类：
  - 生命周期由 `UniWindow` 驱动，而不是 Unity 组件系统。
- 通过 YooAsset 异步加载一个带 `Canvas + GraphicRaycaster` 的 Prefab：
  - 加载完成后，实例化到 `UniWindow.Desktop` 下；
  - 用 `_panel` 字段持有实例根 GameObject；
  - 对外暴露 `transform` 和 `gameObject` 属性，模拟 MonoBehaviour 的常见访问方式。
- 生命周期接口：
  - `OnCreate()`：只调用一次，用来做节点绑定、事件注册等**一次性初始化**。
  - `OnRefresh()`：每次窗口准备完毕 / 再次“激活”时调用，用来刷新 UI 显示。
  - `OnUpdate()`：仅在 `IsPrepare == true` 的帧更新逻辑。
  - `OnDestroy()`：在窗口销毁前调用，用来释放资源、解绑事件。
  - 扩展钩子：`OnSortDepth(int depth)`、`OnSetVisible(bool visible)`。
- 窗口元信息：
  - `WindowName`：使用 `type.FullName`。
  - `WindowLayer`：来自 `[WindowAttribute]`。
  - `FullScreen`：来自 `[WindowAttribute]`。
  - `UserData` / `UserDatas`：打开窗口时传入的参数数组。

### 1.2 `UniWindow`：静态窗口管理器

关键点（来源：`Runtime/UniWindow.cs`）：

- 核心字段：
  - `_isInitialize`：是否已初始化。
  - `_driver`：`UniWindowDriver` 挂载的 GameObject，`DontDestroyOnLoad`。
  - `_stack`：`List<UIWindow>`，作为**窗口栈/有序列表**。
  - `Desktop`：所有窗口实例的父节点 GameObject（通常是 UI 根 Canvas 下的一个子节点）。

- 初始化：
  - `Initalize(GameObject desktop)`：
    - 创建 `[_UniWindow_]` GameObject；
    - 添加 `UniWindowDriver` 组件；
    - `DontDestroyOnLoad`；
    - 保存 `Desktop` 引用。
  - `Destroy()`：
    - `CloseAll()`；
    - 销毁 `_driver`。

- 更新：
  - `Update()`：
    - 遍历 `_stack`，调用每个窗口的 `InternalUpdate()`；
    - 在遍历过程中若 `_stack` 数量发生变化，会 break，避免并发修改。

- 打开窗口：
  - `OpenWindowAsync<T>(string location, params object[] userDatas)`：
    - 若窗口已存在：
      - `GetWindow` 取实例；
      - 从 `_stack` 中 `Pop` 再 `Push`，把它挪到该层顶部；
      - 调用 `TryInvoke(OnWindowPrepare, userDatas)` 触发刷新；
      - 返回绑定该窗口已有 `AssetHandle` 的 `OpenWindowOperation`。
    - 若窗口不存在：
      - `CreateInstance(type)` + `Push(window)`；
      - `window.InternalLoad(location, OnWindowPrepare, userDatas)`，通过 YooAsset 加载 Prefab；
      - 返回新构建的 `OpenWindowOperation`。
  - `OpenWindowSync`：在 `OpenWindowAsync` 基础上调用 `WaitForAsyncComplete()`。

- 关闭窗口：
  - `CloseWindow<T>()` / `CloseWindow(Type)`：
    - 查找窗口实例；
    - `InternalDestroy()` 销毁 `_panel` 与释放 Asset；
    - `Pop(window)` 将其从 `_stack` 移除；
    - 重新排序该层的深度、重算可见性。
  - `CloseAll()`：
    - 逐个 `InternalDestroy()`；
    - 清空 `_stack`。

### 1.3 窗口栈、WindowLayer 与 Depth 规则

- `_stack` 的插入规则（`Push`）：
  - 若已有同名窗口则抛异常（防止重复实例）；
  - 优先插入到同一 `WindowLayer` 的最后一个窗口之后；
  - 若没有同层窗口，则按 `WindowLayer` 数值，在比它小的层之后插入；
  - 全都没有就插入到 0。
- `Depth` 计算（`OnSortWindowDepth(int layer)`）：
  - 在 `_stack` 中遍历所有同一 `WindowLayer` 的窗口：
    - 深度初始值为 `layer`；
    - 每遇到一个窗口：
      - 设 `window.Depth = depth`；
      - 然后 `depth += 100`。
  - 目的：
    - **同一层的窗口之间预留至少 100 的 sortingOrder 间隔**；
    - 避免内部子 Canvas 的 +5 累积后越界到其它窗口的深度范围。
- 单个窗口内部的 Canvas depth：
  - 父 `Canvas.sortingOrder = Depth`；
  - 所有子 `Canvas` 在此基础上依次 +5：
    - 保证窗口内部层级清晰；
    - 给后续增加新的内部 Canvas 预留足够空间。

### 1.4 Layer 与 GraphicRaycaster 的显隐控制

`UIWindow.Visible` 的实现逻辑（简化描述）：

- `WINDOW_SHOW_LAYER = 5`（Unity 默认 UI 层）。
- `WINDOW_HIDE_LAYER = 2`（Unity 默认 Ignore Raycast 层）。
- `Visible = true` 时：
  - 主 Canvas 与所有子 Canvas 的 Layer 设为 `UI`；
  - `Interactable = true` → 打开所有 `GraphicRaycaster`。
- `Visible = false` 时：
  - Layer 切到 `Ignore Raycast`；
  - `Interactable = false` → 关闭所有 `GraphicRaycaster`。

设计意图：

- Layer 决定渲染与射线检测的大方向；  
  Raycaster 决定“UI 是否参与事件系统”。
- 即使在某些相机中依然渲染了 `Ignore Raycast` 层，**只要 Raycaster 关闭，也不会被点击**。
- 在大量隐藏窗口时，关闭 Raycaster 可以减少 UI 射线检测的开销，提升性能。

### 1.5 全屏窗口与遮挡规则

`OnSetWindowVisible()` 的核心逻辑：

- 从 `_stack` 的末尾（栈顶）开始往前遍历：
  - 第一遇到的窗口设为 `Visible = true`；
  - 若该窗口 `FullScreen == true` 且 `IsPrepare == true`，则将标志 `isHideNext = true`；
  - 之后所有窗口都设为 `Visible = false`。

含义：

- 栈顶的窗口总是显示；
- 当栈顶窗口是一个**已准备好的全屏窗口**时：
  - 其下方的所有窗口都被逻辑上“隐藏”；
  - 从视觉与交互上看，就像被完全盖住。

这正好适合：

- 主菜单全屏盖住游戏 HUD；
- Save / Load / Settings 全屏盖住下层的按钮，避免误触；
- Loading / 过场全屏遮住一切。

---

## 2. UI 分层规划与典型窗口集合

### 2.1 传统单机游戏的窗口层级规划

结合对话中提出的需求，一套推荐的 `WindowLayer` 规划如下：

| 用途                     | 建议 WindowLayer | FullScreen | 说明 |
|--------------------------|------------------|-----------|------|
| 主界面 MainMenu          | 100              | true      | 游戏启动后的主菜单界面 |
| 游戏内 HUD               | 200              | false     | 游戏内常驻信息与按钮，与场景内容共存 |
| Save / Load / Settings   | 300              | true      | “弹窗层”，从主菜单或游戏中都可打开 |
| MessageBox（确认对话框） | 400              | false     | 通用确认弹窗，浮在任何弹窗上方 |
| Guide / 教程引导         | 600              | true/false| 视需求：强制引导或仅作提示 |
| Loading / 过场界面       | 900              | true      | 全屏加载界面，遮挡一切 |

**注意：**

- 数值间隔足够大（间隔至少 100），便于未来插入更多层级；
- `WindowLayer` 数值越大，处于 `_stack` 中的深度倾向越靠前（同层再通过 +100 调整具体排序）。

### 2.2 Prefab 结构约定

#### 2.2.1 UIRoot 与 Desktop

- Persistent 场景中：
  - 创建一个 Canvas：
    - 组件：`Canvas + CanvasScaler + GraphicRaycaster`；
    - `Render Mode = Screen Space - Overlay`；
    - `CanvasScaler` 设置参考分辨率。
  - 在 Canvas 下创建子节点 `Desktop`：
    - 组件：`RectTransform`，Anchor = Stretch，全屏。
  - 在场景启动时：
    - 将整体 UIRoot（包含 Canvas / Desktop / Launcher）`DontDestroyOnLoad`；
    - 调用 `UniWindow.Initalize(desktopGameObject)`。

#### 2.2.2 窗口 Prefab 的原则

- 根节点：
  - 挂 `Canvas + GraphicRaycaster`；
  - **不挂 `CanvasScaler`**；
  - `RectTransform` 尺寸为全屏。
- 下级结构只要满足脚本中的 `transform.Find("xxx")` 路径即可：
  - 主菜单示例：`MainMenuWindow/BtnNewGame`；
  - HUD 示例：`InGameHudWindow/TxtHudTitle` 或更复杂的 PlayerInfo 结构。

---

## 3. YooAsset 初始化与 Persistent 场景

### 3.1 Offline 模式初始化示例

Persistent 场景中挂一个 `GameLauncher`：

```csharp
using System.Collections;
using UnityEngine;
using YooAsset;
using UniFramework.Window;
using Game.UI;

public class GameLauncher : MonoBehaviour
{
    public GameObject desktop; // Canvas 下的 Desktop 节点

    private IEnumerator Start()
    {
        // UIRoot 常驻
        DontDestroyOnLoad(transform.root.gameObject);

        // 1. 初始化 YooAsset
        yield return InitializeYooAsset();

        // 2. 初始化 UniWindow
        UniWindow.Initalize(desktop);

        // 3. 打开主菜单
        UniWindow.OpenWindowAsync<MainMenuWindow>("UI/MainMenuWindow");
    }

    private IEnumerator InitializeYooAsset()
    {
        YooAssets.Initialize();

        var package = YooAssets.CreatePackage("DefaultPackage");
        YooAssets.SetDefaultPackage(package);

        var initParameters = new OfflinePlayModeParameters();
        var initOperation = package.InitializeAsync(initParameters);
        yield return initOperation;

        if (initOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"YooAsset 初始化失败：{initOperation.Error}");
        }
    }
}
```

### 3.2 Persistent + Additive 内容场景的优点

- YooAsset、UniWindow、声音/配置等系统只初始化一次；
- HUD 等全局窗口挂在 `Desktop` 下，通过 `DontDestroyOnLoad` 跨场景存在；
- 内容场景（MainScene、Scene1、Scene2、Scene3-1/3-2）按需 Additive 加载/卸载；
- 更利于拆分关卡与团队协作。

---

## 4. 主菜单 & HUD 的 Sample 代码

### 4.1 主菜单 `MainMenuWindow`

Prefab 结构（最简版）：

- `MainMenuWindow`（根，Canvas + GraphicRaycaster）
  - `BtnNewGame`（Button）

代码示例（关键片段）：

```csharp
using UniFramework.Window;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    [WindowAttribute(windowLayer: 100, fullScreen: true)]
    public class MainMenuWindow : UIWindow
    {
        private Button _btnNewGame;

        public override void OnCreate()
        {
            _btnNewGame = transform.Find("BtnNewGame")?.GetComponent<Button>();

            if (_btnNewGame == null)
            {
                Debug.LogError("[MainMenuWindow] BtnNewGame not found, please check prefab path.");
                return;
            }

            _btnNewGame.onClick.AddListener(OnClickNewGame);
        }

        public override void OnRefresh() { }
        public override void OnUpdate()  { }

        public override void OnDestroy()
        {
            if (_btnNewGame != null)
                _btnNewGame.onClick.RemoveListener(OnClickNewGame);

            _btnNewGame = null;
        }

        private void OnClickNewGame()
        {
            // 示例：直接加载 GameScene（可替换为 GameSceneManager 调用）
            SceneManager.LoadScene("GameScene", LoadSceneMode.Additive);

            UniWindow.CloseWindow<MainMenuWindow>();
            UniWindow.OpenWindowAsync<InGameHudWindow>("UI/InGameHudWindow");
        }
    }
}
```

### 4.2 HUD `InGameHudWindow`（最简版）

Prefab 结构：

- `InGameHudWindow`（根，Canvas + GraphicRaycaster）
  - `TxtHudTitle`（Text）

代码示例：

```csharp
using UniFramework.Window;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [WindowAttribute(windowLayer: 200, fullScreen: false)]
    public class InGameHudWindow : UIWindow
    {
        private Text _txtHudTitle;

        public override void OnCreate()
        {
            _txtHudTitle = transform.Find("TxtHudTitle")?.GetComponent<Text>();

            if (_txtHudTitle == null)
            {
                Debug.LogError("[InGameHudWindow] TxtHudTitle not found, please check prefab path.");
                return;
            }

            _txtHudTitle.text = "Game HUD";
        }

        public override void OnRefresh() { }
        public override void OnUpdate()  { }

        public override void OnDestroy()
        {
            _txtHudTitle = null;
        }
    }
}
```

在实际项目中，可以扩展 HUD：增加 PlayerInfo 面板、Save/Load/Settings 按钮等。

---

## 5. 场景管理：Scene1 / Scene2 / Scene3-1+3-2

### 5.1 需求回顾

- 使用 YooAsset 管理场景加载；
- `scene1` 与 `scene2` 之间切换时：
  - 必须卸载当前内容场景；
- `scene3-1` 与 `scene3-2`：
  - 必须**成对加载**；
  - 当切回 `scene1` 或 `scene2` 时，必须**同时卸载两者**。

### 5.2 场景管理器实现示例

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using UnityEngine.SceneManagement;

public enum EGameSceneType
{
    None,
    Scene1,
    Scene2,
    Scene3Group, // scene3-1 + scene3-2
}

public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    private EGameSceneType _currentType = EGameSceneType.None;
    private SceneHandle _currentSingleScene;
    private readonly List<SceneHandle> _currentGroupScenes = new List<SceneHandle>();

    private ResourcePackage _package;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _package = YooAssets.GetPackage("DefaultPackage");
        if (_package == null)
        {
            Debug.LogError("[GameSceneManager] Not found YooAsset package : DefaultPackage");
        }
    }

    // Public API
    public Coroutine SwitchToScene1()
    {
        return StartCoroutine(SwitchToSingleScene("scene1", EGameSceneType.Scene1));
    }

    public Coroutine SwitchToScene2()
    {
        return StartCoroutine(SwitchToSingleScene("scene2", EGameSceneType.Scene2));
    }

    public Coroutine SwitchToScene3Group()
    {
        string[] scenes = { "scene3-1", "scene3-2" };
        return StartCoroutine(SwitchToSceneGroup(scenes, EGameSceneType.Scene3Group));
    }

    // Core implementation
    private IEnumerator SwitchToSingleScene(string sceneLocation, EGameSceneType targetType)
    {
        if (_package == null)
            yield break;

        // 1. 卸载当前内容场景（包括组合）
        yield return UnloadCurrentContentScenes();

        // 2. 加载新的单场景（Additive）
        var loadOp = _package.LoadSceneAsync(sceneLocation, LoadSceneMode.Additive);
        yield return loadOp;

        if (loadOp.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"[GameSceneManager] Load scene failed : {sceneLocation}, Error : {loadOp.Error}");
            yield break;
        }

        _currentSingleScene = loadOp;
        _currentType = targetType;

        _currentSingleScene.ActivateScene();
    }

    private IEnumerator SwitchToSceneGroup(string[] sceneLocations, EGameSceneType targetType)
    {
        if (_package == null)
            yield break;

        // 1. 卸载当前内容场景
        yield return UnloadCurrentContentScenes();

        _currentGroupScenes.Clear();

        // 2. 加载组合中的所有场景（Additive）
        foreach (var location in sceneLocations)
        {
            var loadOp = _package.LoadSceneAsync(location, LoadSceneMode.Additive);
            yield return loadOp;

            if (loadOp.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[GameSceneManager] Load scene failed : {location}, Error : {loadOp.Error}");
                continue;
            }

            _currentGroupScenes.Add(loadOp);
            loadOp.ActivateScene();
        }

        _currentType = _currentGroupScenes.Count > 0 ? targetType : EGameSceneType.None;
    }

    private IEnumerator UnloadCurrentContentScenes()
    {
        // 卸载单场景
        if (_currentSingleScene.IsValid)
        {
            var unloadOp = _currentSingleScene.UnloadAsync();
            yield return unloadOp;
            _currentSingleScene = default;
        }

        // 卸载组合场景
        if (_currentGroupScenes.Count > 0)
        {
            foreach (var handle in _currentGroupScenes)
            {
                if (!handle.IsValid)
                    continue;

                var unloadOp = handle.UnloadAsync();
                yield return unloadOp;
            }
            _currentGroupScenes.Clear();
        }

        _currentType = EGameSceneType.None;
    }
}
```

### 5.3 与 UI 的交互示例

在 `MainMenuWindow` / HUD / 其它 UI 中：

```csharp
// 进入关卡 1
GameSceneManager.Instance.SwitchToScene1();

// 进入关卡 2
GameSceneManager.Instance.SwitchToScene2();

// 进入组合关卡（scene3-1 + scene3-2）
GameSceneManager.Instance.SwitchToScene3Group();
```

与此同时，HUD 可以作为“全局 UI 窗口”存在：

- 首次进入游戏时调用 `OpenWindowAsync<InGameHudWindow>`；
- 后续切换 `scene1/scene2/scene3Group` 时，只切内容场景，不关 HUD；
- HUD 始终挂在 `Desktop` 下，不随场景卸载。

---

## 6. 常见问题 & 对话要点总结

### 6.1 为什么 Depth 要 +100，而子 Canvas +5？

- **+100**：服务于“窗口之间的排序”，属于**跨窗口维度**：
  - 同一 `WindowLayer` 里的多个窗口：100、200、300… 依次错开；
  - 确保单个窗口内部的子 Canvas 即使多次 +5，也不容易穿透到其他窗口的 sortingOrder 区间。
- **+5**：服务于“单个窗口内部 Canvas 的排序”，属于**窗口内部维度**：
  - 父 Canvas 100，子 Canvas 105、110、115…，在一个窗口内部区分前后关系。

### 6.2 既然隐藏时 Layer 已经是 Ignore Raycast，为何还要关 Raycaster？

- **语义层面**：`Visible = false` 在设计上意味着“看不见 + 不可交互”，与摄像机 / Culling Mask 配置解耦；
- **安全性**：多摄像机、多 Canvas 情况下，单靠 Layer 不能 100% 保证不被点击；
- **性能**：关闭 `GraphicRaycaster` 可减少 UI 每帧射线检测的开销，尤其在隐藏大量窗口时。

### 6.3 HUD 如何跨场景存在？

- HUD 的 `_panel` 挂在 `Desktop` 下，而 `Desktop` 所在的 UIRoot 是 `DontDestroyOnLoad` 的；
- 只要不调用 `CloseWindow<InGameHudWindow>`，HUD 就不会随着内容场景的卸载而销毁；
- 再次调用 `OpenWindowAsync<InGameHudWindow>` 时，`UniWindow` 会复用已有实例，而不是重建。

### 6.4 Persistent 场景 + Additive 内容场景结构是否推荐？

非常推荐，尤其是：

- 使用 YooAsset 管理场景与资源；
- 使用 UniWindow 做全局 UI；
- 需要 HUD / 顶部信息条等跨场景存在的传统单机游戏或 RPG。

---

通过本文件与 `UniFramework/UniWindow/README.md`，你可以在不查看聊天记录的情况下，完整还原这次讨论中的所有关键设计与示例代码，并据此搭建一套可运行的 UniWindow + YooAsset 单机游戏架构。


