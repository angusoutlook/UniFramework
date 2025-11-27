## UniPooling 使用指南（简体中文）

> 基于 `UniPooling/Runtime` 目录下的真实代码整理，说明保持与源码一致，仅做文字归纳。

### 一、架构概览

- **入口单例 `UniPooling`**
  - 负责整个对象池系统的初始化、销毁和 `Spawner` 管理。
  - `Initalize()`：创建一个名为 `"[UniPooling]"` 的 `GameObject`，挂载 `UniPoolingDriver`，并 `DontDestroyOnLoad`，防止重复初始化。
  - `Destroy()`：依次销毁所有 `Spawner`，清空列表后销毁驱动器 `GameObject`。
  - `CreateSpawner(string packageName)`：为指定 YooAsset `ResourcePackage` 创建或返回一个 `Spawner`。

- **驱动器 `UniPoolingDriver`**
  - 一个挂在 `"[UniPooling]"` 节点上的 `MonoBehaviour`，在 `Update()` 中调用 `UniPooling.Update()`。
  - 保证对象池的自动更新和回收逻辑每帧执行。

- **孵化器 `Spawner`**
  - 每个 `Spawner` 绑定一个 `ResourcePackage`，并在场景层级中有自己的根节点 `_spawnerRoot`。
  - 管理多个 `GameObjectPool`，按资源定位地址（`location`，如 `"Cube.prefab"`）区分。
  - 提供：
    - 创建对象池：`CreateGameObjectPoolAsync / CreateGameObjectPoolSync`
    - 实例化对象：`SpawnAsync / SpawnSync` 多个重载
    - 销毁所有池：`DestroyAll(bool includeAll)`

- **对象池 `GameObjectPool`**
  - 针对单一 `location` 的对象池，内部有：
    - `_root`：该池在层级里的根节点。
    - `_cacheOperations`：`InstantiateOperation` 队列，缓存已实例化但未使用的对象。
    - 容量与销毁参数：`_initCapacity`、`_maxCapacity`、`_destroyTime`、`_dontDestroy`。
    - 计数：`SpawnCount`（在外使用中的数量）、`_lastRestoreRealTime`（最后一次完全空闲时间）。
  - 负责：
    - 创建初始缓存：`CreatePool(ResourcePackage package)`
    - 回收：`Restore(InstantiateOperation operation)`
    - 丢弃：`Discard(InstantiateOperation operation)`
    - 自动销毁判断：`CanAutoDestroy()`

- **异步操作封装**
  - `SpawnHandle : GameAsyncOperation`：封装单次实例化操作及其回收/丢弃逻辑。
  - `CreatePoolOperation : GameAsyncOperation`：封装“创建对象池”的异步状态。
  - 都支持：
    - `yield return operation`（协程异步）
    - `WaitForAsyncComplete()`（转为同步等待）

- **日志工具 `UniLogger`**
  - `Log` 仅在 `DEBUG` 下输出，`Warning` 和 `Error` 始终可用。

### 二、生命周期与自动回收设计

- **初始化阶段**
  - 先按 YooAsset 流程初始化 `YooAssets` 和对应 `ResourcePackage`。
  - 再调用 `UniPooling.Initalize()`，系统会创建驱动对象并常驻。

- **对象池创建阶段**
  - `GameObjectPool.CreatePool(ResourcePackage package)`：
    - 通过 `package.LoadAssetAsync<GameObject>(Location)` 获取 `AssetHandle`。
    - 循环 `_initCapacity` 次：
      - 调用 `Handle.InstantiateAsync(_root.transform)` 实例化。
      - 注册 `Completed` 回调并入队 `_cacheOperations`。
  - `Operation_Completed` 中：
    - 当异步实例化成功时，将 `op.Result.SetActive(false)`，使缓存对象在场景中处于“隐藏状态”。

- **静默自动销毁**
  - 回收逻辑 `Restore` / `Discard` 会更新 `SpawnCount` 和 `_lastRestoreRealTime`。
  - `CanAutoDestroy()` 满足条件时返回 `true`：
    - 不是常驻池（`_dontDestroy == false`）
    - 配置了静默销毁时间（`_destroyTime >= 0`）
    - 对象池当前没有外部使用（`SpawnCount <= 0`）
    - 自 `_lastRestoreRealTime` 起已超过 `_destroyTime`
  - `Spawner.Update()` 每帧检查所有池，通过 `CanAutoDestroy()` 判定是否销毁。

### 三、同步 / 异步调用方式

- **创建对象池**
  - 异步：
    - `CreateGameObjectPoolAsync(string location, bool dontDestroy = false, int initCapacity = 0, int maxCapacity = int.MaxValue, float destroyTime = -1f)`
    - 返回 `CreatePoolOperation`，可：
      - `yield return operation;`
      - 或调用 `operation.WaitForAsyncComplete();`
  - 同步：
    - `CreateGameObjectPoolSync(...)` 内部直接调用 `WaitForAsyncComplete()` 并返回。

- **孵化（实例化）对象**
  - 异步：
    - `SpawnAsync(string location, ...)` → 返回 `SpawnHandle`
    - 在协程中 `yield return handle;`，完成后通过 `handle.GameObj` 访问实例。
  - 同步：
    - `SpawnSync(string location, ...)` → 内部 `WaitForAsyncComplete()`，直接返回已就绪的 `SpawnHandle`。

### 四、对象激活（SetActive）行为说明

#### 1. 创建缓存时为什么 `SetActive(false)`？

在 `GameObjectPool` 中，创建初始缓存对象时，回调里有如下逻辑：

```23:89:UniPooling/Runtime/GameObjectPool.cs
private void Operation_Completed(AsyncOperationBase obj)
{
    if (obj.Status == EOperationStatus.Succeed)
    {
        var op = obj as InstantiateOperation;
        if (op.Result != null)
            op.Result.SetActive(false);
    }
}
```

- 这些对象被预先实例化出来，但暂时只作为“缓存”，并不应出现在场景中。
- 因此统一 `SetActive(false)`，确保不会在未孵化前误参与游戏逻辑或渲染。

#### 2. 孵化（Spawn）时是否需要手动 `SetActive(true)`？

**结论：不需要。**

在 `SpawnHandle` 中，真正完成孵化时，代码会自动激活对象：

```56:93:UniPooling/Runtime/SpawnHandle.cs
protected override void OnUpdate()
{
    if (_steps == ESteps.None || _steps == ESteps.Done)
        return;

    if (_steps == ESteps.Waiting)
    {
        if (_operation.IsDone == false)
            return;

        if (_operation.Status != EOperationStatus.Succeed)
        {
            _steps = ESteps.Done;
            Status = EOperationStatus.Failed;
            Error = _operation.Error;
            return;
        }

        if (_operation.Result == null)
        {
            _steps = ESteps.Done;
            Status = EOperationStatus.Failed;
            Error = $"Clone game object is null.";
            return;
        }

        // 设置参数	
        _operation.Result.transform.SetParent(_parent);
        _operation.Result.transform.SetPositionAndRotation(_position, _rotation);
        _operation.Result.SetActive(true);   // 这里自动激活

        _steps = ESteps.Done;
        Status = EOperationStatus.Succeed;
    }
}
```

对应到使用层面：

- 使用 `Spawner.SpawnAsync(...)`：
  - `yield return handle;` 之后，`handle.GameObj` 已经是 **激活状态**。
- 使用 `Spawner.SpawnSync(...)`：
  - 方法返回时，内部已完成 `WaitForAsyncComplete()`，`handle.GameObj` 同样是 **激活状态**。

因此：

- 正常通过 `Spawner.SpawnAsync / SpawnSync` + `SpawnHandle` 使用对象池时，**不要额外再手动 `SetActive(true)`**，否则只是重复设置。
- 只有在你后续业务逻辑中**自己主动关闭了对象**（例如 `GameObj.SetActive(false)`，并且不是通过 `Restore()` 回收），才需要你自己在合适的时机重新激活。

### 五、回收与丢弃的区别

- **回收（Restore）**
  - 调用：`handle.Restore();`
  - 内部流程：
    - 清除 `GameAsyncOperation` 的完成回调，修改自身状态。
    - 调用 `_pool.Restore(_operation)`：
      - 若池未销毁且缓存未满：
        - 对象被 `SetActive(false)`，并重置父节点为池根、位置为 `Vector3.zero`、旋转为 `Quaternion.identity`。
        - 对应的 `InstantiateOperation` 被重新放入 `_cacheOperations`，下次优先复用。
      - 若池已销毁或缓存已满，则直接销毁实例。

- **丢弃（Discard）**
  - 调用：`handle.Discard();`
  - 内部流程：
    - 同样清除回调、修改状态。
    - 调用 `_pool.Discard(_operation)`：
      - 不进入缓存池，直接取消操作并销毁 GameObject。
  - 适用于不希望该实例再次被复用的场景。

### 六、典型使用流程（概念版）

1. **初始化**
   - 初始化 YooAsset 包（如 `"DefaultPackage"`），确保 `InitializeStatus == Succeed`。
   - 调用 `UniPooling.Initalize()`。
2. **创建 Spawner**
   - `var spawner = UniPooling.CreateSpawner("DefaultPackage");`
3. **（可选）预先创建某个 prefab 的对象池**
   - 异步：`var op = spawner.CreateGameObjectPoolAsync("Cube.prefab", dontDestroy:false, initCapacity:10, maxCapacity:50, destroyTime:60f);`
   - 同步：`var op = spawner.CreateGameObjectPoolSync("Cube.prefab", ...);`
4. **孵化对象**
   - 异步：`SpawnHandle handle = spawner.SpawnAsync("Cube.prefab"); yield return handle; // 此时 handle.GameObj 已经 SetActive(true)`
   - 同步：`SpawnHandle handle = spawner.SpawnSync("Cube.prefab"); // 返回时已经激活`
5. **使用完成后回收或丢弃**
   - 复用：`handle.Restore();`
   - 不复用：`handle.Discard();`
6. **退出或切场景时清理**
   - 销毁某个包下的所有对象池：`spawner.DestroyAll(includeAll: true/false);`
   - 销毁整个对象池系统：`UniPooling.Destroy();`

以上内容即为当前源码下 UniPooling 的架构、生命周期与 `SetActive` 行为的精炼说明，可作为使用该模块时的参考文档。


### 七、核心类型与成员详解（按源码逐项说明）

> 本节按源码逐个类型、逐个重要成员说明行为，避免遗漏实现细节。

#### 1. `UniPooling`（入口静态类）

- **字段**
  - `_isInitialize : bool`  
    - 标记系统是否已初始化；重复初始化会抛异常。
  - `_driver : GameObject`  
    - 对应场景中的 `"[UniPooling]"` 根节点，挂载 `UniPoolingDriver`，`DontDestroyOnLoad`。
  - `_spawners : List<Spawner>`  
    - 存放当前所有 `Spawner` 实例，每个对应一个 YooAsset 包。

- **方法**
  - `public static void Initalize()`  
    - 若 `_isInitialize` 已为 `true`，直接抛出 `Exception($"{nameof(UniPooling)} is initialized !")`。  
    - 否则：
      - 将 `_isInitialize` 置为 `true`；
      - 创建 `GameObject($"[{nameof(UniPooling)}]")` 赋给 `_driver`；
      - `_driver.AddComponent<UniPoolingDriver>()`；
      - `Object.DontDestroyOnLoad(_driver)`；
      - 通过 `UniLogger.Log` 打印初始化日志。
  - `public static void Destroy()`  
    - 仅在 `_isInitialize` 为 `true` 时执行销毁：
      - 遍历 `_spawners` 调用 `spawner.Destroy()`（内部会销毁其管理的所有对象池）；
      - 清空 `_spawners`；
      - 将 `_isInitialize` 置为 `false`；
      - 销毁 `_driver` GameObject（如果不为 null）；
      - 打印销毁日志。
  - `internal static void Update()`  
    - 在 `_isInitialize` 为 `true` 时：
      - 遍历 `_spawners`，调用每个 `spawner.Update()`，用于自动回收和清理对象池。
  - `public static Spawner CreateSpawner(string packageName)`  
    - 通过 `YooAssets.GetPackage(packageName)` 获取 `ResourcePackage`：
      - 若为 null，抛异常 `"Not found asset package : {packageName}"`；
      - 若 `InitializeStatus == None`，抛异常 `"Asset package {packageName} not initialize !"`；
      - 若 `InitializeStatus == Failed`，抛异常 `"Asset package {packageName} initialize failed !"`。
    - 若已存在同名 spawner（`HasSpawner` 为 true），直接返回 `GetSpawner(packageName)`。
    - 否则：
      - 使用 `_driver` 和 `assetPackage` 创建新的 `Spawner`；
      - 加入 `_spawners` 并返回。
  - `public static Spawner GetSpawner(string packageName)`  
    - 遍历 `_spawners`，按 `spawner.PackageName` 精确匹配；找不到时：
      - `UniLogger.Warning($"Not found spawner : {packageName}")`；
      - 返回 null。
  - `public static bool HasSpawner(string packageName)`  
    - 遍历 `_spawners`，存在同名则返回 true，否则 false。

#### 2. `UniPoolingDriver`（驱动器 MonoBehaviour）

- 挂在 `"[UniPooling]"` GameObject 上。
- `void Update()`：
  - 每帧调用 `UniPooling.Update()`，从而间接触发所有 `Spawner.Update()` 和对象池的自动清理逻辑。

#### 3. `Spawner`（按资源包划分的孵化器）

- **字段与属性**
  - `_gameObjectPools : List<GameObjectPool>`  
    - 管理当前资源包下所有 `GameObjectPool`。
  - `_removeList : List<GameObjectPool>`  
    - 每帧更新时临时存放需要被销毁的对象池。
  - `_spawnerRoot : GameObject`  
    - 在场景层级中的根节点，以包名命名，并作为所有池 `_root` 的父节点。
  - `_package : ResourcePackage`  
    - YooAsset 的资源包引用。
  - `public string PackageName => _package.PackageName;`

- **构造**
  - 内部构造：`internal Spawner(GameObject poolingRoot, ResourcePackage package)`  
    - 创建 `_spawnerRoot = new GameObject($"{package.PackageName}")`，并设置其父节点为 `poolingRoot.transform`（即 `"[UniPooling]"`）。
    - 记录 `_package = package`。

- **更新与销毁**
  - `internal void Update()`  
    - 清空 `_removeList`；
    - 遍历 `_gameObjectPools`，对每个 `pool` 调用 `pool.CanAutoDestroy()`：
      - 若返回 true，将该 pool 加入 `_removeList`。
    - 再遍历 `_removeList`：
      - 从 `_gameObjectPools` 移除；
      - 调用 `pool.DestroyPool()` 释放资源与节点。
  - `internal void Destroy()`  
    - 调用 `DestroyAll(true)`，销毁当前 `Spawner` 下的所有对象池，包括常驻池。
  - `public void DestroyAll(bool includeAll)`  
    - `includeAll == true`：
      - 直接遍历 `_gameObjectPools`，对每个调用 `DestroyPool()`，然后清空列表。
    - `includeAll == false`：
      - 遍历 `_gameObjectPools`，将 `pool.DontDestroy == false` 的池收集到临时 `removeList`；
      - 再逐个从 `_gameObjectPools` 移除并 `DestroyPool()`。

- **创建对象池**
  - `public CreatePoolOperation CreateGameObjectPoolAsync(...)`  
    - 直接调用私有方法 `CreateGameObjectPoolInternal(...)` 并返回结果。
  - `public CreatePoolOperation CreateGameObjectPoolSync(...)`  
    - 调用 `CreateGameObjectPoolInternal(...)` 得到 `operation`；
    - 调用 `operation.WaitForAsyncComplete()` 将创建过程同步完成；
    - 返回 `operation`。
  - `private CreatePoolOperation CreateGameObjectPoolInternal(string location, bool dontDestroy = false, int initCapacity = 0, int maxCapacity = int.MaxValue, float destroyTime = -1f)`  
    - 若 `maxCapacity < initCapacity`，直接抛异常 `"The max capacity value must be greater the init capacity value."`。
    - 通过 `TryGetGameObjectPool(location)` 查找已有池：
      - 若存在：
        - 打一条警告：`UniLogger.Warning($"GameObject pool is already existed : {location}")`；
        - 直接基于已存在池的 `pool.Handle` 创建新的 `CreatePoolOperation`；
        - 使用 `YooAssets.StartOperation(operation)` 启动该操作；
        - 返回该 operation（表示“创建池”的异步流程已经完成或正在完成）。
      - 若不存在：
        - 新建 `GameObjectPool`，传入 `_spawnerRoot`、`location`、`dontDestroy`、`initCapacity`、`maxCapacity`、`destroyTime`；
        - 调用 `pool.CreatePool(_package)` 真正执行加载与预热；
        - 将 pool 加入 `_gameObjectPools`；
        - 生成 `CreatePoolOperation`（同样使用 `pool.Handle`），并通过 `YooAssets.StartOperation(operation)` 启动；
        - 返回该 operation。

- **实例化对象（Spawn 系列）**
  - 提供多组重载，分别支持：
    - 是否指定 `parent : Transform`
    - 是否指定 `position : Vector3` 和 `rotation : Quaternion`
    - 是否强制克隆 `forceClone`
    - 是否同步等待完成（`SpawnSync` 内部调用 `WaitForAsyncComplete()`）
  - 所有重载最终都调用：
    - `private SpawnHandle SpawnInternal(string location, Transform parent, Vector3 position, Quaternion rotation, bool forceClone, params object[] userDatas)`
  - `SpawnInternal` 逻辑：
    - 先通过 `TryGetGameObjectPool(location)` 查找对象池：
      - 若找到：
        - 直接 `return pool.Spawn(parent, position, rotation, forceClone, userDatas);`
      - 若没找到：
        - 立即创建一个**默认配置**的临时对象池：
          - `dontDestroy = false`
          - `initCapacity = 0`
          - `maxCapacity = int.MaxValue`
          - `destroyTime = -1f`（不自动销毁）
        - 调用 `pool.CreatePool(_package)` 触发加载；
        - 将 pool 加入 `_gameObjectPools`；
        - 立刻 `return pool.Spawn(parent, position, rotation, forceClone, userDatas);`。

- **辅助方法**
  - `private GameObjectPool TryGetGameObjectPool(string location)`  
    - 在线性遍历 `_gameObjectPools` 中按 `pool.Location` 精确匹配，找到则返回池，否则返回 null。

#### 4. `GameObjectPool`（单资源对象池）

- **字段与属性**
  - `_root : GameObject`  
    - 对应该池的根节点，名为 `location`，其父节点为 `poolingRoot`（即 Spawner 的 `_spawnerRoot`）。
  - `_cacheOperations : Queue<InstantiateOperation>`  
    - 存放已实例化完毕并当前未被使用的对象。队列容量初始为 `initCapacity`。
  - `_dontDestroy : bool`  
    - 是否为常驻池。为 true 时不会被静默自动销毁。
  - `_initCapacity, _maxCapacity, _destroyTime : int/float`  
    - 初始容量、最大容量、静默销毁时间（<0 表示不自动销毁）。
  - `_lastRestoreRealTime : float`  
    - 最近一次 `SpawnCount` 归零时的 `Time.realtimeSinceStartup`。
  - `public AssetHandle Handle { get; private set; }`  
    - YooAsset 的资源句柄，用来加载和实例化 `GameObject`。
  - `public string Location { get; private set; }`  
    - 当前池对应的资源定位地址。
  - `public int CacheCount => _cacheOperations.Count;`  
    - 当前内部缓存的对象数量（队列长度）。
  - `public int SpawnCount { get; private set; }`  
    - 当前处于“在外被使用”的实例数量。
  - `public bool DontDestroy => _dontDestroy;`

- **构造**
  - `public GameObjectPool(GameObject poolingRoot, string location, bool dontDestroy, int initCapacity, int maxCapacity, float destroyTime)`  
    - 创建 `_root = new GameObject(location)`，父节点设为 `poolingRoot.transform`；
    - 记录 `Location` 和各配置参数；
    - 使用 `initCapacity` 创建 `_cacheOperations` 的初始容量。

- **创建与销毁**
  - `public void CreatePool(ResourcePackage package)`  
    - 调用 `Handle = package.LoadAssetAsync<GameObject>(Location)` 异步加载 prefab。
    - 循环 `_initCapacity` 次：
      - `var operation = Handle.InstantiateAsync(_root.transform);`
      - `operation.Completed += Operation_Completed;`
      - `_cacheOperations.Enqueue(operation);`
  - `private void Operation_Completed(AsyncOperationBase obj)`  
    - 若 `obj.Status == EOperationStatus.Succeed`：
      - 将结果强转为 `InstantiateOperation`；
      - 若 `op.Result != null`，执行 `op.Result.SetActive(false)`。
  - `public void DestroyPool()`  
    - 调用 `Handle.Release()` 卸载资源；
    - 将 `Handle = null`；
    - 销毁 `_root` GameObject；
    - 清空 `_cacheOperations`；
    - 将 `SpawnCount` 置 0。

- **状态与销毁判定**
  - `public bool CanAutoDestroy()`  
    - 若 `_dontDestroy == true`，返回 false；
    - 若 `_destroyTime < 0`，返回 false；
    - 若 `_lastRestoreRealTime > 0 && SpawnCount <= 0`：
      - 返回 `(Time.realtimeSinceStartup - _lastRestoreRealTime) > _destroyTime`；
    - 否则返回 false。
  - `public bool IsDestroyed()`  
    - 简单判断 `Handle == null`。

- **回收与丢弃**
  - `public void Restore(InstantiateOperation operation)`  
    - 若 `IsDestroyed()` 为 true，说明池已销毁：
      - 直接调用 `DestroyInstantiateOperation(operation)` 并返回。
    - 否则：
      - `SpawnCount--`；
      - 若 `SpawnCount <= 0`，将 `_lastRestoreRealTime` 记录为 `Time.realtimeSinceStartup`；
      - 若 `operation.Status == EOperationStatus.Succeed` 且 `operation.Result == null`，说明外部已经销毁了对象，直接返回；
      - 若 `_cacheOperations.Count < _maxCapacity`（缓存未满）：
        - 调用 `SetRestoreGameObject(operation.Result)`：
          - `SetActive(false)`、重设父节点为 `_root`、位置和旋转归零；
        - `_cacheOperations.Enqueue(operation)`；
      - 否则（缓存已满）：
        - 调用 `DestroyInstantiateOperation(operation)`，完全销毁该实例。
  - `public void Discard(InstantiateOperation operation)`  
    - 若 `IsDestroyed()` 为 true，同样直接 `DestroyInstantiateOperation(operation)` 并返回。
    - 否则：
      - `SpawnCount--`；
      - 若 `SpawnCount <= 0`，更新 `_lastRestoreRealTime`；
      - 不进入缓存，直接 `DestroyInstantiateOperation(operation)`。

- **实例化（从池中取或克隆）**
  - `public SpawnHandle Spawn(Transform parent, Vector3 position, Quaternion rotation, bool forceClone, params object[] userDatas)`  
    - 若 `forceClone == false && _cacheOperations.Count > 0`：
      - 从 `_cacheOperations` 中 `Dequeue()` 一个 `InstantiateOperation`；
    - 否则：
      - 调用 `Handle.InstantiateAsync()` 创建新的异步实例；
    - `SpawnCount++`；
    - 使用当前 pool、operation、parent、position、rotation、userDatas 创建 `SpawnHandle`：
      - `SpawnHandle handle = new SpawnHandle(this, operation, parent, position, rotation, userDatas);`
      - 通过 `YooAssets.StartOperation(handle)` 启动该异步操作；
    - 返回 `handle`。

- **内部辅助**
  - `private void DestroyInstantiateOperation(InstantiateOperation operation)`  
    - 调用 `operation.Cancel()` 取消异步；
    - 若 `operation.Result != null`，销毁对应的 GameObject。
  - `private void SetRestoreGameObject(GameObject gameObj)`  
    - 若 `gameObj != null`：
      - `gameObj.SetActive(false)`；
      - 父节点设为 `_root.transform`；
      - `transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity)`。

#### 5. `SpawnHandle`（单次实例化操作句柄）

- **字段与属性**
  - `_pool : GameObjectPool`  
    - 指向当前实例所属的对象池。
  - `_operation : InstantiateOperation`  
    - YooAsset 的实例化操作对象。
  - `_parent : Transform`、`_position : Vector3`、`_rotation : Quaternion`  
    - 最终要设置给实例的父节点和变换。
  - `_steps : ESteps`  
    - 内部状态机，枚举值：`None / Waiting / Done`。
  - `public GameObject GameObj`  
    - 若 `_operation == null`，使用 `UniLogger.Warning("The spawn handle is invalid !")` 打警告并返回 null；
    - 否则返回 `_operation.Result`。
  - `public object[] UserDatas { get; private set; }`  
    - 用户自定义数据数组，仅做简单存储，不参与逻辑。

- **生命周期方法**
  - `protected override void OnStart()`  
    - 将 `_steps` 置为 `ESteps.Waiting`。
  - `protected override void OnUpdate()`  
    - 若 `_steps == None` 或 `_steps == Done`，直接返回；
    - 在 `Waiting` 状态：
      - 若 `_operation.IsDone == false`，表示实例化尚未完成，直接返回，等待下一帧；
      - 若 `_operation.Status != EOperationStatus.Succeed`：
        - 将 `_steps` 置为 `Done`；
        - 将 `Status` 置为 `Failed`，`Error = _operation.Error`；
        - 返回；
      - 若 `_operation.Result == null`：
        - `_steps = Done`；
        - `Status = Failed`，`Error = "Clone game object is null."`；
        - 返回；
      - 否则（成功且有实例）：
        - 设置父节点、位置、旋转：
          - `_operation.Result.transform.SetParent(_parent);`
          - `_operation.Result.transform.SetPositionAndRotation(_position, _rotation);`
        - 调用 `_operation.Result.SetActive(true);` 激活对象；
        - `_steps = Done`；
        - `Status = Succeed`。
  - `protected override void OnAbort()`  
    - 当前实现为空（留作扩展点）。

- **回收与丢弃（与对象池交互）**
  - `public void Restore()`  
    - 若 `_operation != null`：
      - 调用 `ClearCompletedCallback()` 清除 `GameAsyncOperation` 的完成回调；
      - 调用 `CancelHandle()` 更新自身状态：
        - 若 `IsDone == false`，则：
          - `_steps = Done`；
          - `Status = Failed`；
          - `Error = "User cancelled !"`；
      - 调用 `_pool.Restore(_operation)`；
      - 将 `_operation = null`，表示该 `SpawnHandle` 不再持有有效实例。
  - `public void Discard()`  
    - 逻辑与 `Restore()` 类似，只是使用 `_pool.Discard(_operation)`，从而完全销毁实例而不返回缓存。

- **同步等待**
  - `public void WaitForAsyncComplete()`  
    - 若 `_operation != null`：
      - 若 `_steps == Done` 直接返回；
      - 否则调用 `_operation.WaitForAsyncComplete()` 等待 YooAsset 实例化完成；
      - 再手动调用一次 `OnUpdate()` 完成父节点、变换设置与激活逻辑。

#### 6. `CreatePoolOperation`（创建对象池的异步操作）

- **字段**
  - `_handle : AssetHandle`  
    - 创建对象池时传入的资源句柄。
  - `_steps : ESteps`  
    - 同样使用 `None / Waiting / Done` 三段状态。

- **生命周期**
  - 构造：`internal CreatePoolOperation(AssetHandle handle)` 记录 `_handle`。
  - `protected override void OnStart()`  
    - 将 `_steps` 置为 `Waiting`。
  - `protected override void OnUpdate()`  
    - 若 `_steps == None` 或 `Done`，直接返回；
    - 在 `Waiting` 状态：
      - 若 `_handle.IsValid == false`：
        - `_steps = Done`；
        - `Status = Failed`；
        - `Error = "AssetHandle is invalid."`；
        - 返回；
      - 若 `_handle.IsDone == false`，说明资源仍在加载中，直接返回；
      - 若 `_handle.AssetObject == null`：
        - `_steps = Done`；
        - `Status = Failed`；
        - `Error = "AssetHandle.AssetObject is null."`；
        - 返回；
      - 否则：
        - `_steps = Done`；
        - `Status = Succeed`。
  - `protected override void OnAbort()`  
    - 当前为空实现。
  - `public void WaitForAsyncComplete()`  
    - 若 `_handle != null` 且 `_steps != Done`：
      - 调用 `_handle.WaitForAsyncComplete()` 等待资源加载结束；
      - 再调用 `OnUpdate()` 推进状态机，最终得到 `Succeed/Failed` 结果。

#### 7. `UniLogger`（日志工具）

- 位于 `UniFramework.Pooling` 命名空间，内部静态类。
- `Log(string info)`：
  - 使用 `[Conditional("DEBUG")]` 标记，仅在编译符号包含 `DEBUG` 时才会真正调用 `UnityEngine.Debug.Log(info)`。
- `Warning(string info)`：
  - 始终调用 `UnityEngine.Debug.LogWarning(info)`。
- `Error(string info)`：
  - 始终调用 `UnityEngine.Debug.LogError(info)`。

---

以上为 `UniPooling/Runtime` 下各核心类型和主要成员的完整行为说明，尽量逐项覆盖源码实现细节，方便你在阅读或修改源码时对照理解。若后续你有新的接口或改动，也可以按本节风格补充新的条目。


