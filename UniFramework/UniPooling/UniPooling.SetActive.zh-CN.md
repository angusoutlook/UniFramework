## UniPooling 中 SetActive 行为说明（缓存与孵化）

> 本文完全基于 `UniPooling/Runtime` 目录下的真实代码，说明缓存阶段和孵化阶段对 `GameObject.SetActive` 的处理方式，以及在业务代码中应该如何正确使用。

### 一、缓存阶段：创建对象池时为什么要 SetActive(false)？

对象池在创建初始缓存对象时，会为每一次异步实例化注册完成回调，并在回调中将实例隐藏：

```csharp
// 摘自：UniPooling/Runtime/GameObjectPool.cs
// 方法：Operation_Completed
if (obj.Status == EOperationStatus.Succeed)
{
    var op = obj as InstantiateOperation;
    if (op.Result != null)
        op.Result.SetActive(false);
}
```

结合 `GameObjectPool.CreatePool(ResourcePackage package)` 的逻辑可以看到：

- 对象池会根据 `_initCapacity` 预先实例化一批对象（预热）。
- 这些对象仅作为**缓存**存在，暂时不参与游戏逻辑和渲染。
- 为防止它们在未“孵化”前出现在场景中，统一执行 `SetActive(false)`。

结论：

- **缓存阶段的 SetActive(false) 是对象池内部的设计细节，用来保证预热对象不会提前出现在场景中。**

### 二、孵化阶段：Spawn 时是否需要手动 SetActive(true)？

当你调用 `Spawner.SpawnAsync(...)` 或 `Spawner.SpawnSync(...)` 时，最终都会通过 `SpawnHandle` 来驱动一次实例化（或从缓存中取出）流程。

在 `SpawnHandle` 的状态机中，真正完成孵化的逻辑如下：

```csharp
// 摘自：UniPooling/Runtime/SpawnHandle.cs
// 方法：OnUpdate
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
_operation.Result.SetActive(true);   // ★ 这里自动激活实例

_steps = ESteps.Done;
Status = EOperationStatus.Succeed;
```

对应使用方式：

- **异步孵化：**
  - 你通常会写成：
    - `SpawnHandle handle = spawner.SpawnAsync("Cube.prefab");`
    - `yield return handle;`
  - 当协程继续往下执行时，`handle.GameObj` 对应的实例已经在 `OnUpdate` 中完成了：
    - 设置父节点 `_parent`
    - 设置位置与旋转
    - 调用 `SetActive(true)` 变为激活状态

- **同步孵化：**
  - 使用 `SpawnSync(...)` 系列接口时，内部会调用 `handle.WaitForAsyncComplete()`，并在此过程中触发同样的 `OnUpdate` 逻辑。
  - 所以当 `SpawnSync` 返回时，`handle.GameObj` 同样是已激活的。

结论：

- **通过 `Spawner.SpawnAsync / SpawnSync` + `SpawnHandle` 获取的对象，不需要也不应该再额外手动 `SetActive(true)`，因为系统已经在孵化完成时自动激活了实例。**

### 三、在什么情况下需要你自己调用 SetActive？

在正常的对象池使用流程中，推荐遵循以下原则：

- **不需要自己 SetActive(true) 的情况**
  - 通过 `Spawner.SpawnAsync/SpawnSync` 拿到 `SpawnHandle`，并在：
    - `yield return handle`（异步）
    - 或 `SpawnSync` 返回（同步）
  - 之后访问 `handle.GameObj`，此时对象已经：
    - 绑定到你指定的父节点（若有）
    - 设置到你指定的位置和旋转（若有）
    - 已经 `SetActive(true)`，可直接参与游戏逻辑。

- **不要绕过对象池自行激活缓存对象**
  - 对象池内部缓存的对象在被回收到池中时，会被：
    - `SetActive(false)`
    - 重设父节点到池根 `_root`
    - 位置重置为 `Vector3.zero`，旋转重置为 `Quaternion.identity`
  - 这类对象仅应通过对象池的 `Spawn` 流程再次启用，不建议直接获取并手动 `SetActive(true)`，否则会绕开池的生命周期管理。

- **可能需要自己 SetActive 的特殊情况**
  - 如果你在自己的业务逻辑中，对 `handle.GameObj` 主动调用了 `SetActive(false)`，而且：
    - 并没有通过 `handle.Restore()` 把对象归还给池，
    - 只是想暂时隐藏它，然后在稍后用同一个引用再次显示，
  - 那么在这类**自管理的生命周期**下，重新 `SetActive(true)` 是你的业务逻辑行为，不属于对象池托管范围。

### 四、常见使用示例（与 SetActive 的关系）

#### 1. 标准对象池使用（推荐）

- 流程：
  1. 初始化：`UniPooling.Initalize();`
  2. 创建 `Spawner`：`var spawner = UniPooling.CreateSpawner("DefaultPackage");`
  3. 创建对象池：`var op = spawner.CreateGameObjectPoolAsync("Cube.prefab"); yield return op;`
  4. 孵化对象：`SpawnHandle handle = spawner.SpawnAsync("Cube.prefab"); yield return handle;`
  5. 使用 `handle.GameObj`（已激活）
  6. 用完后：
     - 若希望复用：`handle.Restore();`（对象会自动隐藏并回到池中）
     - 若不再需要：`handle.Discard();`（对象被销毁，不再缓存）

- 与 SetActive 的关系：
  - 你不主动调用 `SetActive`，所有激活/隐藏由对象池内部控制。

#### 2. 自己控制显示/隐藏但仍依赖池管理（不推荐混用）

- 例如你在拿到对象后写：
  - `handle.GameObj.SetActive(false);`
  - 稍后又写：
  - `handle.GameObj.SetActive(true);`
- 如果最终仍是通过 `handle.Restore()` 把对象交还给池，池内部仍然会在回收时统一处理一次 `SetActive(false)` 和变换重置。
- 这类做法容易与池的“缓存/孵化语义”混淆，**不推荐**在不必要时手动频繁切换激活状态。

### 五、小结

- **缓存阶段**：对象池在创建初始对象时会自动执行 `SetActive(false)`，确保缓存对象不出现在场景中。
- **孵化阶段**：通过 `SpawnHandle` 的状态机，在孵化完成时自动执行 `SetActive(true)`，同时设置父节点、位置与旋转。
- **正常使用时**：
  - 调用 `Spawner.SpawnAsync/SpawnSync` 后拿到的 `handle.GameObj` 已经处于激活状态。
  - 一般情况下，不需要也不应该再额外手动 `SetActive(true)`。
- **仅当你有自定义的生命周期管理需求时**（例如在不归还对象给池的前提下，临时隐藏/显示），才需要自行调用 `SetActive`，这部分行为属于你的业务代码，不由对象池负责。


### 六、与 Restore / Discard 及内部销毁逻辑的更细节关系

> 本节把 `SetActive` 行为放回到完整的恢复 / 丢弃代码路径中，从源码角度说明每一步会发生什么。

#### 1. `SpawnHandle.Restore()` / `SpawnHandle.Discard()` 调用路径

- `SpawnHandle.Restore()` 关键步骤：
  - 若 `_operation != null`：
    1. `ClearCompletedCallback()`：移除 `GameAsyncOperation` 上注册的完成回调，避免重复触发。
    2. `CancelHandle()`：若句柄当前 `IsDone == false`，则：
       - `_steps = Done;`
       - `Status = Failed;`
       - `Error = "User cancelled !";`
    3. `_pool.Restore(_operation)`：把当前实例的 `InstantiateOperation` 交回对象池。
    4. `_operation = null;`：该 `SpawnHandle` 不再持有有效实例。

- `SpawnHandle.Discard()` 与之类似：
  - 只是第 3 步调用的是 `_pool.Discard(_operation)`，从而彻底销毁实例而不再放入缓存。

> 这里的 `CancelHandle()` 只影响 `SpawnHandle` 自身的异步状态（用于标记“被用户取消”），**不会改变已经实例化的 GameObject 的激活状态**，真正的激活/隐藏仍由对象池内部控制。

#### 2. `GameObjectPool.Restore` 中的 SetActive 行为（源码视角）

在对象池内部，`Restore` 的关键逻辑包含了是否重新进入缓存队列以及如何处理激活状态：

```csharp
// 简化版逻辑，保持与源码一致的判断顺序
public void Restore(InstantiateOperation operation)
{
    if (IsDestroyed())
    {
        DestroyInstantiateOperation(operation);
        return;
    }

    SpawnCount--;
    if (SpawnCount <= 0)
        _lastRestoreRealTime = Time.realtimeSinceStartup;

    // 如果外部逻辑销毁了游戏对象
    if (operation.Status == EOperationStatus.Succeed)
    {
        if (operation.Result == null)
            return;
    }

    // 如果缓存池还未满员
    if (_cacheOperations.Count < _maxCapacity)
    {
        SetRestoreGameObject(operation.Result);  // ★ 这里会 SetActive(false)
        _cacheOperations.Enqueue(operation);
    }
    else
    {
        DestroyInstantiateOperation(operation);
    }
}
```

- 当决定把对象重新放回缓存队列时，会调用 `SetRestoreGameObject(gameObj)`：
  - `gameObj.SetActive(false);`
  - `gameObj.transform.SetParent(_root.transform);`
  - `gameObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);`
- 这保证了：**所有回收到池中的对象都处于“隐藏 + 归位”的统一状态**，方便后续再次 `Spawn` 时由 `SpawnHandle` 重新设置父节点、位置和激活。

#### 3. `GameObjectPool.Discard` 与 SetActive 的关系

```csharp
public void Discard(InstantiateOperation operation)
{
    if (IsDestroyed())
    {
        DestroyInstantiateOperation(operation);
        return;
    }

    SpawnCount--;
    if (SpawnCount <= 0)
        _lastRestoreRealTime = Time.realtimeSinceStartup;

    DestroyInstantiateOperation(operation);
}
```

- 与 `Restore` 不同，`Discard` 不会再调用 `SetRestoreGameObject`，也不会进入 `_cacheOperations`。
- `DestroyInstantiateOperation` 内部：
  - `operation.Cancel();`
  - 若 `operation.Result != null`，`GameObject.Destroy(operation.Result);`
- 因此：
  - 通过 `Discard()` 走的是**彻底销毁路径**，最终 GameObject 会被销毁，而不是被隐藏或复用。

#### 4. 自动销毁（`CanAutoDestroy`）时的 SetActive 状态

- 当某个 `GameObjectPool` 满足自动销毁条件（`CanAutoDestroy()` 返回 true）时：
  - `Spawner.Update()` 会将其加入 `_removeList`，随后调用 `pool.DestroyPool()`。
- `DestroyPool()` 中：
  - `Handle.Release(); Handle = null;`
  - `GameObject.Destroy(_root);`
  - `_cacheOperations.Clear();`
  - `SpawnCount = 0;`
- 在这种情况下：
  - 所有缓存中的实例（它们本来就已经是 `SetActive(false)` 且挂在 `_root` 下）会随着 `_root` 销毁一并销毁；
  - 不再存在“隐藏等待复用”的对象。

> 总结来说：**SetActive(false)** 只在“缓存中”且“未被销毁”的对象上起作用；当整个池被销毁时，这些对象也会随着池一起销毁，不会留在场景中。



