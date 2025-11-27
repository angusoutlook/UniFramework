## UniNetwork 模块说明（Unity 2022.3.61f1c1）

> 本文档完全基于 `UniNetwork/Runtime` 目录下的真实代码进行说明，不对源码做任何简化或逻辑改写，仅做结构化解读和使用指引。

---

### 1. 文件与类结构总览

- **入口与驱动**
  - `UniNetwork/Runtime/UniNetwork.cs`：静态网络系统入口，负责初始化、更新、统一管理多个 `TcpClient`。
  - `UniNetwork/Runtime/UniNetworkDriver.cs`：`MonoBehaviour` 驱动，每帧调用 `UniNetwork.Update()`。

- **TCP 实现**
  - `UniNetwork/Runtime/TCP/TcpClient.cs`：对外暴露的 TCP 客户端类（实现 `IDisposable`），组合 `TcpChannel`。
  - `UniNetwork/Runtime/TCP/TcpChannel.cs`：内部 TCP 通道实现（`internal class`，实现 `IDisposable`），封装 `Socket` + `SocketAsyncEventArgs` + 编解码流程。

- **协议与包结构**
  - `UniNetwork/Runtime/Package/INetPackage.cs`：网络包接口与错误回调委托声明。
  - `UniNetwork/Runtime/Package/INetPackageEncoder.cs`：编码器接口。
  - `UniNetwork/Runtime/Package/INetPackageDecoder.cs`：解码器接口。
  - `UniNetwork/Runtime/Package/DefaultNetPackage.cs`：默认包结构，`MsgID + BodyBytes`。
  - `UniNetwork/Runtime/Package/DefaultNetPackageEncoder.cs`：默认包编码器。
  - `UniNetwork/Runtime/Package/DefaultNetPackageDecoder.cs`：默认包解码器。

- **工具与基础设施**
  - `UniNetwork/Runtime/RingBuffer.cs`：环形缓冲区实现及一整套基础类型读写 API。
  - `UniNetwork/Runtime/ThreadSyncContext.cs`：线程同步上下文，将其它线程回调同步到 Unity 主线程。
  - `UniNetwork/Runtime/UniLogger.cs`：内部日志封装，基于 `UnityEngine.Debug`。

---

### 2. UniNetwork：网络系统入口

源码片段：

```csharp
public static class UniNetwork
{
    private static bool _isInitialize = false;
    private static GameObject _driver = null;
    private readonly static List<TcpClient> _tcpClients = new List<TcpClient>();

    public static void Initalize()
    {
        if (_isInitialize)
            throw new Exception($"{nameof(UniNetwork)} is initialized !");

        if (_isInitialize == false)
        {
            _isInitialize = true;
            _driver = new GameObject($"[{nameof(UniNetwork)}]");
            _driver.AddComponent<UniNetworkDriver>();
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            UniLogger.Log($"{nameof(UniNetwork)} initalize !");
        }
    }
}
```

- **初始化（`Initalize`）**
  - 通过 `_isInitialize` 防止重复初始化，如果重复调用会直接抛出异常。
  - 创建名为 `"[UniNetwork]"` 的 `GameObject`，添加 `UniNetworkDriver` 组件。
  - `DontDestroyOnLoad(_driver)`：确保跨场景常驻。

- **销毁（`Destroy`）**

```csharp
public static void Destroy()
{
    if (_isInitialize)
    {
        foreach (var client in _tcpClients)
        {
            client.Destroy();
        }
        _tcpClients.Clear();

        _isInitialize = false;
        if (_driver != null)
            GameObject.Destroy(_driver);
        UniLogger.Log($"{nameof(UniNetwork)} destroy all !");
    }
}
```

- **要点说明**：
  - 对列表中的每个 `TcpClient` 调用 `client.Destroy()`，而非直接 `Dispose()`；`Destroy` 内部又会调用 `Dispose()`，属于一个显式生命周期接口。
  - 销毁完所有客户端后，清空 `_tcpClients`、销毁驱动 `GameObject`，并把 `_isInitialize` 置为 `false`。

- **更新（`Update`）**

```csharp
internal static void Update()
{
    if (_isInitialize)
    {
        foreach (var client in _tcpClients)
        {
            client.Update();
        }
    }
}
```

- **要点说明**：
  - 这是一个 `internal` 方法，只会被同程序集的 `UniNetworkDriver` 调用。
  - 每帧依次调用所有 `TcpClient.Update()`，从而驱动线程同步和底层 `TcpChannel` 的收发逻辑。

- **TCP 客户端管理**

```csharp
public static TcpClient CreateTcpClient(int packageBodyMaxSize, INetPackageEncoder encoder, INetPackageDecoder decoder)
{
    if (_isInitialize == false)
        throw new Exception($"{nameof(UniNetwork)} not initialized !");

    var client = new TcpClient(packageBodyMaxSize, encoder, decoder);
    _tcpClients.Add(client);
    return client;
}

public static void DestroyTcpClient(TcpClient client)
{
    if (client == null)
        return;

    client.Dispose();
    _tcpClients.Remove(client);
}
```

- **设计细节**：
  - 必须在 `Initalize()` 成功之后才能创建 `TcpClient`，否则会抛出异常。
  - `DestroyTcpClient` 对单个客户端执行 `Dispose()` 并从 `_tcpClients` 列表中移除，和 `Destroy()` 里统一销毁所有客户端的逻辑相互独立。

---

### 3. UniNetworkDriver：驱动组件

源码非常精简：

```csharp
internal class UniNetworkDriver : MonoBehaviour
{
    void Update()
    {
        UniNetwork.Update();
    }
}
```

- **设计意图**：通过一个简单的 `MonoBehaviour` 把静态类 `UniNetwork` 与 Unity 的帧更新生命周期关联起来。
- **执行线程**：`Update()` 在 Unity 主线程执行，从而保证 `UniNetwork.Update()` 与其内部的 `TcpClient.Update()` 也在主线程执行。

---

### 4. TcpClient：对外 TCP 客户端

定义：

```csharp
public class TcpClient : IDisposable
{
    private class UserToken
    {
        public System.Action<SocketError> Callback;
    }

    private TcpChannel _channel;
    private readonly int _packageBodyMaxSize;
    private readonly INetPackageEncoder _encoder;
    private readonly INetPackageDecoder _decoder;
    private readonly ThreadSyncContext _syncContext;
    ...
}
```

- **关键成员**
  - `_channel`：内部真正进行 Socket 通信的 `TcpChannel`。
  - `_packageBodyMaxSize`：单个包体允许的最大长度，用于编码和解码时的安全校验。
  - `_encoder` / `_decoder`：构造时注入的编解码器实例，实现自定义协议的基础。
  - `_syncContext`：`ThreadSyncContext` 实例，用于把 Socket 的异步回调同步回主线程。
  - 嵌套类 `UserToken`：简单的结构，挂在 `SocketAsyncEventArgs.UserToken` 上携带连接回调。

- **构造与初始化**

```csharp
internal TcpClient(int packageBodyMaxSize, INetPackageEncoder encoder, INetPackageDecoder decoder)
{
    _packageBodyMaxSize = packageBodyMaxSize;
    _encoder = encoder;
    _decoder = decoder;
    _syncContext = new ThreadSyncContext();
}
```

> 注意：构造函数是 `internal`，只能通过 `UniNetwork.CreateTcpClient(...)` 创建，外部无法直接 `new TcpClient(...)`。

- **Update：驱动同步与通道**

```csharp
internal void Update()
{
    _syncContext.Update();

    if (_channel != null)
        _channel.Update();
}
```

1. `SyncContext.Update()`：执行其它线程里通过 `Post` 投递过来的所有回调（如 IOCP 的连接、收发完成事件），保证这些逻辑在主线程内执行。
2. 如果 `_channel` 已创建，则调用 `_channel.Update()`，驱动一次发送与接收过程。

- **生命周期相关**

```csharp
internal void Destroy()
{
    Dispose();
}

public void Dispose()
{
    if (_channel != null)
    {
        _channel.Dispose();
        _channel = null;
    }
}
```

- `Destroy()` 仅仅调用 `Dispose()`，是给 `UniNetwork.Destroy()` 统一调用的内部接口。
- `Dispose()` 会安全关闭并释放 `TcpChannel`，内部会关闭 `Socket` 与 `SocketAsyncEventArgs`（详见后文 `TcpChannel.Dispose`）。

- **发送与收包接口**

```csharp
public void SendPackage(INetPackage package)
{
    if (_channel != null)
        _channel.SendPackage(package);
}

public INetPackage PickPackage()
{
    if (_channel == null)
        return null;

    return _channel.PickPackage();
}

public bool IsConnected()
{
    if (_channel == null)
        return false;

    return _channel.IsConnected();
}
```

- **说明**：
  - `SendPackage`：简单转发给 `_channel.SendPackage`，如果通道尚未创建（未连接/已断开），直接忽略。
  - `PickPackage`：从内部接收队列取出一个已解码的 `INetPackage`，无包时返回 `null`。
  - `IsConnected`：调用 `TcpChannel.IsConnected()`，本质上是看底层 `Socket.Connected` 状态。

- **异步连接流程**

```csharp
public void ConnectAsync(IPEndPoint remote, System.Action<SocketError> callback)
{
    UserToken token = new UserToken()
    {
        Callback = callback,
    };

    SocketAsyncEventArgs args = new SocketAsyncEventArgs();
    args.RemoteEndPoint = remote;
    args.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
    args.UserToken = token;

    Socket clientSock = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    bool willRaiseEvent = clientSock.ConnectAsync(args);
    if (!willRaiseEvent)
    {
        ProcessConnected(args);
    }
}
```

- 通过 `SocketAsyncEventArgs` + `Socket.ConnectAsync` 建立异步连接：
  - 把外部传入的 `callback` 封装到 `UserToken` 中，挂在 `args.UserToken`。
  - 订阅 `args.Completed` 事件，回调到 `AcceptEventArg_Completed`。
  - 如果 `ConnectAsync` 立刻完成，直接本地调用 `ProcessConnected(args)`。

连接完成事件：

```csharp
private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
{
    switch (e.LastOperation)
    {
        case SocketAsyncOperation.Connect:
            _syncContext.Post(ProcessConnected, e);
            break;
        default:
            throw new ArgumentException("The last operation completed on the socket was not a connect");
    }
}
```

- **要点**：
  - 无论 `ConnectAsync` 是在 IOCP 线程还是当前线程完成，最终都会通过 `_syncContext.Post(ProcessConnected, e)` 把 `ProcessConnected` 投递到主线程执行。

真正处理连接结果：

```csharp
private void ProcessConnected(object obj)
{
    SocketAsyncEventArgs e = obj as SocketAsyncEventArgs;
    UserToken token = (UserToken)e.UserToken;
    if (e.SocketError == SocketError.Success)
    {
        if (_channel != null)
            throw new Exception("TcpClient channel is created.");

        // 创建频道
        _channel = new TcpChannel();
        _channel.InitChannel(_syncContext, e.ConnectSocket, _packageBodyMaxSize, _encoder, _decoder);
    }
    else
    {
        UniLogger.Error($"Network connecte error : {e.SocketError}");
    }

    // 回调函数
    if (token.Callback != null)
        token.Callback.Invoke(e.SocketError);
}
```

- **设计细节**：
  - 成功连接后：
    - 确保 `_channel` 尚未创建，否则抛出异常。
    - 使用 `e.ConnectSocket`（已完成连接的 `Socket`）实例化一个新的 `TcpChannel`。
    - 调用 `_channel.InitChannel(_syncContext, ..., _packageBodyMaxSize, _encoder, _decoder)` 完成通道初始化。
  - 无论成功或失败，都会调用外部传入的回调 `token.Callback`，并传入 `SocketError`。
  - 日志错误信息使用 `UniLogger.Error` 输出。

---

### 5. TcpChannel：底层 Socket 通道

类定义与主要字段：

```csharp
internal class TcpChannel : IDisposable
{
    private readonly SocketAsyncEventArgs _receiveArgs = new SocketAsyncEventArgs();
    private readonly SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();

    private readonly Queue<INetPackage> _sendQueue = new Queue<INetPackage>(10000);
    private readonly Queue<INetPackage> _receiveQueue = new Queue<INetPackage>(10000);
    private readonly List<INetPackage> _decodeTempList = new List<INetPackage>(100);

    private byte[] _receiveBuffer;
    private RingBuffer _encodeBuffer;
    private RingBuffer _decodeBuffer;
    private int _packageBodyMaxSize;
    private INetPackageEncoder _packageEncoder;
    private INetPackageDecoder _packageDecoder;
    private bool _isSending = false;
    private bool _isReceiving = false;

    // 通信 Socket
    private Socket _socket;

    // 同步上下文
    private ThreadSyncContext _context;
}
```

- **发送队列 `_sendQueue`**：缓存待发送的 `INetPackage`，通过 `SendPackage` 入队。
- **接收队列 `_receiveQueue`**：存放解码完成、等待上层处理的 `INetPackage`，通过 `PickPackage` 出队。
- **临时列表 `_decodeTempList`**：单次解码过程中存放多个解出来的包，之后统一入 `_receiveQueue`。
- **缓冲区**
  - `_encodeBuffer`：`RingBuffer`，发送前批量写入编码后的包体。
  - `_decodeBuffer`：`RingBuffer`，接收时把原始字节写入其中，再从中循环解码。
  - `_receiveBuffer`：原始接收缓冲，长度为「包体最大长度 + 包头长度」。

#### 5.1 初始化通道：`InitChannel`

```csharp
internal void InitChannel(ThreadSyncContext context, Socket socket, int packageBodyMaxSize, INetPackageEncoder encoder, INetPackageDecoder decoder)
{
    if (packageBodyMaxSize <= 0)
        throw new System.ArgumentException($"PackageMaxSize is invalid : {packageBodyMaxSize}");

    _context = context;
    _socket = socket;
    _socket.NoDelay = true;

    // 创建编码解码器
    _packageBodyMaxSize = packageBodyMaxSize;
    _packageEncoder = encoder;
    _packageEncoder.RegisterHandleErrorCallback(HandleError);
    _packageDecoder = decoder;
    _packageDecoder.RegisterHandleErrorCallback(HandleError);

    // 创建字节缓冲类（推荐 4 倍最大包体长度）
    int encoderPackageMaxSize = packageBodyMaxSize + _packageEncoder.GetPackageHeaderSize();
    int decoderPakcageMaxSize = packageBodyMaxSize + _packageDecoder.GetPackageHeaderSize();
    _encodeBuffer = new RingBuffer(encoderPackageMaxSize * 4);
    _decodeBuffer = new RingBuffer(decoderPakcageMaxSize * 4);
    _receiveBuffer = new byte[decoderPakcageMaxSize];

    // 创建 IOCP 接收
    _receiveArgs.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
    _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);

    // 创建 IOCP 发送
    _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
    _sendArgs.SetBuffer(_encodeBuffer.GetBuffer(), 0, _encodeBuffer.Capacity);
}
```

- **设计要点**：
  - 对包体最大长度做安全校验，避免异常配置。
  - 为编码器和解码器注册统一的错误处理回调 `HandleError`，所有协议层错误统一在这里汇总。
  - 缓冲区容量采用「最大单包长度 × 4」，平衡内存占用与粘包场景下的吞吐。
  - `_receiveArgs`、`_sendArgs` 的 `Completed` 都指向同一个 `IO_Completed`，再通过 `e.LastOperation` 分发。

#### 5.2 连接状态与销毁

```csharp
public bool IsConnected()
{
    if (_socket == null)
        return false;
    return _socket.Connected;
}

public void Dispose()
{
    try
    {
        if (_socket != null)
            _socket.Shutdown(SocketShutdown.Both);

        _receiveArgs.Dispose();
        _sendArgs.Dispose();

        _sendQueue.Clear();
        _receiveQueue.Clear();
        _decodeTempList.Clear();

        _encodeBuffer.Clear();
        _decodeBuffer.Clear();

        _isSending = false;
        _isReceiving = false;
    }
    catch (Exception)
    {
        // 客户端进程已经关闭时可能抛异常，这里直接吞掉。
    }
    finally
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
        }
    }
}
```

- **注意点**：
  - 调用 `Shutdown(SocketShutdown.Both)` 尝试优雅关闭连接。
  - 释放 `SocketAsyncEventArgs`，清空所有队列与缓冲，并重置 `_isSending`、`_isReceiving` 标记。
  - 任何异常都被吞掉（注释说明部分异常来源于客户端进程已关闭）。

#### 5.3 Update 循环：发送与接收

```csharp
public void Update()
{
    if (_socket == null || _socket.Connected == false)
        return;

    // 接收数据
    UpdateReceiving();

    // 发送数据
    UpdateSending();
}
```

##### 接收流程：`UpdateReceiving` → `ReceiveAsync` → `ProcessReceive`

```csharp
private void UpdateReceiving()
{
    if (_isReceiving == false)
    {
        _isReceiving = true;

        bool willRaiseEvent = _socket.ReceiveAsync(_receiveArgs);
        if (!willRaiseEvent)
        {
            ProcessReceive(_receiveArgs);
        }
    }
}
```

- `_isReceiving` 保证只发起一次初始接收请求，后续的继续接收都是在 `ProcessReceive` 中递归发起。

`IO_Completed` 中的分发：

```csharp
private void IO_Completed(object sender, SocketAsyncEventArgs e)
{
    switch (e.LastOperation)
    {
        case SocketAsyncOperation.Receive:
            _context.Post(ProcessReceive, e);
            break;
        case SocketAsyncOperation.Send:
            _context.Post(ProcessSend, e);
            break;
        default:
            throw new ArgumentException("The last operation completed on the socket was not a receive or send");
    }
}
```

- **关键点**：收发完成的回调都通过 `_context.Post` 投递到主线程，因此后续所有包解码、入队等逻辑都在主线程执行。

`ProcessReceive` 解包逻辑：

```csharp
private void ProcessReceive(object obj)
{
    if (_socket == null)
        return;

    SocketAsyncEventArgs e = obj as SocketAsyncEventArgs;

    if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
    {
        if (_decodeBuffer.IsWriteable(e.BytesTransferred) == false)
        {
            HandleError(true, "The channel fatal error");
            return;
        }

        // 写入解码缓冲
        _decodeBuffer.WriteBytes(e.Buffer, 0, e.BytesTransferred);

        // 解码
        _decodeTempList.Clear();
        _packageDecoder.Decode(_packageBodyMaxSize, _decodeBuffer, _decodeTempList);
        lock (_receiveQueue)
        {
            for (int i = 0; i < _decodeTempList.Count; i++)
            {
                _receiveQueue.Enqueue(_decodeTempList[i]);
            }
        }

        // 发起下一次接收
        e.SetBuffer(0, _receiveBuffer.Length);
        bool willRaiseEvent = _socket.ReceiveAsync(e);
        if (!willRaiseEvent)
        {
            ProcessReceive(e);
        }
    }
    else
    {
        HandleError(true, $"ProcessReceive error : {e.SocketError}");
    }
}
```

- **粘包/半包处理** 完全由 `_packageDecoder.Decode` 完成，`TcpChannel` 只负责把字节按顺序写入 `_decodeBuffer`，并在成功解出若干包后将其入队 `_receiveQueue`。
- 如果 `ReceiveAsync` 返回错误或 `BytesTransferred <= 0`，通过 `HandleError(true, ...)` 打印日志并销毁通道。

##### 发送流程：`UpdateSending` → `SendAsync` → `ProcessSend`

```csharp
private void UpdateSending()
{
    if (_isSending == false && _sendQueue.Count > 0)
    {
        _isSending = true;

        // 清空编码缓冲
        _encodeBuffer.Clear();

        // 合并多个包一起发送
        while (_sendQueue.Count > 0)
        {
            int encoderPackageMaxSize = _packageBodyMaxSize + _packageEncoder.GetPackageHeaderSize();
            if (_encodeBuffer.WriteableBytes < encoderPackageMaxSize)
                break;

            INetPackage package = _sendQueue.Dequeue();
            _packageEncoder.Encode(_packageBodyMaxSize, _encodeBuffer, package);
        }

        _sendArgs.SetBuffer(0, _encodeBuffer.ReadableBytes);
        bool willRaiseEvent = _socket.SendAsync(_sendArgs);
        if (!willRaiseEvent)
        {
            ProcessSend(_sendArgs);
        }
    }
}
```

- 每次发送前都会先清空 `_encodeBuffer`，然后不断从 `_sendQueue` 出队编码，直到剩余可写空间不足以容纳一个「最大包」为止。
- 这样可以在一次 `SendAsync` 中发送多个逻辑包，减少系统调用次数。

发送完成：

```csharp
private void ProcessSend(object obj)
{
    if (_socket == null)
        return;

    SocketAsyncEventArgs e = obj as SocketAsyncEventArgs;
    if (e.SocketError == SocketError.Success)
    {
        _isSending = false;
    }
    else
    {
        HandleError(true, $"ProcessSend error : {e.SocketError}");
    }
}
```

- 成功时仅将 `_isSending` 复位，允许下一帧继续发送队列中的其它包。
- 失败时同样通过 `HandleError` 统一处理。

##### 发送/接收队列接口

```csharp
public void SendPackage(INetPackage package)
{
    lock (_sendQueue)
    {
        _sendQueue.Enqueue(package);
    }
}

public INetPackage PickPackage()
{
    INetPackage package = null;
    lock (_receiveQueue)
    {
        if (_receiveQueue.Count > 0)
            package = _receiveQueue.Dequeue();
    }
    return package;
}
```

- **线程安全**：`_sendQueue` / `_receiveQueue` 都用 `lock` 做同步，保证上层在主线程访问时的安全性，即使将来扩展到多线程调用也具有基本防护。

##### 统一错误处理

```csharp
private void HandleError(bool isDispose, string error)
{
    UniLogger.Error(error);
    if (isDispose) Dispose();
}
```

- 所有错误都会统一通过 `UniLogger.Error` 打印。
- 根据 `isDispose` 决定是否立即 `Dispose` 通道，完全与编码器、解码器的具体实现解耦。

---

### 6. RingBuffer：环形缓冲区与序列化工具

类注释中将其称为「环形缓冲区」，实际实现为：

- 一个 `byte[] _buffer` 存储数据。
- `_readerIndex` / `_writerIndex` 两个指针维护「已读/已写」区域。
- 通过 `DiscardReadBytes()` 把未读数据前移到数组起始，实现逻辑上的“环形复用”。

#### 6.1 基本属性与指针

- **容量与缓冲区**
  - `public int Capacity => _buffer.Length;`
  - `public byte[] GetBuffer()`：直接返回内部数组引用（用于 `SocketAsyncEventArgs.SetBuffer`）。

- **读相关**
  - `public int ReaderIndex { get; }`：当前读指针。
  - `public int ReadableBytes => _writerIndex - _readerIndex;`：当前可读字节数。
  - `public bool IsReadable(int size = 1)`：是否有足够数据可读。
  - `MarkReaderIndex()` 与 `ResetReaderIndex()`：配合解包逻辑，在读取不完整包时回滚到标记位置（默认解码器中有使用）。

- **写相关**
  - `public int WriterIndex { get; }`：当前写指针。
  - `public int WriteableBytes => Capacity - _writerIndex;`：当前可写字节数。
  - `public bool IsWriteable(int size = 1)`：是否有足够空间写入。
  - `MarkWriterIndex()` 与 `ResetWriterIndex()`：为更复杂的写入逻辑提供回滚能力（当前网络代码未使用，但接口完整）。

#### 6.2 清理与压缩

```csharp
public void Clear()
{
    _readerIndex = 0;
    _writerIndex = 0;
    _markedReaderIndex = 0;
    _markedWriterIndex = 0;
}

public void DiscardReadBytes()
{
    if (_readerIndex == 0)
        return;

    if (_readerIndex == _writerIndex)
    {
        _readerIndex = 0;
        _writerIndex = 0;
    }
    else
    {
        for (int i = 0, j = _readerIndex, length = _writerIndex - _readerIndex; i < length; i++, j++)
        {
            _buffer[i] = _buffer[j];
        }
        _writerIndex -= _readerIndex;
        _readerIndex = 0;
    }
}
```

- **设计目的**：在不断读取的过程中，周期性地把未读数据前移，释放尾部空间，避免数组无限增长。
- 默认解码器在完成一轮解码后会调用 `DiscardReadBytes()`。

#### 6.3 读取操作（部分）

读取前有 DEBUG 检查：

```csharp
[Conditional("DEBUG")]
private void CheckReaderIndex(int length)
{
    if (_readerIndex + length > _writerIndex)
    {
        throw new IndexOutOfRangeException();
    }
}
```

- 常用读取方法：
  - `ReadBytes(int count)`：返回一段新分配的 `byte[]`。
  - 基本数值类型：`ReadBool`、`ReadByte`、`ReadShort`、`ReadUShort`、`ReadInt`、`ReadUInt`、`ReadLong`、`ReadULong`、`ReadFloat`、`ReadDouble`。
  - 字符串：`ReadUTF()`，内部先读 `ushort` 长度，再读取对应字节，并忽略末尾 `\0` 结束符。
  - 容器类型：`ReadListInt`、`ReadListLong`、`ReadListFloat`、`ReadListDouble`、`ReadListUTF`。
  - 向量类型：`ReadVector2`、`ReadVector3`、`ReadVector4`（按顺序读多个 `float`）。

#### 6.4 写入操作（部分）

写入同样有 DEBUG 检查：

```csharp
[Conditional("DEBUG")]
private void CheckWriterIndex(int length)
{
    if (_writerIndex + length > Capacity)
    {
        throw new IndexOutOfRangeException();
    }
}
```

- 常用写入方法：
  - `WriteBytes(byte[] data, int offset, int count)`。
  - 基本数值类型：`WriteBool`、`WriteByte`、`WriteShort`、`WriteUShort`、`WriteInt`、`WriteUInt`、`WriteLong`、`WriteULong`、`WriteFloat`、`WriteDouble`。
  - 字符串：`WriteUTF(string value)`：
    - 使用 UTF8 编码字符串。
    - 写入长度 `ushort`（+1 表示末尾多写一个 `\0`）。
    - 写入字符串字节与 `\0` 结束符。
  - 容器类型：`WriteListInt`、`WriteListLong`、`WriteListFloat`、`WriteListDouble`、`WriteListUTF`。
  - 向量类型：`WriteVector2`、`WriteVector3`、`WriteVector4`。

#### 6.5 大小端转换工具

```csharp
public static void ReverseOrder(byte[] data, int offset, int length)
{
    if (length <= 1)
        return;

    int end = offset + length - 1;
    int max = offset + length / 2;
    byte temp;
    for (int index = offset; index < max; index++, end--)
    {
        temp = data[end];
        data[end] = data[index];
        data[index] = temp;
    }
}
```

- 静态方法 `ReverseOrder` 可对指定片段进行原地反转，用于大小端转换等场景（当前网络模块未直接使用，但为后续扩展预留能力）。

---

### 7. ThreadSyncContext：线程同步到主线程

代码：

```csharp
/// <summary>
/// 同步其它线程里的回调到主线程里
/// 注意：Unity3D中需要设置Scripting Runtime Version为.NET4.6
/// </summary>
internal sealed class ThreadSyncContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _safeQueue = new ConcurrentQueue<Action>();

    public void Update()
    {
        while (true)
        {
            if (_safeQueue.TryDequeue(out Action action) == false)
                return;
            action.Invoke();
        }
    }

    public override void Post(SendOrPostCallback callback, object state)
    {
        Action action = new Action(() => { callback(state); });
        _safeQueue.Enqueue(action);
    }
}
```

- **用途**：统一将其它线程（如 IOCP 线程）上的回调转移到 Unity 主线程执行。
- **工作原理**：
  - `Post`：接收一个委托与状态，包装成 `Action` 后入 `_safeQueue`。
  - `Update`：在主线程反复从队列中取出并执行所有 `Action`，直到队列为空。
- **与 TcpClient / TcpChannel 的关系**：
  - `TcpClient` 在构造时创建一个 `ThreadSyncContext` 实例。
  - `TcpChannel` 初始化时接收该实例，并在 `IO_Completed` 等回调中使用 `_context.Post(...)` 把逻辑投递回主线程。
  - `TcpClient.Update()` 每帧调用 `_syncContext.Update()`，保证所有回调在 Unity 主线程里依次执行。

---

### 8. 协议层：INetPackage 与默认实现

#### 8.1 INetPackage 与错误回调

```csharp
public delegate void HandleErrorDelegate(bool isDispose, string error);

public interface INetPackage
{
}
```

- `INetPackage` 本身不包含任何字段，仅作为“网络包类型”的标记接口。
- `HandleErrorDelegate` 用于编解码层向通道层报告错误，并传递是否需要销毁通道的标记。

#### 8.2 DefaultNetPackage：默认包结构

```csharp
public class DefaultNetPackage : INetPackage
{
    public int MsgID { set; get; }
    public byte[] BodyBytes { set; get; }
}
```

- **`MsgID`**：业务协议号。
- **`BodyBytes`**：包体原始字节，可存放 JSON / Protobuf / 自定义二进制等内容。

#### 8.3 INetPackageEncoder 接口与默认实现

接口：

```csharp
public interface INetPackageEncoder
{
    int GetPackageHeaderSize();
    void RegisterHandleErrorCallback(HandleErrorDelegate callback);
    void Encode(int packageBodyMaxSize, RingBuffer ringBuffer, INetPackage encodePackage);
}
```

默认实现 `DefaultNetPackageEncoder`：

```csharp
private HandleErrorDelegate _handleErrorCallback;
private const int HeaderMsgIDFiledSize = 4;      // 包头里的协议ID（int）
private const int HeaderMsgLengthFiledSize = 4;  // 包头里的包体长度（int）

public int GetPackageHeaderSize()
{
    return HeaderMsgIDFiledSize + HeaderMsgLengthFiledSize;
}

public void RegisterHandleErrorCallback(HandleErrorDelegate callback)
{
    _handleErrorCallback = callback;
}
```

编码逻辑：

```csharp
public void Encode(int packageBodyMaxSize, RingBuffer ringBuffer, INetPackage encodePackage)
{
    if (encodePackage == null)
    {
        _handleErrorCallback(false, "The encode package object is null");
        return;
    }

    DefaultNetPackage package = (DefaultNetPackage)encodePackage;
    if (package == null)
    {
        _handleErrorCallback(false, $"The encode package object is invalid : {encodePackage.GetType()}");
        return;
    }

    if (package.BodyBytes == null)
    {
        _handleErrorCallback(false, $"The encode package BodyBytes field is null : {encodePackage.GetType()}");
        return;
    }

    byte[] bodyData = package.BodyBytes;

    if (bodyData.Length > packageBodyMaxSize)
    {
        _handleErrorCallback(false, $"The encode package {package.MsgID} body size is larger than {packageBodyMaxSize}");
        return;
    }

    // 写入包头
    ringBuffer.WriteInt(package.MsgID);
    ringBuffer.WriteInt(bodyData.Length);

    // 写入包体
    ringBuffer.WriteBytes(bodyData, 0, bodyData.Length);
}
```

- **包格式明确**：`[4字节 MsgID][4字节 BodyLength][BodyBytes...]`。
- **多层校验**：
  - `encodePackage` 不得为 `null`。
  - 必须成功转换为 `DefaultNetPackage`。
  - `BodyBytes` 不得为 `null`。
  - `BodyBytes.Length` 不得超过 `packageBodyMaxSize`。
- 所有错误都通过 `_handleErrorCallback` 抛给上层（`TcpChannel.HandleError`）。

#### 8.4 INetPackageDecoder 接口与默认实现

接口：

```csharp
public interface INetPackageDecoder
{
    int GetPackageHeaderSize();
    void RegisterHandleErrorCallback(HandleErrorDelegate callback);
    void Decode(int packageBodyMaxSize, RingBuffer ringBuffer, List<INetPackage> outputPackages);
}
```

默认实现 `DefaultNetPackageDecoder`：

```csharp
private HandleErrorDelegate _handleErrorCallback;
private const int HeaderMsgIDFiledSize = 4;             // 协议ID
private const int HeaderMsgBodyLengthFiledSize = 4;     // 包体长度

public int GetPackageHeaderSize()
{
    return HeaderMsgIDFiledSize + HeaderMsgBodyLengthFiledSize;
}

public void RegisterHandleErrorCallback(HandleErrorDelegate callback)
{
    _handleErrorCallback = callback;
}
```

解码逻辑（核心）：

```csharp
public void Decode(int packageBodyMaxSize, RingBuffer ringBuffer, List<INetPackage> outputPackages)
{
    // 循环解包
    while (true)
    {
        // 不够一个包头，退出
        if (ringBuffer.ReadableBytes < GetPackageHeaderSize())
            break;
        ringBuffer.MarkReaderIndex();

        int msgID = ringBuffer.ReadInt();
        int msgBodyLength = ringBuffer.ReadInt();

        // 不够一个完整包体，回滚读指针等待更多数据
        if (ringBuffer.ReadableBytes < msgBodyLength)
        {
            ringBuffer.ResetReaderIndex();
            break;
        }

        DefaultNetPackage package = new DefaultNetPackage();
        package.MsgID = msgID;

        if (msgBodyLength > packageBodyMaxSize)
        {
            _handleErrorCallback(true, $"The decode package {package.MsgID} body size is larger than {packageBodyMaxSize} !");
            break;
        }

        package.BodyBytes = ringBuffer.ReadBytes(msgBodyLength);
        outputPackages.Add(package);
    }

    // 将剩余数据移至起始
    ringBuffer.DiscardReadBytes();
}
```

- **粘包/半包处理**：
  - 利用 `MarkReaderIndex` 和 `ResetReaderIndex`，发现数据不够包体长度时回滚到包头起始位置，等待下次接收。
  - 多个完整包会在一次循环中被解出并加入 `outputPackages`。
- **包长校验**：
  - 包体长度大于 `packageBodyMaxSize` 时，通过 `_handleErrorCallback(true, ...)` 上报，并设置 `isDispose = true`，提示通道需要销毁。
- **剩余数据搬移**：`DiscardReadBytes()` 保证缓冲区长期使用不会堆积旧数据。

---

### 9. UniLogger：内部日志工具

```csharp
internal static class UniLogger
{
    [Conditional("DEBUG")]
    public static void Log(string info)
    {
        UnityEngine.Debug.Log(info);
    }
    public static void Warning(string info)
    {
        UnityEngine.Debug.LogWarning(info);
    }
    public static void Error(string info)
    {
        UnityEngine.Debug.LogError(info);
    }
}
```

- **Log**：仅在定义了 `DEBUG` 宏时才会编译，发布构建中会被裁剪掉。
- **Warning / Error**：始终存在，用于输出重要日志与错误信息。
- 网络模块所有错误最终都会集中到这里输出，便于调试与线上排查。

---

### 10. 线程模型与调用时序（简要）

- **Unity 主线程**
  - `UniNetwork.Initalize / Destroy / CreateTcpClient / DestroyTcpClient`。
  - `UniNetworkDriver.Update` → `UniNetwork.Update` → 每个 `TcpClient.Update`。
  - `ThreadSyncContext.Update` 内部的所有回调执行。
  - `TcpChannel.Update / ProcessReceive / ProcessSend`（通过 `ThreadSyncContext.Post` 投递至主线程执行）。
  - 上层游戏逻辑对 `TcpClient.SendPackage` / `PickPackage` 等方法的调用。

- **IOCP 线程 / 其它线程**
  - `Socket.ConnectAsync` / `Socket.ReceiveAsync` / `Socket.SendAsync` 的底层完成线程。
  - `SocketAsyncEventArgs.Completed` 回调最初所在的线程，但回调主体会立刻通过 `_context.Post` 转移到主线程。

> 结论：**所有编码、解码、队列操作与上层回调最终都在 Unity 主线程执行**，游戏逻辑层不需要再关心线程同步问题。

---

### 11. 基本使用样例（与源码一致）

> 说明：以下示例基于仓库自带的 `README.md` 并结合源码整理，示例中使用默认的 `DefaultNetPackage` + `DefaultNetPackageEncoder/Decoder`，包体是 JSON 文本。

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UniFramework.Network;

// 登录请求消息
[System.Serializable]
public class LoginRequestMessage
{
    public string Name;
    public string Password;
}

// 登录反馈消息
[System.Serializable]
public class LoginResponseMessage
{
    public string Result;
}

public class UniNetworkSample : MonoBehaviour
{
    private TcpClient _client;

    // 创建 TCP 客户端并连接服务器
    public void CreateClient()
    {
        // 1. 初始化网络系统（只需调用一次）
        UniNetwork.Initalize();

        // 2. 创建 TCP 客户端，使用默认编解码器
        int packageMaxSize = short.MaxValue;
        var encoder = new DefaultNetPackageEncoder();
        var decoder = new DefaultNetPackageDecoder();
        _client = UniNetwork.CreateTcpClient(packageMaxSize, encoder, decoder);

        // 3. 连接服务器
        var remote = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);
        _client.ConnectAsync(remote, OnConnectServer);
    }

    // 关闭 TCP 客户端
    public void CloseClient()
    {
        if (_client != null)
        {
            _client.Dispose();
            _client = null;
        }

        // 如果整个游戏生命周期内都不再需要任何网络，可以按需销毁：
        // UniNetwork.Destroy();
    }

    // 连接结果回调（在主线程执行）
    private void OnConnectServer(SocketError error)
    {
        Debug.Log($"Server connect result : {error}");
        if (error == SocketError.Success)
        {
            Debug.Log("服务器连接成功！");
            // 可在此时主动发送登录请求
            SendLoginMessage();
        }
        else
        {
            Debug.Log("服务器连接失败！");
        }
    }

    // 每帧从 TCP 客户端中取出已解码的网络包
    private void Update()
    {
        if (_client == null)
            return;

        var netPackage = _client.PickPackage() as DefaultNetPackage;
        if (netPackage == null)
            return;

        // 根据 MsgID 做分发，这里假设 10001 是登录反馈消息
        if (netPackage.MsgID == 10001)
        {
            string json = Encoding.UTF8.GetString(netPackage.BodyBytes);
            var message = JsonUtility.FromJson<LoginResponseMessage>(json);
            Debug.Log($"登录结果：{message.Result}");
        }
        else
        {
            Debug.Log($"收到未知消息 ID：{netPackage.MsgID}");
        }
    }

    // 向服务器发送登录请求
    public void SendLoginMessage()
    {
        if (_client == null || !_client.IsConnected())
        {
            Debug.LogWarning("TCP 未连接，不能发送消息");
            return;
        }

        var message = new LoginRequestMessage
        {
            Name = "hevinci",
            Password = "1234567"
        };

        var netPackage = new DefaultNetPackage
        {
            MsgID = 10001, // 与服务端约定的登录请求消息 ID
            BodyBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message))
        };

        _client.SendPackage(netPackage);
    }
}
```

---

### 12. 小结与扩展建议

- **初始化时机**：`UniNetwork.Initalize()` 一般在游戏入口或网络管理器初始化阶段调用一次即可，不建议在每个场景重复初始化。
- **连接数量**：一个 `TcpClient` 对应一个 TCP 连接，`UniNetwork` 支持管理多个客户端，可按需创建不同用途的连接（如游戏长连接、日志上报连接等）。
- **协议扩展**：
  - 如需自定义协议，可实现自己的 `INetPackage` 类型及配套的 `INetPackageEncoder` / `INetPackageDecoder`，并在 `UniNetwork.CreateTcpClient` 中注入。
  - 只要保持「编码写入到 `RingBuffer`，解码从 `RingBuffer` 读出 `INetPackage` 列表」这一模式不变，其余细节都可以按业务需求定制。
- **线程安全**：所有解码与上层回调逻辑都在 Unity 主线程执行，`ThreadSyncContext` + `TcpClient.Update` 保证了跨线程安全，业务层不需要额外加锁。
