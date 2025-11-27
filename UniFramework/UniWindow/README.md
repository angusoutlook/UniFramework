## UniFramework.Window

一个轻量级的 **基于堆栈的界面系统**，用于管理 UGUI 窗口的创建、销毁、层级与显隐，内部深度集成 YooAsset。

> 统一约定：示例基于 Unity **2022.3.61f1c1**，资源加载基于 YooAsset Offline 模式。

---

## 1. 架构说明与设计意图（Architecture & Intent）

### 1.1 关键角色

- **`UIWindow`（抽象基类）**
  - 纯 C# 类，不继承 `MonoBehaviour`。
  - 通过 YooAsset 加载一个带 `Canvas + GraphicRaycaster` 的 Prefab 实例，内部用 `_panel` 持有实例根节点。
  - 暴露生命周期函数：`OnCreate / OnRefresh / OnUpdate / OnDestroy`，以及 `OnSortDepth / OnSetVisible` 扩展点。
  - 提供属性：`WindowName`、`WindowLayer`、`FullScreen`、`Depth`、`Visible`、`UserData / UserDatas` 等。

- **`UniWindow`（静态管理器）**
  - 维护一个 `List<UIWindow>` 作为**有序栈** `_stack`。
  - 负责：
    - 初始化：`Initalize(GameObject desktop)`，创建 `UniWindowDriver` 常驻对象，并记录 `Desktop`。
    - 更新驱动：`Update()` 遍历栈中窗口执行 `InternalUpdate()`。
    - 打开/关闭窗口：`OpenWindowAsync/Sync`、`CloseWindow`、`CloseAll`。
    - 层级与显隐：根据 `WindowLayer` 和是否全屏，统一计算 `Depth` 和 `Visible`。
    - 安全区域适配：`ApplyScreenSafeRect` / `SimulateIPhoneXNotchScreen`。

- **`WindowAttribute`**
  - `[WindowAttribute(int windowLayer, bool fullScreen)]`。
  - 用于声明窗口的逻辑层级与是否为全屏窗口。
  - 在 `CreateInstance(Type type)` 中强制要求每个 `UIWindow` 子类都带该 Attribute，否则抛异常。

- **`UniWindowDriver`**
  - 一个简单的 `MonoBehaviour`，在 `Update` 中调用 `UniWindow.Update()`。
  - 由 `UniWindow.Initalize` 动态创建，并 `DontDestroyOnLoad`。

- **`OpenWindowOperation`**
  - 包装 YooAsset 的 `AssetHandle`，用于跟踪“打开窗口”的异步状态。
  - 支持同步等待：`WaitForAsyncComplete()`。

### 1.2 窗口栈与层级规则

- **窗口栈 `_stack`**：
  - 所有 `UIWindow` 按 **`WindowLayer` + 入栈顺序** 有序排列。
  - `Push` 时根据 `WindowLayer` 找到插入位置：
    - 先尝试插入到同层窗口之后；
    - 如果没有同层，则根据层级值插在相邻层之后；
    - 栈为空或找不到位置时从 0 插入。

- **深度计算（`Depth`）**
  - 同一 `WindowLayer` 内：深度从 `layer` 开始，每个窗口 **+100**：
    - 避免不同窗口的 Canvas sortingOrder 互相挤压。
  - 单个窗口内部：父 `Canvas` 使用窗口的 `Depth`，每个子 `Canvas` 在此基础上 **+5**：
    - 保证窗口内部多个 Canvas 有清晰的前后关系。

- **显隐与全屏遮挡**
  - `Visible` 属性通过切换 Layer 与 `GraphicRaycaster` 控制：
    - `WINDOW_SHOW_LAYER = 5`（UI）
    - `WINDOW_HIDE_LAYER = 2`（Ignore Raycast）
    - 同时会开启/关闭所有 `GraphicRaycaster`，确保不可见时也**绝对不可交互**。
  - `OnSetWindowVisible` 从栈顶往下遍历：
    - 第一个可见窗口始终显示；
    - 若它为 `FullScreen` 且已准备好，则其下所有窗口强制隐藏。

- **与 YooAsset 的集成**
  - 打开窗口：
    - 若实例已存在：调整栈顺序并复用已加载的资源。
    - 若实例不存在：`Activator.CreateInstance` + `YooAssets.LoadAssetAsync<GameObject>(location)` 加载 Prefab 并实例化到 `Desktop` 下。
  - 通过 `OpenWindowOperation` 将异步加载过程统一包装，外部可选择异步或同步打开。

### 1.3 典型架构模式

推荐使用：**Persistent 场景 + 其他内容场景 Additive 加载** 的结构：

- Persistent 场景（常驻）：
  - 初始化 YooAsset。
  - 创建 UI 根：`Canvas + CanvasScaler + GraphicRaycaster + Desktop`。
  - 调用 `UniWindow.Initalize(desktop)`。
  - 放置 `GameSceneManager`、声音/配置/存档等全局管理器。

- 内容场景：
  - `MainScene`（可选：主菜单 3D 背景）。
  - `Scene1` / `Scene2`（普通关卡）。
  - `Scene3-1` + `Scene3-2`（成对出现的组合场景）。
  - 通过 YooAsset 的 `LoadSceneAsync`（Additive）与 `UnloadAsync` 配合一个统一的 `GameSceneManager` 控制加载卸载。

HUD 窗口可作为“全局窗口”，依托 `Desktop` + `DontDestroyOnLoad` 跨场景存在。

### 1.4 深度 / Layer / Raycaster 设计细节

- **`WINDOW_HIDE_LAYER` / `WINDOW_SHOW_LAYER`**
  - `WINDOW_SHOW_LAYER = 5`：对应 Unity 默认 `UI` 层，窗口处于正常显示与交互状态。
  - `WINDOW_HIDE_LAYER = 2`：对应 Unity 默认 `Ignore Raycast` 层，通常不会被 UI 射线检测命中。
  - `Visible` 设为 `false` 时：
    - 主 `Canvas` 与所有子 `Canvas` 的 Layer 切到 `Ignore Raycast`；
    - 同时关闭所有 `GraphicRaycaster`（见 `Interactable` 属性）。
  - 设计意图：
    - 即使摄像机或 Culling Mask 配置被修改，也能保证“不可见 = 绝对不可交互”；
    - 隐藏大量窗口时，关闭 Raycaster 可以减少不必要的 UI 射线检测，提升性能。

- **`Depth` 的 +100 与子 `Canvas` 的 +5**
  - 同一 `WindowLayer` 内：
    - 第一个窗口的深度 = `WindowLayer`；
    - 后续窗口每出现一个，深度在前一个基础上 **+100**。
    - 用于保证“同一逻辑层级的多个窗口”之间有足够间距，避免 sortingOrder 互相挤压。
  - 单个窗口内部：
    - 父 `Canvas.sortingOrder = Depth`；
    - 所有子 `Canvas` 在此基础上依次 **+5**。  
    - 用于管理窗口内部多个 Canvas 之间的前后关系，并为后续扩展预留空间。

- **Prefab 与 CanvasScaler 的约定**
  - **场景中的 UIRoot**：
    - 只在 Persistent 场景中创建一个：`Canvas + CanvasScaler + GraphicRaycaster`。
    - 子节点 `Desktop` 作为所有窗口实例的父节点传给 `UniWindow.Initalize(desktop)`。
  - **每个窗口 Prefab**：
    - 根节点挂 `Canvas + GraphicRaycaster`，**不要再挂 `CanvasScaler`**；
    - 交由场景中的唯一 `CanvasScaler` 统一做分辨率适配与缩放。

---

## 2. 使用示例（Samples）

以下示例演示：

- 使用 YooAsset Offline 模式初始化资源系统。
- 初始化 `UniWindow`。
- 进入主菜单（`MainMenuWindow`），点击 `New` 进入游戏场景，显示 HUD（`InGameHudWindow`）。

> 说明：示例中的命名空间、资源路径仅供参考，可根据项目实际调整。

### 2.1 Persistent 场景入口：`GameLauncher`

将该脚本挂在 Persistent 场景中的 UI 根对象（包含 Canvas / CanvasScaler / GraphicRaycaster）上，并指定子节点 `Desktop`。

```csharp
using System.Collections;
using UnityEngine;
using YooAsset;
using UniFramework.Window;
using Game.UI;

public class GameLauncher : MonoBehaviour
{
    // 指向 Canvas 下的 Desktop（RectTransform，stretch 全屏）
    public GameObject desktop;

    private IEnumerator Start()
    {
        // 整个 UI 根常驻
        DontDestroyOnLoad(transform.root.gameObject);

        // 1. 初始化 YooAsset（Offline 模式示例）
        yield return InitializeYooAsset();

        // 2. 初始化 UniWindow
        UniWindow.Initalize(desktop);

        // 3. 打开主菜单窗口
        UniWindow.OpenWindowAsync<MainMenuWindow>("UI/MainMenuWindow");
    }

    private IEnumerator InitializeYooAsset()
    {
        // YooAsset 框架初始化
        YooAssets.Initialize();

        // 创建并设置默认资源包
        var package = YooAssets.CreatePackage("DefaultPackage");
        YooAssets.SetDefaultPackage(package);

        // Offline 模式参数（适合单机，把资源打到 StreamingAssets）
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

### 2.2 主菜单窗口：`MainMenuWindow`

Prefab 结构示例（根节点挂 `Canvas + GraphicRaycaster`，不挂 `CanvasScaler`）：

- `MainMenuWindow`（Prefab 根）  
  - `BtnNewGame`（Button）

```csharp
using UniFramework.Window;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    /// <summary>
    /// 最简主菜单窗口：只有一个 NewGame 按钮。
    /// </summary>
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

        public override void OnRefresh()
        {
        }

        public override void OnUpdate()
        {
        }

        public override void OnDestroy()
        {
            if (_btnNewGame != null)
                _btnNewGame.onClick.RemoveListener(OnClickNewGame);

            _btnNewGame = null;
        }

        private void OnClickNewGame()
        {
            // 切换到 GameScene（普通内容场景，Additive 模式）
            SceneManager.LoadScene("GameScene", LoadSceneMode.Additive);

            // 关闭主菜单
            UniWindow.CloseWindow<MainMenuWindow>();

            // 打开 HUD 窗口
            UniWindow.OpenWindowAsync<InGameHudWindow>("UI/InGameHudWindow");
        }
    }
}
```

### 2.3 游戏内 HUD：`InGameHudWindow`

Prefab 结构示例（根节点挂 `Canvas + GraphicRaycaster`）：

- `InGameHudWindow`（Prefab 根）  
  - `TxtHudTitle`（Text）

```csharp
using UniFramework.Window;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 最简 HUD 窗口示例。
    /// </summary>
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

        public override void OnRefresh()
        {
        }

        public override void OnUpdate()
        {
        }

        public override void OnDestroy()
        {
            _txtHudTitle = null;
        }
    }
}
```

### 2.4 典型窗口集合（主菜单 / HUD / 存档 / 读档 / 设置 / MessageBox）

下面是一套适用于“传统单机游戏”的典型窗口集合与推荐层级：

- 主菜单：`MainMenuWindow`（主界面）
- HUD：`InGameHudWindow`（游戏内常驻信息+按钮）
- 存档：`SaveWindow`
- 读档：`LoadWindow`
- 设置：`SettingsWindow`
- 提示框：`MessageBoxWindow`（通用确认/警告对话框）

#### 2.4.1 推荐 WindowLayer 规划

| 用途                     | 建议 WindowLayer | FullScreen | 说明 |
|--------------------------|------------------|-----------|------|
| 主界面 MainMenu          | **100**          | **true**  | 启动后的主菜单界面 |
| 游戏内 HUD               | **200**          | **false** | 游戏内顶部/边缘信息，与场景内容共存 |
| Save / Load / Settings   | **300**          | **true**  | 作为“弹窗层”，从主菜单或游戏内都能打开 |
| MessageBox（确认对话框） | **400**          | **false** | 浮在任意弹窗之上，用于二次确认 |
| Guide / 教程引导         | **600**          | 视需求    | 新手引导层，可选 |
| Loading / 过场界面       | **900**          | **true**  | 全屏 Loading 或过场，遮挡一切 |

#### 2.4.2 Save / Load / Settings / MessageBox 代码骨架示例

仅展示与窗口系统相关的部分，实际存档/加载/设置逻辑可按项目需求扩展。

```csharp
using UniFramework.Window;
using UnityEngine;

namespace Game.UI
{
    // 存档窗口
    [WindowAttribute(windowLayer: 300, fullScreen: true)]
    public class SaveWindow : UIWindow
    {
        public override void OnCreate()  { /* 初始化存档槽 UI */ }
        public override void OnRefresh() { /* 刷新存档列表显示 */ }
        public override void OnUpdate()  { }
        public override void OnDestroy() { }
    }

    // 读档窗口
    [WindowAttribute(windowLayer: 300, fullScreen: true)]
    public class LoadWindow : UIWindow
    {
        public override void OnCreate()  { /* 初始化存档槽 UI */ }
        public override void OnRefresh() { /* 刷新存档列表显示 */ }
        public override void OnUpdate()  { }
        public override void OnDestroy() { }
    }

    // 设置窗口
    [WindowAttribute(windowLayer: 300, fullScreen: true)]
    public class SettingsWindow : UIWindow
    {
        public override void OnCreate()
        {
            // 绑定：全屏/分辨率、音量、多语言等控件
        }

        public override void OnRefresh()
        {
            // 从配置系统读取当前设置，同步到控件
        }

        public override void OnUpdate()  { }

        public override void OnDestroy()
        {
            // 解绑事件
        }

        // 点击“应用/确认”时：读取控件 -> 应用到系统 -> 关闭窗口
    }
}
```

`MessageBoxWindow` 典型设计：通过 `UserDatas` 传入提示文本与回调。

```csharp
using UniFramework.Window;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace Game.UI
{
    [WindowAttribute(windowLayer: 400, fullScreen: false)]
    public class MessageBoxWindow : UIWindow
    {
        private Text _txtMessage;
        private Button _btnConfirm;
        private Button _btnCancel;

        private Action _onConfirm;
        private Action _onCancel;

        public override void OnCreate()
        {
            _txtMessage = transform.Find("Panel/TxtMessage")?.GetComponent<Text>();
            _btnConfirm = transform.Find("Panel/BtnConfirm")?.GetComponent<Button>();
            _btnCancel  = transform.Find("Panel/BtnCancel") ?.GetComponent<Button>();

            _btnConfirm?.onClick.AddListener(OnClickConfirm);
            _btnCancel ?.onClick.AddListener(OnClickCancel);
        }

        public override void OnRefresh()
        {
            // 约定：UserDatas[0] = string message, [1] = Action onConfirm, [2] = Action onCancel
            if (UserDatas != null)
            {
                if (UserDatas.Length > 0 && UserDatas[0] is string msg)
                    _txtMessage.text = msg;
                if (UserDatas.Length > 1 && UserDatas[1] is Action c1)
                    _onConfirm = c1;
                if (UserDatas.Length > 2 && UserDatas[2] is Action c2)
                    _onCancel = c2;
            }
        }

        public override void OnUpdate() { }

        public override void OnDestroy()
        {
            _btnConfirm?.onClick.RemoveListener(OnClickConfirm);
            _btnCancel ?.onClick.RemoveListener(OnClickCancel);

            _onConfirm = null;
            _onCancel  = null;
        }

        private void OnClickConfirm()
        {
            _onConfirm?.Invoke();
            UniWindow.CloseWindow<MessageBoxWindow>();
        }

        private void OnClickCancel()
        {
            _onCancel?.Invoke();
            UniWindow.CloseWindow<MessageBoxWindow>();
        }
    }
}
```

---

## 3. 场景管理架构示例（含 scene1 / scene2 / scene3-1 / scene3-2）

下例展示一个简单的 **场景管理器**，配合 YooAsset 加载场景：

- 切到 `scene1` 或 `scene2`：**必须卸载当前内容场景**。
- `scene3-1` 与 `scene3-2`：作为一组场景组合，**一起加载 / 一起卸载**。

### 3.1 枚举与管理器骨架

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
    Scene3Group, // scene3-1 + scene3-2 组合
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

        // 前提：Persistent 场景中，已经创建并设置了默认包 "DefaultPackage"
        _package = YooAssets.GetPackage("DefaultPackage");
        if (_package == null)
        {
            Debug.LogError("[GameSceneManager] Not found YooAsset package : DefaultPackage");
        }
    }

    // 切到 scene1
    public Coroutine SwitchToScene1()
    {
        return StartCoroutine(SwitchToSingleScene("scene1", EGameSceneType.Scene1));
    }

    // 切到 scene2
    public Coroutine SwitchToScene2()
    {
        return StartCoroutine(SwitchToSingleScene("scene2", EGameSceneType.Scene2));
    }

    // 切到 scene3 组合（scene3-1 + scene3-2）
    public Coroutine SwitchToScene3Group()
    {
        string[] scenes = { "scene3-1", "scene3-2" };
        return StartCoroutine(SwitchToSceneGroup(scenes, EGameSceneType.Scene3Group));
    }

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

### 3.2 与 UI/主菜单联动的示例

在 `MainMenuWindow` 或其他 UI 中可以这样使用：

```csharp
// 进入关卡 1
GameSceneManager.Instance.SwitchToScene1();

// 进入关卡 2
GameSceneManager.Instance.SwitchToScene2();

// 进入 scene3 组合（scene3-1 + scene3-2）
GameSceneManager.Instance.SwitchToScene3Group();
```

配合 UniWindow，你可以：

- 在切换场景时保持 HUD 等全局窗口不销毁（依托 Persistent 场景的 Desktop）。
- 在需要时通过 `FullScreen` 窗口遮挡或 `Visible` 控制 HUD 的显隐。

以上示例代码与文档可作为入门骨架，你可以在此基础上增加存档系统、设置窗口、引导层、Loading 层等更完整的游戏架构。

---

## 4. 传统单机游戏整体架构示例（总结）

本节将上述内容组合成一套完整的“传统单机游戏”架构蓝图：

- **Persistent 场景（常驻）**
  - 负责：
    - 初始化 YooAsset（Offline 模式）。
    - 创建 UIRoot：`Canvas + CanvasScaler + GraphicRaycaster + Desktop`。
    - 调用 `UniWindow.Initalize(desktop)`。
    - 常驻管理器：`GameSceneManager`、声音/配置/存档管理器等。
  - 启动流程：
    - `GameLauncher.Start()` → 初始化资源 & UniWindow → 打开 `MainMenuWindow`。

- **UI 窗口分层**
  - `MainMenuWindow`：WindowLayer=100，FullScreen=true。
  - `InGameHudWindow`：WindowLayer=200，FullScreen=false，作为跨场景 HUD，可在进入游戏后常驻。
  - `SaveWindow` / `LoadWindow` / `SettingsWindow`：WindowLayer=300，FullScreen=true，用于阻挡下层 UI，避免误触。
  - `MessageBoxWindow`：WindowLayer=400，FullScreen=false，用于二次确认（保存/读档/退出等）。
  - 可选：`GuideWindow`（600）、`LoadingWindow`（900）。

- **场景流转**
  - 逻辑流：
    - 启动 → Persistent 场景加载 → 主菜单 → 选择关卡 → GameSceneManager 通过 YooAsset 加载/卸载场景。
    - 例如：`SwitchToScene1()` 卸载当前内容场景 → 加载 `scene1`（Additive）。
    - `SwitchToScene3Group()` 卸载当前内容场景 → 加载 `scene3-1` + `scene3-2` 作为组合关卡。
  - HUD 跨场景存在：
    - 第一次从主菜单进入游戏时调用：`OpenWindowAsync<InGameHudWindow>`。
    - 后续切换关卡仅切换内容场景，HUD 始终挂在 `Desktop` 下，不随场景卸载。
    - 如需短暂隐藏 HUD，可：
      - 打开一个全屏窗口（如 `LoadingWindow`）遮挡；或
      - 在 HUD 内部暴露一个接口控制 `Visible`。

通过这套架构，UniWindow 负责“窗口栈 + 层级 + 显隐”，YooAsset 负责“资源与场景加载”，Persistent 场景负责“全局生命周期”，可以比较优雅地支撑传统单机游戏的主菜单、游戏内 HUD、多关卡切换、存档/读档/设置、MessageBox 等完整流程。


