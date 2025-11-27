## UniWebRequest 架构说明（Unity 2022.3.61f1c1）

### 总体设计

- **核心基类 `WebRequestBase`**
  - 封装了一个 `UnityWebRequest` 实例以及对应的 `UnityWebRequestAsyncOperation`。
  - 统一对外暴露：`URL`、`Status`（`EReqeustStatus`）、`ResponseCode`、`RequestError`、上传/下载进度与字节数等。
  - 实现 `IEnumerator` 接口，因此可以直接在协程里写：`yield return request;` 来等待请求结束。
  - 提供 `Completed` 事件：`event Action<WebRequestBase> Completed`，用于在协程外接收回调。

- **状态枚举 `EReqeustStatus`**
  - `None`：尚未开始。
  - `InProgress`：正在请求。
  - `Succeed`：请求成功。
  - `ConnectionError`：网络连接层面出错（如无法连上服务器）。
  - `ProtocolError`：与服务器通信成功，但 HTTP 协议层返回错误（如 4xx / 5xx），可结合 `ResponseCode` 进一步判断。
  - `DataProcessingError`：接收数据成功，但在解析 / 处理时出错（如格式错误、内容损坏）。

- **具体请求器（Requester）**
  - `WebRequestGet`：通用 GET 文本/二进制请求（使用 `DownloadHandlerBuffer`）。
  - `WebRequestPost`：POST 文本或表单数据（内部统一补充 `DownloadHandlerBuffer` 用于读取响应）。
  - `WebRequestFile`：下载文件到磁盘（使用 `DownloadHandlerFile`，避免大文件常驻内存）。
  - `WebRequestTexture`：下载纹理（使用 `DownloadHandlerTexture` 并封装到 `RequestAsset` 中）。
  - `WebRequestAudio`：下载音频（使用 `DownloadHandlerAudioClip` 并封装到 `RequestAsset` 中）。
  - `WebRequestHeader`：仅请求 Header 信息（使用 `UnityWebRequest.Head`）。

- **资源包装 `RequestAsset`**
  - 对下载到的 `UnityEngine.Object` 做统一包装，记录 `AssetURL`。
  - 提供：`GetTexture()` / `GetSprite()` / `GetAudioClip()` 等强类型访问接口。
  - 提供 `UnloadAsset()` 主动卸载资源，避免资源长时间占用内存。

---

### 协程与异步用法说明

#### 1. `yield return request` 的工作方式

`WebRequestBase` 实现了 `IEnumerator`：

```csharp
bool IEnumerator.MoveNext()
{
    return !IsDone();
}
```

- Unity 在协程中遇到 `yield return request;` 时，会把 `request` 当作子协程，**每帧调用一次 `MoveNext()`**。
- `IsDone()` 内部基于 `UnityWebRequestAsyncOperation.isDone` 判断：
  - 返回 `false` → 本帧还没完成，请求继续进行，协程保持挂起。
  - 返回 `true`  → 请求已结束（无论成功失败），协程从 `yield return` 这一行继续往下执行。
- 如果没有调用 `SendRequest()` 导致内部 `_operation` 一直为 `null`，`IsDone()` 会一直返回 `false`，协程就会永远卡在 `yield return request;`；这属于**使用方式错误**，而非架构 bug。

推荐调用顺序：

```csharp
var request = new WebRequestGet(url);
request.SendRequest(timeout: 10);
yield return request;  // 等待请求完成
```

#### 2. `Completed` 事件

- 属性定义：

```csharp
public event System.Action<WebRequestBase> Completed
{
    add
    {
        if (IsDone())
            value.Invoke(this);
        else
            _callback += value;
    }
    remove
    {
        _callback -= value;
    }
}
```

- 请求内部在 `SendRequest()` 时会挂接：

```csharp
_operation = _webRequest.SendWebRequest();
_operation.completed += CompleteInternal;
```

- 当 `UnityWebRequest` 完成时，`CompleteInternal` 会被调用，从而触发 `_callback`：

```csharp
protected void CompleteInternal(AsyncOperation op)
{
    _callback?.Invoke(this);
}
```

使用方式示例：

```csharp
var request = new WebRequestGet(url);
request.Completed += OnCompleted;
request.SendRequest();
```

这样即使不在协程里 `yield return`，也可以在 `OnCompleted` 回调中读取结果。

---

### 请求生命周期与释放策略

统一的推荐流程如下：

1. `new` 对应的请求器（例如 `new WebRequestGet(url)`）。
2. 调用 `SendRequest(...)` 开始请求。
3. 通过协程 `yield return request;` 或 `Completed` 事件等待结束。
4. 检查 `Status` / `ResponseCode` / `RequestError` 判定成功或失败。
5. 读取结果（文本 / 字节 / Texture / AudioClip / 文件等）。
6. **调用 `request.Dispose()` 释放底层 `UnityWebRequest` 与相关 Handler**。
7. 如使用了 `RequestAsset` 并且资源不再需要，可调用 `UnloadAsset()` 卸载。

注意：

- 所有请求器内部都把 `disposeDownloadHandlerOnDispose` 设为 `true`，因此只需要调用一次 `WebRequestBase.Dispose()` 即可完成内存清理。
- 对于大文件 / 大纹理 / 大音频，务必在用完后及时卸载 `RequestAsset`，否则会占用大量内存。


