## UniAnimation 与 Unity 动画系统调度理解手册

> 说明：本手册基于当前项目中的真实代码（`UniAnimation/Runtime`），并结合 Unity 动画系统（Animator、Layer、Avatar Mask、Update / FixedUpdate、JobSystem）运行原理进行整理，用于帮助理解 UniAnimation 的设计思路与使用方式。  
> 若与实际代码有差异，以源码为准，这里属于概念化与简化说明。

---

### 一、UniAnimation 模块总体设计

- **定位**
  - 一个**基于 Playables** 的轻量动画系统，不依赖 Animator Controller 状态机。
  - 使用方式类似旧版 `Animation`：通过 **动画名字符串 + 淡入时间** 控制播放。
  - 对外主要入口：`UniAnimation : MonoBehaviour` 组件。

- **内部核心类关系（概念图）**
  - `UniAnimation`（挂在 GameObject 上的组件）
    - 内部持有 `AnimPlayable`
  - `AnimPlayable`
    - 内部管理多个 `AnimClip`（每个对应一个 `AnimationClip`）
    - 管理多个 `AnimMixer`（每个对应一个 Layer）
    - 根节点为 `AnimationLayerMixerPlayable`
  - `AnimMixer : AnimNode`
    - 管理某一 Layer 上的所有 `AnimClip`
    - 控制同一层上的淡入淡出、互斥播放和自动断开
  - `AnimClip : AnimNode`
    - 封装一个 `AnimationClipPlayable`
    - 持有名字、Layer、WrapMode、归一化时间等信息
    - 暴露 `AnimState` 作为对外状态视图
  - `AnimNode`（抽象基类）
    - 封装 PlayableGraph 中单个节点的通用逻辑：连接、断开、Time、Speed、Weight、淡入淡出
  - `AnimState`
    - 提供对单个动画片段的只读/可写状态访问（Name、Length、Layer、WrapMode、Time、Speed、NormalizedTime、Weight）
  - `UniLogger`
    - 简单封装日志输出（Debug / Warning / Error）

---

### 二、AnimNode 抽象与淡入淡出机制

> 对应源码：`Runtime/AnimNode.cs`

- **职责**
  - 抽象 PlayableGraph 中“一个节点”的共同行为：
    - 持有：`PlayableGraph`、自身 Playable（`_source`）、父 Playable（`_parent`）、输入端口 `InputPort`。
    - 提供常用属性：
      - `Time` / `Speed`：时间轴与播放速度。
      - `Weight`：在父节点 Mixer 上的输入权重。
      - `IsDone` / `IsValid` / `IsPlaying`：Playable 状态。
    - 提供基础操作：
      - `Connect(parent, parentInputPort)` / `Disconnect()`。
      - `PlayNode()` / `PauseNode()` / `ResetNode()`。
      - `StartWeightFade(destWeight, fadeDuration)` 实现统一的权重插值。

- **淡入淡出逻辑**
  - 内部字段：
    - `_fadeSpeed`：按“每秒权重变化量”来算（`1f / fadeDuration`）。
    - `_fadeWeight`：目标权重。
    - `_isFading`：是否正在淡入淡出中。
  - `Update(deltaTime)` 中：
    - 使用 `Mathf.MoveTowards(Weight, _fadeWeight, _fadeSpeed * deltaTime)` 移动权重。
    - 当接近目标值时，结束淡入淡出。

- **设计意图**
  - 上层节点（`AnimClip`、`AnimMixer`）不需要关心 PlayableGraph 的连接细节，只要继承 `AnimNode` 并调用统一接口即可。
  - 所有节点的淡入淡出行为保持一致，便于在 Mixer 和 Clip 间复用逻辑。

---

### 三、AnimClip：单个动画片段节点

> 对应源码：`Runtime/AnimClip.cs`

- **核心字段**
  - `Name`：动画名（字符串，为 `_animClips` 中的唯一标识）。
  - `_clip : AnimationClip`。
  - `_clipPlayable : AnimationClipPlayable`。
  - `Layer`：该动画所属的层级。
  - `State : AnimState`：该 Clip 的状态封装。

- **关键属性**
  - `ClipLength`：
    - 返回 `_clip.length / Speed`，Speed 为 0 时视为 `Infinity`。
  - `NormalizedTime`：
    - `0 ~ 1` 映射到 `0 ~ _clip.length`，内部通过 `Time` 来读写。
  - `WrapMode`：
    - 直接操控 `_clip.wrapMode`，同时 `AnimState.WrapMode` 提供只读访问。

- **构造逻辑**
  - 创建 `AnimationClipPlayable`，关闭 Foot IK / Playable IK。
  - 设置 `_clip.wrapMode == WrapMode.Once` 时的 `SetDuration(clip.length)`，让 Playable 的 `IsDone` 机制生效。
  - 构造 `AnimState` 并与该 Clip 绑定。

- **播放重置逻辑**
  - 重写 `PlayNode()`：对 `Once` / `ClampForever` 模式，在播放前将 `Time` 归零，然后调用基类 `PlayNode()`。

- **设计意图**
  - 把 `AnimationClip` 封装成一个“带名字、Layer、WrapMode、时间轴控制、状态对象”的可控单元。
  - 与 `AnimState` 配合，为上层逻辑提供简单的动画状态访问入口。

---

### 四、AnimMixer：单层动画混合器

> 对应源码：`Runtime/AnimMixer.cs`

- **职责**
  - 表示某一个 Layer 上的混合器节点。
  - 管理该层上的所有 `AnimClip`：
    - 控制单一激活动画。
    - 负责淡入淡出。
    - 在所有 Clip 完成后自动淡出并断开连接。

- **内部结构**
  - `_animClips : List<AnimClip>`：管理当前层的所有动画片段。
  - `_mixer : AnimationMixerPlayable`：该层的混合器 Playable。
  - `Layer`：当前 Mixer 对应的层级索引。
  - `_isQuiting`：标记是否进入“退出流程”（所有子 Clip 完成后淡出自身）。

- **Update 流程**
  1. 调用基类 `Update(deltaTime)`，处理自身权重淡入淡出。
  2. 遍历 `_animClips`，调用每个 `AnimClip.Update(deltaTime)`。
  3. 检查所有 Clip 的 `IsDone`：
     - 若全部完成且未进入退出流程：
       - 将 `_isQuiting = true`，调用 `StartWeightFade(0, HIDE_DURATION)` 淡出本层。
  4. 若 `_isQuiting == true` 且 `Weight` 接近 0，则调用 `DisconnectMixer()`：
     - 断开所有子 Clip；
     - 断开本 Mixer 与父节点的连接。

- **播放逻辑 `Play(AnimClip newAnimClip, float fadeDuration)`**
  - 重置 `_isQuiting = false`，并立即让本层权重 `Weight = 1`。
  - 若 `newAnimClip` 不在 `_animClips` 中：
    - 优先使用列表中的空位（`null`）；
    - 如无空位则增加 `_mixer` 输入口并连接新 Clip。
  - 遍历 `_animClips`：
    - 对新 Clip：`StartWeightFade(1, fadeDuration)` + `PlayNode()`。
    - 对其他 Clip：`StartWeightFade(0, fadeDuration)` + `PauseNode()`。

- **设计意图**
  - 将“同一层上多个动画片段的淡入淡出与互斥播放”统一管理起来。
  - 当没有任何子动画在播放时，自动从图中移除该层，优化性能。

---

### 五、AnimPlayable：Playable 图管理器

> 对应源码：`Runtime/AnimPlayable.cs`

- **核心职责**
  - 为一个 `Animator` 创建并管理一个独立的 `PlayableGraph`：
    - 根节点：`AnimationLayerMixerPlayable`（`_mixerRoot`）。
    - 输出：`AnimationPlayableOutput`（绑定到目标 Animator）。
  - 维护所有的 `AnimClip` / `AnimMixer`。
  - 提供面向上层的播放、停止、增加/移除动画接口。

- **图创建 `Create(Animator animator)`**
  - 以 `animator.gameObject.name` 为 Graph 名称。
  - 使用 `DirectorUpdateMode.Manual`（手动刷新）。
  - 创建：
    - `_mixerRoot = AnimationLayerMixerPlayable.Create(_graph)`。
    - `_output = AnimationPlayableOutput.Create(_graph, name, animator)` 并设置 `SourcePlayable`。

- **图更新 `Update(float deltaTime)`**
  - 调用 `_graph.Evaluate(deltaTime)` 推进时间。
  - 遍历所有 `AnimMixer`：
    - 仅对 `IsConnect == true` 的层调用 `Update(deltaTime)`。

- **Clip 管理**
  - `AddAnimation(name, clip, layer)`：
    - 参数校验（name 非空、clip 非空、layer >= 0）。
    - 若已存在同名动画则 Warning 并返回 false。
    - 创建 `AnimClip` 加入 `_animClips`。
  - `RemoveAnimation(name)`：
    - 若不存在则 Warning 并返回 false。
    - 找到对应 Clip，定位其 `Layer` 的 `AnimMixer`，调用 `RemoveClip()` 并销毁 Clip。
  - `IsContains(name)`、`GetAnimClip(name)`、`GetAnimState(name)`、`IsPlaying(name)` 提供查询能力。

- **Layer 管理**
  - `GetAnimMixer(layer)`：按层级索引查找 Mixer。
  - `CreateAnimMixer(layer)`：
    - 确保 `_mixerRoot` 的输入口数足够（`SetInputCount`）。
    - 创建 `AnimMixer` 并加入 `_animMixers`。

- **播放/停止接口**
  - `Play(string name, float fadeLength)`：
    - 找到目标 `AnimClip`；
    - 按 `Layer` 获取或创建对应的 `AnimMixer`；
    - 若 Mixer 尚未连接，则 `Connect(_mixerRoot, mixer.Layer)`；
    - 调用 `AnimMixer.Play(animClip, fadeLength)`。
  - `Stop(string name)`：
    - 找到目标 `AnimClip`；
    - 若该 Clip 未连接则直接返回；
    - 找到对应 `AnimMixer`，调用 `Stop(animClip.Name)`。

- **设计意图**
  - 将所有 Playables 的创建、连接、更新、销毁集中管理。
  - 上层代码不直接处理 PlayableGraph，而与 `AnimPlayable` / `UniAnimation` 交互即可。

---

### 六、AnimState：动画状态视图

> 对应源码：`Runtime/AnimState.cs`

- **内部结构**
  - 持有一个 `AnimClip` 引用。
  - 仅在内部构造，由 `AnimClip` 创建，外部无法直接 new。

- **暴露属性**
  - `Name`：动画名。
  - `Length`：动画长度（考虑 `Speed` 后的可播放时长）。
  - `Layer`：所属层级。
  - `WrapMode`：当前环绕模式。
  - `Weight`：当前权重，可读写。
  - `Time`：当前时间轴位置，可读写。
  - `NormalizedTime`：归一化时间（0~1），可读写。
  - `Speed`：播放速度，可读写。

- **设计意图**
  - 为游戏逻辑提供一个简单的状态对象，方便查询与轻量控制，无需直接操作 Playable 或 AnimClip。
  - 支持在逻辑层根据动画进度实现事件触发、跳转控制等。

---

### 七、UniAnimation 组件：对外入口

> 对应源码：`Runtime/UniAnimation.cs`

- **组件依赖与字段**
  - `[RequireComponent(typeof(Animator))]`：强制要求同节点上有 Animator。
  - 内部字段：
    - `_animPlayable : AnimPlayable`
    - `_animator : Animator`
  - 序列化字段：
    - `AnimationWrapper[] _animations`：
      - 每个元素包含 `Layer`、`WrapMode Mode`、`AnimationClip Clip`。
    - `_playAutomatically`：是否启用时自动播放第一个动画。
    - `_animatePhysics`：是否使用 `AnimatePhysics` 更新模式。

- **生命周期**
  - `Awake()`：
    - 获取 `Animator`，设置其 `updateMode`（Normal / AnimatePhysics）。
    - 创建 `AnimPlayable` 并调用 `Create(_animator)`。
    - 遍历 `_animations`：
      - 为每个有效 `wrapper` 设置 `clip.wrapMode = wrapper.Mode`；
      - 调用 `_animPlayable.AddAnimation(wrapper.Clip.name, wrapper.Clip, wrapper.Layer)`。
  - `OnEnable()`：
    - 调用 `_animPlayable.PlayGraph()` 启动 Graph。
    - 若 `PlayAutomatically == true`：
      - 取 `GetDefaultWrapper()`（第一个有效动画）；
      - 调用 `Play(wrapper.Clip.name, 0f)` 播放。
    - 调用 `_animPlayable.Update(float.MaxValue)` 做一次“立即采样”。
  - `Update()`：
    - 每帧调用 `_animPlayable.Update(Time.deltaTime)`，因为 Graph 是 `Manual` 模式。
  - `OnDisable()`：
    - `_animPlayable.StopGraph()`。
  - `OnDestroy()`：
    - `_animPlayable.Destroy()`。

- **对外 API**
  - `AddAnimation(AnimationClip clip, int layer = 0)` / `RemoveAnimation(string name)`。
  - `GetState(string name)`：返回 `AnimState`。
  - `IsPlaying(string name)` / `IsContains(string name)`。
  - `Play(string name, float fadeLength = 0.25f)`。
  - `Stop(string name)`。

- **设计意图**
  - 对使用者隐藏 PlayableGraph 细节，提供类似旧 `Animation` 的调用体验：
    - Inspector 里配置动画列表；
    - 代码里通过动画名字符串和淡入时间进行控制。

---

### 八、Animator Layer 与 Avatar Mask 的概念回顾

虽然 UniAnimation 使用的是 Playables 而非 Animator Controller，但理解 Animator 的 Layer 与 Avatar Mask 有助于理解 UniAnimation 的“Layer 设计”。

- **Layer 的含义**
  - 一层可以看作一套“全身姿势计算逻辑”：自己的状态机/Blend Tree → 计算出**一整套骨骼姿势（Pose）**。
  - 多层是“姿势叠加”：上层的 Pose 在指定骨骼（由 Mask 决定）上覆盖/叠加下层结果。

- **Avatar Mask 的作用**
  - 决定**这一层影响哪些骨骼**：
    - 勾选的骨骼：该 Layer 的 Pose 在这些骨骼上参与混合。
    - 未勾选的骨骼：沿用下层的最终结果。
  - 常见用法：
    - 0 层：全身移动（Idle/Run/Jump）。
    - 1 层：上半身攻击（只勾选上半身骨骼，Layer 使用 Override）。

- **Blending / Weight**
  - `Override`：按 Weight 比例覆盖下层姿势。
  - `Additive`：在下层姿势基础上叠加偏移（常用于抖动、小动作）。
  - `Weight`：0 = 完全不生效，1 = 完全生效（在 Mask 勾选范围内）。

- **与 UniAnimation 的对应关系**
  - Animator 的 Layer 概念，对应 UniAnimation 中的 `Layer` 字段和 `AnimMixer`。
  - Avatar Mask 在 UniAnimation 中没有直接暴露，而是通过 PlayableGraph 的架构（Layer Mixer）自行管理。
  - 你可以沿用“0 层控制移动，1 层控制上半身动作”的设计思路。

---

### 九、Unity 动画计算链路（从 Clip 到顶点）

> 这是对 Unity 动画系统的简化说明，帮助理解“层混合”与“顶点插值”的关系。

1. **Clip 关键帧插值**
   - 对每个 `AnimationClip`，在当前时间 t：
     - 根据曲线插值出每个骨骼的局部变换（Position/Rotation/Scale）。

2. **每一层内部混合**
   - 在同一 Layer 内，根据状态机和 Blend Tree：
     - 可能混合多个 Clip 的结果，得到**该 Layer 的整身 Pose**（所有骨骼的局部变换）。

3. **Layer 之间混合（含 Mask、Blending、Weight）**
   - 从 Base Layer 开始，一层一层往上：
     - 对每根骨骼：
       - 若 Mask 勾选该骨骼：
         - 按 Override/Additive + Weight 混入当前 Layer 的 Pose。
       - 否则：
         - 直接沿用下层结果。
   - 最终得到**每根骨骼的最终姿势矩阵**。

4. **SkinnedMesh 蒙皮：从骨骼到顶点**
   - 对每个顶点，按绑定骨骼和权重，执行线性蒙皮（LBS）：
     - 顶点最终位置 = 各骨骼矩阵 * 顶点原始位置，再按权重加权求和。
   - 最终形成一帧的所有顶点位置，再用于渲染三角形。

> 粗略理解可以说：  
> “每一帧都会算出所有三角形的顶点新位置，但真正直接插值/混合的对象是**骨骼姿势**，顶点是通过蒙皮被‘带动’到新位置的。”

---

### 十、Update / FixedUpdate 与动画调度

> 以下为 Unity 主循环与 JobSystem 的概念化说明，帮助理解“动画/物理在一帧内何时被计算”。

- **Animator.UpdateMode = Normal**
  - 以“渲染帧”为节奏：
    1. 主线程：执行所有脚本的 `Update()`（你在这里改 Animator 参数/Trigger）。
    2. 主线程：进入动画评估阶段：
       - 推进动画状态机，计算各 Animator 的 Layer/Pose。
       - 构建并 `Schedule` 动画/蒙皮等 Job 到工作线程。
    3. 工作线程：并行执行这些 Job。
    4. 主线程：在需要结果的同步点（如 LateUpdate、渲染前）调用 `Complete`，等待 Job 完成。
    5. 主线程：执行 `LateUpdate()`（此时 Animator/Transform 姿势已稳定），然后提交渲染。
  - 多个 Animator 在这一阶段**统一处理**，内部可能并行，但先后顺序对用户不保证、也不应依赖。

- **Animator.UpdateMode = AnimatePhysics / FixedUpdate**
  - 以“物理时间步” `fixedDeltaTime` 为节奏：
    - Unity 累加真实时间，当累积时间 ≥ `fixedDeltaTime` 时，开始一轮或多轮 FixedUpdate：
      1. 主线程：调用 `FixedUpdate()`。
      2. 主线程：调度物理 + AnimatePhysics 动画 Job。
      3. 工作线程：并行执行 Job。
      4. 主线程：在该物理步结束前等待相关 Job 完成。
    - 每完成一次物理步，**模拟时间 + `fixedDeltaTime`**，与 CPU 实际耗时无关。

- **JobSystem 与主线程等待**
  - 调度（`Schedule`）在主线程进行，只是发任务很快。
  - 真正的计算在工作线程执行。
  - 当主线程需要 Job 结果（动画姿势、物理结果、蒙皮数据）时，会在同步点 `Complete`：
    - 若 Job 已完成：立即返回。
    - 若 Job 未完成：主线程阻塞等待，导致这一帧/这一物理步时间变长。

- **重负载场景（例如一次 FixedUpdate 耗时 0.5 秒）**
  - 单次 FixedUpdate 实际用时远大于 `fixedDeltaTime`：
    - 模拟时间每步只前进 0.02 秒；
    - 真实时间每步前进 0.5 秒 → **物理/动画明显跟不上真实时间**。
  - Unity 会尝试在后续帧中多跑几次 FixedUpdate 追时间，但每步都很慢时，“追帧”本身也追不动：
    - 导致整体 FPS 极低；
    - 物理/动画表现成“一卡一卡，且节奏发慢”的状态。

---

### 十一、面向使用者的 UniAnimation 使用建议

- **最低认知要求**
  - 已熟悉 Animator Controller 的基本使用（状态机、参数、Trigger、Blend Tree）。
  - 理解 Layer/Mask 的基本概念：不同 Layer 叠加姿势，上层可部分覆盖下层。

- **使用 UniAnimation 的推荐步骤**
  1. 挂载组件：
     - 在角色 GameObject 上添加 `Animator`（可以没有 Animator Controller）。
     - 再添加 `UniAnimation` 组件。
  2. Inspector 配置 `_animations`：
     - 为每个 `AnimationClip` 配置 Layer、WrapMode、Clip。
     - Clip 名即为之后脚本中使用的字符串。
  3. 脚本控制：
     - 使用：
       - `uniAnimation.Play("Run", 0.25f);`
       - `uniAnimation.IsPlaying("Run");`
       - `uniAnimation.GetState("Jump").NormalizedTime;`
     - 用 if/else / 状态机脚本来代替 Animator Controller 内部的状态图。
  4. 进阶控制：
     - 根据 `AnimState` 里的 Time / NormalizedTime / Speed / Weight 实现复杂逻辑（如技能出刀点、落地判定）。

- **整体心态转换**
  - 从“在 Animator Controller 里画状态图” → “在 C# 脚本里用字符串 API 精确控制播放”。
  - 从“参数驱动状态机切换” → “用脚本逻辑 + `Play/Stop` 明确表达切换条件”。

---

### 十二、总结

- UniAnimation 通过一套基于 Playables 的封装，提供了接近旧 `Animation` 组件的使用体验，同时保留了 Layer 混合、淡入淡出等高级能力。
- 理解 Animator 的 Layer / Avatar Mask / Pose 混合，有助于理解 UniAnimation 中 `Layer` 与 `AnimMixer` 的设计。
- 理解 Update / FixedUpdate 与 JobSystem 的大致调度流程，有助于从整体性能和时序角度看待动画播放，而不仅仅停留在 API 层。
- 在实际项目中，可以优先把 UniAnimation 当作“**代码驱动的 Animator 替代品**”来使用，待用熟后再回到源码深入理解其架构与优化思路。

---

### 十三、Walk + Attack 复合状态示例（多 Layer 叠加）

> 说明：本节用一个“边走路边攻击”的例子，帮助理解 UniAnimation 中 Layer 的组合用法。  
> 这里是对真实行为的简化描述，具体逻辑以源码为准。

#### 1. 动画资源与 Layer 配置

假设角色有 4 个动画片段：

- `idle`：待机站立，Loop。  
- `walk`：行走，Loop。  
- `runing`：奔跑（名字就叫 runing），Loop。  
- `attack`：攻击，上半身动作，Once。

在 `UniAnimation` 的 `_animations` 中配置（关键在 Layer）：

- `idle`：`Layer = 0`，`Mode = Loop`  
- `walk`：`Layer = 0`，`Mode = Loop`  
- `runing`：`Layer = 0`，`Mode = Loop`  
- `attack`：`Layer = 1`，`Mode = Once`

含义：

- **Layer 0**：移动层（控制全身/下半身的 Idle / Walk / Runing）。  
- **Layer 1**：攻击层（只控制上半身 Attack）。

`attack` 动画在制作时应尽量只给上半身（脊椎、手臂、头部）关键帧，避免修改腿部骨骼，这样在混合时下半身可以继续使用 Layer 0 的姿势。

#### 2. 基础逻辑：移动层（Layer 0）

层 0 只负责 idle / walk / runing 三个状态切换，例如：

- 未按 W：`Play("idle", 0.2f)`。  
- 按住 W：`Play("walk", 0.2f)`。  
- 按住 W + Shift：`Play("runing", 0.25f)`。

内部行为（概念化）：

- `AnimMixer(0)` 作为 Layer 0 的混合器，接在 `_mixerRoot` 的 input0 上。  
- 每次 `Play("xxx")`：
  - 将目标 Clip（如 `walk`）在 Layer 0 内淡入；
  - 将同层其它 Clip（如 `idle`、`runing`）淡出并暂停。

#### 3. 攻击层：在 Layer 1 上播放 attack

当玩家在“按住 W 走路”时点击攻击键，逻辑调用：

- `uniAnimation.Play("attack", 0.1f);`

内部行为（概念化）：

1. 通过 `AnimPlayable.Play` 找到 `AnimClip("attack")`，其 `Layer = 1`。  
2. 若尚未创建 `AnimMixer(1)`，则：  
   - 调 `CreateAnimMixer(1)`，为 `_mixerRoot` 扩展输入口并创建 Layer 1 混合器；  
   - 调 `AnimMixer(1).Connect(_mixerRoot, 1)`，将其接到根 Mixer 的 input1。  
3. 调 `AnimMixer(1).Play(attackClip, 0.1f)`：  
   - 若是首次使用 attack，则将其 `Connect` 到 `AnimMixer(1)` 内部；  
   - 在 Layer 1 内：
     - `attackClip`：权重在 0.1 秒内从 0 淡入到 1，并开始播放；  
     - 同层其他 Clip（如有）淡出并暂停。

此时：

- **Layer 0**：继续播 `walk`（或 `runing`），控制整体移动和腿部。  
- **Layer 1**：开始播 `attack`，主要影响上半身。

`AnimationLayerMixerPlayable _mixerRoot` 会按各层 Weight 和内部规则，将 Layer 0 与 Layer 1 的 Pose 叠加为最终姿势：

- 腿部骨骼：主要来自 Layer 0（`walk`）。  
- 上半身骨骼：被 Layer 1（`attack`）的姿势覆盖/叠加。

视觉效果：角色保持行走（或跑步）姿势的同时，上半身执行攻击动画，即“walk + attack”。

#### 4. 攻击结束后的自动淡出

当 `attack`（WrapMode.Once）播放到尾部：

- 对应 `AnimClip("attack")` 的 Playable 会标记 `IsDone = true`。  
- 在 `AnimMixer(1).Update` 中，当检测到所有子 Clip 均 `IsDone == true`：
  - 进入退出流程 `_isQuiting = true`；  
  - 调 `StartWeightFade(0, HIDE_DURATION)`（例如 0.25 秒），将 Layer 1 的整体权重淡至 0。  
- 当 Layer 1 的 `Weight` 接近 0 时：
  - 调 `DisconnectMixer()` 将所有子 Clip 与 Layer 1 Mixer 断开，并将 Layer 1 Mixer 从 `_mixerRoot` 上断开。

在游戏逻辑中，如果有类似：

- 在 `Update` 中：当 `IsPlaying("attack") == false` 时，恢复根据输入驱动 idle/walk/runing，

则可以实现：

- 攻击播放期间上半身覆盖移动层；  
- 攻击结束后自动恢复到仅由 Layer 0 控制的移动状态。

---

### 十四、AnimationMixerPlayable 与 AnimationLayerMixerPlayable 的区别

> 说明：本节是对 Unity 两种常用动画 Playable 的角色区分，帮助理解 UniAnimation 中 `AnimMixer` 与 `_mixerRoot` 的设计。

#### 1. AnimationMixerPlayable：同一层内部的“片段混合器”

- 在 UniAnimation 中，对应：`AnimMixer` 内部的 `_mixer : AnimationMixerPlayable`。  
- 作用对象：**同一个 Layer 内的多条 `AnimClip`**，例如 Layer 0 上的 `idle / walk / runing`。  
- 工作方式：
  - 每条 `AnimClip` 通过 `Connect(_mixer, inputIndex)` 接到 `_mixer` 的某个 input。  
  - 每条 Clip 的 `Weight`（父为 `_mixer`）决定该 Clip 在本层中的占比：  
    - 过渡期：旧 Clip Weight 从 1 → 0，新 Clip 从 0 → 1；  
    - 稳定期：通常只有一个 Clip 的 Weight ≈ 1，其它 ≈ 0。  
  - Mixer 输出的是**这一层的一整套骨骼姿势（Pose）**。

可以把 `AnimationMixerPlayable` 理解成“**这一层的歌单混音器**”：选择同一层里哪一首歌在主导（Idle/Walk/Run 之间切换）。

#### 2. AnimationLayerMixerPlayable：多层之间的“总混合器”

- 在 UniAnimation 中，对应：`AnimPlayable` 里的 `_mixerRoot : AnimationLayerMixerPlayable`。  
- 作用对象：**每一层已经混合好的姿势**，例如：
  - input0：Layer 0 的 `AnimMixer(0)._mixer`（移动层）；  
  - input1：Layer 1 的 `AnimMixer(1)._mixer`（攻击层）；  
  - ……
- 工作方式：
  - 对每个 input（每层）有一个整体的 Layer Weight（在 UniAnimation 中由 `AnimMixer.Weight` 控制）。  
  - 若配合 Avatar Mask 使用，可以只在部分骨骼上应用某一层的姿势。  
  - 最终输出的是**角色的总姿势**，供 `AnimationPlayableOutput` / `Animator` 使用。

可以把 `AnimationLayerMixerPlayable` 理解成“**整首歌的总调音台**”：  
它不关心单层里具体是 Idle 还是 Walk，只关心“底鼓层音量多少、人声层音量多少”，最后混成整首歌。

---

### 十五、同层权重与跨层权重的作用

> 说明：本节总结“同一层内的 Clip.Weight”与“不同层之间的 Layer Weight（`AnimMixer.Weight`）”分别起什么作用。

#### 1. 同一层内的权重：Clip.Weight

- 对象：`AnimClip.Weight`（父为 `AnimationMixerPlayable`）。  
- 真实调用点：
  - 在 `AnimNode.Weight` 中，通过 `_parent.SetInputWeight(InputPort, value)` 设置。  
  - 在 `AnimMixer.Play` 中，对新旧 Clip 分别调用 `StartWeightFade(1/0, fadeDuration)`，形成淡入淡出。  
- 作用：
  - 在**同一 Layer 内的多条动画片段之间**做平滑过渡（cross-fade）。  
  - 例如在 Layer 0 上从 `idle` 切到 `walk`：
    - 若 `fadeDuration = 0.2` 秒：
      - 0.2 秒内：`idle.Weight` 从 1 渐变到 0，`walk.Weight` 从 0 渐变到 1；  
      - 这一小段时间内，两条 Clip 同时参与混合，生成平滑的过渡姿势。  
    - 过渡完成后：`walk.Weight ≈ 1`，`idle.Weight ≈ 0`。

#### 2. 不同层之间的权重：AnimMixer.Weight（Layer Weight）

- 对象：`AnimMixer.Weight`（父为 `_mixerRoot : AnimationLayerMixerPlayable`）。  
- 设置方式：
  - 通过 `AnimNode.StartWeightFade` 在 `AnimMixer.Update` 中对自身权重进行淡入淡出。  
  - 例如：当某层所有子 Clip `IsDone == true` 时，`AnimMixer` 会在 `HIDE_DURATION` 时间内将自身 Weight 从 1 淡出到 0，并在结束后 `DisconnectMixer()`。
- 作用：
  - 控制**这一整层（移动层 / 攻击层 / 表情层等）对最终角色姿势的影响有多大**。  
  - 典型用法：
    - Walk + Attack：  
      - Layer 0（移动层）Weight = 1，一直存在；  
      - Layer 1（攻击层）在攻击开始时 Weight 从 0 → 1，攻击结束后 Weight 从 1 → 0 并断开；  
      - 结果是：下半身始终沿用 Layer 0 的 walk，上半身在 Layer 1 激活时叠加 attack。

#### 3. 合并理解

- **Clip.Weight（同层）**：  
  - 决定“这一层内部当前由哪条动画主导”，负责**片段之间的过渡**。  
- **AnimMixer.Weight（跨层）**：  
  - 决定“这一层整体对最终角色的影响程度”，负责**整层的开关与淡入淡出**。  
- 二者叠加后，UniAnimation 可以：
  - 在单层内平滑切换 Idle / Walk / Run；  
  - 在多层之间平滑叠加 Walk + Attack / Idle + 上半身表情等复合状态。



