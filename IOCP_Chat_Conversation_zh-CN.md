## 基于 C# IO 完成端口的客户端/服务端聊天示例（对话记录）

用户：
请告诉 c#版本的io 完成端口的架构，并写一段示例给我

助手：
我是 **GPT-5.1** 模型，现在用中文为你说明。

## C# 下 IO 完成端口（IOCP）的整体架构

先强调一点：在 C# / .NET 里，你**不会直接操作 Win32 的 CreateIoCompletionPort**，而是通过 `Socket` / `SocketAsyncEventArgs` / 线程池 API，它们内部在 Windows 上就是用 IOCP 实现的。

### 核心思想（概念层）

- **内核完成通知**：  
  - 发起异步 IO（读/写/接受连接）后立即返回；  
  - 操作在内核里进行，完成后把“完成事件”投递到一个 **完成端口（IOCP 队列）**。
- **线程池轮询 IOCP**：  
  - 系统/CLR 维护一组工作线程，它们在 IOCP 上阻塞等待；  
  - 一旦 IO 完成，某个线程被唤醒，取出完成事件并回调你的代码。
- **少量线程，高并发**：  
  - 不需要为每个连接/请求开线程，而是共享这一组工作线程；  
  - 非常适合高并发网络服务器。

### 在 C# 中的典型架构（Socket + SocketAsyncEventArgs）

常见的高性能 IOCP 服务器结构大致如下：

- **监听套接字（listenSocket）**  
  - 用 `Socket`（通常 `AddressFamily.InterNetwork`, `SocketType.Stream`, `ProtocolType.Tcp`）去 `Bind+Listen`。  
  - 使用 `AcceptAsync` 异步接受连接。
- **SocketAsyncEventArgs 对象池（SAEA Pool）**  
  - 预创建很多 `SocketAsyncEventArgs`，避免频繁分配和 GC。  
  - 每个 `SocketAsyncEventArgs` 绑定一个固定大小的缓冲区（从一个**缓冲池**里分出来）。
- **缓冲区管理器（BufferManager）**  
  - 提前分配一大块字节数组，然后按块切分，给多个 `SocketAsyncEventArgs` 使用。  
  - 统一管理内存，提高性能，减少 GC。
- **事件回调（Completed 事件）**  
  - 每个 `SocketAsyncEventArgs` 注册 `Completed` 事件；  
  - 当异步操作（Accept/Receive/Send）完成时，在 IOCP 线程上触发回调；  
  - 在回调中决定：继续读/写、关闭连接、或把对象归还池子。
- **最外层逻辑**  
  - 通常是：  
    - Accept 完成 → 为连接分配一个 `SocketAsyncEventArgs` 做收发；  
    - Receive 完成 → 处理数据，再次 `ReceiveAsync`；  
    - Send 完成 → 如有剩余数据继续发；  
    - 出错 / 断开 → 回收资源。

## C# 示例：基于 IOCP 的简易 Echo 服务器

下面是一个**简化结构**（仅演示 IOCP 架构流程，不是生产代码），使用 `SocketAsyncEventArgs` 实现多连接 Echo 服务器（原样回显客户端发来的数据）。

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class IocpEchoServer
{
    private readonly Socket _listenSocket;
    private readonly int _port;
    private readonly int _maxConnections;
    private readonly int _bufferSize;

    // 接收/发送用的 SocketAsyncEventArgs 对象池
    private readonly ConcurrentStack<SocketAsyncEventArgs> _readWriteEventArgsPool;
    private readonly BufferManager _bufferManager;

    public IocpEchoServer(int port, int maxConnections = 1000, int bufferSize = 1024)
    {
        _port = port;
        _maxConnections = maxConnections;
        _bufferSize = bufferSize;

        _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _readWriteEventArgsPool = new ConcurrentStack<SocketAsyncEventArgs>();

        // 一块大内存池：maxConnections * bufferSize
        _bufferManager = new BufferManager(maxConnections * bufferSize, bufferSize);
        _bufferManager.InitBuffer();

        // 预创建 SAEA 并从缓冲池分配各自的 buffer
        for (int i = 0; i < maxConnections; i++)
        {
            var args = new SocketAsyncEventArgs();
            args.Completed += IO_Completed;
            _bufferManager.SetBuffer(args);
            _readWriteEventArgsPool.Push(args);
        }
    }

    public void Start()
    {
        _listenSocket.Bind(new IPEndPoint(IPAddress.Any, _port));
        _listenSocket.Listen(100);
        Console.WriteLine($"Server listening on port {_port}");

        StartAccept(null);
    }

    private void StartAccept(SocketAsyncEventArgs acceptEventArg)
    {
        if (acceptEventArg == null)
        {
            acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += Accept_Completed;
        }
        else
        {
            acceptEventArg.AcceptSocket = null;
        }

        bool willRaiseEvent = _listenSocket.AcceptAsync(acceptEventArg);
        if (!willRaiseEvent)
        {
            ProcessAccept(acceptEventArg);
        }
    }

    private void Accept_Completed(object sender, SocketAsyncEventArgs e)
    {
        ProcessAccept(e);
    }

    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            StartAccept(e); // 继续接受后续连接
            return;
        }

        Socket client = e.AcceptSocket;
        Console.WriteLine($"Client connected: {client.RemoteEndPoint}");

        // 从池中取一个 SAEA 用于收发
        if (!_readWriteEventArgsPool.TryPop(out var readWriteEventArgs))
        {
            Console.WriteLine("No more SAEA objects in pool.");
            client.Close();
        }
        else
        {
            readWriteEventArgs.UserToken = client;
            StartReceive(readWriteEventArgs);
        }

        // 继续异步接受下一位客户端
        StartAccept(e);
    }

    private void StartReceive(SocketAsyncEventArgs e)
    {
        Socket client = (Socket)e.UserToken;
        bool willRaiseEvent = client.ReceiveAsync(e);
        if (!willRaiseEvent)
        {
            ProcessReceive(e);
        }
    }

    private void IO_Completed(object sender, SocketAsyncEventArgs e)
    {
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                ProcessReceive(e);
                break;
            case SocketAsyncOperation.Send:
                ProcessSend(e);
                break;
        }
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        Socket client = (Socket)e.UserToken;

        if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
        {
            // 把收到的数据原样发回去（Echo）
            string text = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
            Console.WriteLine($"Recv from {client.RemoteEndPoint}: {text.Trim()}");

            // 直接使用同一个缓冲区发送
            e.SetBuffer(e.Offset, e.BytesTransferred);
            bool willRaiseEvent = client.SendAsync(e);
            if (!willRaiseEvent)
            {
                ProcessSend(e);
            }
        }
        else
        {
            CloseClientSocket(e);
        }
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        // 发送完成后，继续接收数据
        StartReceive(e);
    }

    private void CloseClientSocket(SocketAsyncEventArgs e)
    {
        Socket client = e.UserToken as Socket;

        try
        {
            client?.Shutdown(SocketShutdown.Both);
        }
        catch { /* ignore */ }

        client?.Close();
        e.UserToken = null;

        // 回收 SAEA 到池
        _readWriteEventArgsPool.Push(e);

        Console.WriteLine("Client disconnected.");
    }
}

// 非常简化的 BufferManager：一块大数组，按块切给多个 SAEA 用
public class BufferManager
{
    private readonly int _totalBytes;
    private readonly int _bufferSize;
    private readonly byte[] _buffer;
    private int _currentIndex;

    public BufferManager(int totalBytes, int bufferSize)
    {
        _totalBytes = totalBytes;
        _bufferSize = bufferSize;
        _buffer = new byte[totalBytes];
        _currentIndex = 0;
    }

    public void InitBuffer()
    {
        // 此处仅在构造里已分配，不需要额外操作
    }

    public void SetBuffer(SocketAsyncEventArgs args)
    {
        if (_currentIndex + _bufferSize > _totalBytes)
            throw new InvalidOperationException("Buffer pool exhausted.");

        args.SetBuffer(_buffer, _currentIndex, _bufferSize);
        _currentIndex += _bufferSize;
    }
}
```

在 `Main` 中使用时（注意：这里只是展示用法，不是“测试代码”的含义，可以按需整合到你自己的程序启动里）：

```csharp
public static class Program
{
    public static void Main(string[] args)
    {
        var server = new IocpEchoServer(port: 9000, maxConnections: 1000, bufferSize: 1024);
        server.Start();
        Console.ReadLine();
    }
}
```

### 小结（和 IOCP 的对应关系）

- **`AcceptAsync / ReceiveAsync / SendAsync`**：在 Windows 上本质上会使用 IOCP 发起异步 IO；  
- **`SocketAsyncEventArgs.Completed` 回调**：就是在 IOCP 完成端口上被线程池唤醒后执行的处理逻辑；  
- **对象池 + 缓冲池**：减少分配，匹配 IOCP“少线程、多 IO”的高吞吐设计。

如果你后面想把这个结构移植/封装到 Unity 项目里，也可以在 `.NET 6/7 + Windows` 或 Unity 的 Windows Standalone 环境中复用这套思路。需要的话我可以帮你基于这个示例继续拆分成更干净的“连接会话类 / 服务类”结构。

用户：
添加一下客户端的代码

实现，login，sendmsg，broadcastmsg

服务端也实现 login协议，sendmsg，和broadcastmsg协议

使用原生的c#提供的io completed编写，不要参考任何uniframework内部的代码。纯c#的。

助手：
我是 **GPT-5.1** 模型。

下面给你一个**纯 C#、基于 SocketAsyncEventArgs（IOCP）** 的简化聊天示例：  
- **协议**：支持 `Login`、`SendMsg`、`BroadcastMsg` 三种消息；  
- **服务器**：维护多客户端连接，处理三种协议；  
- **客户端**：能发送 `login / sendmsg / broadcastmsg`，并接收服务器的回执/广播；  
- **实现方式**：全部使用 `SocketAsyncEventArgs.Completed` 事件，不依赖任何 UniFramework 代码。

---

### 一、简单协议设计（简化版）

- **消息格式**（所有消息）：
  - 前 4 字节：`int`，小端，表示“消息体长度 bodyLen”（**不含这 4 个字节本身**）；
  - 第 5 字节：`byte`，表示消息类型 `MessageType`；
  - 后面 `bodyLen - 1` 字节：UTF8 文本内容（payload）。
- **消息类型**：
  - `1 = Login`：payload 为用户名（字符串）；
  - `2 = SendMsg`：客户端发给服务器的普通消息，服务器回显给该客户端；
  - `3 = BroadcastMsg`：客户端发给服务器，服务器广播给所有在线客户端。

---

### 二、完整示例代码（服务器 + 客户端，纯 C#）

你可以放在同一个项目里（例如 ConsoleApp），按需要拆分成多个文件即可。

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IocpChatSample
{
    /// <summary>
    /// 消息类型定义
    /// </summary>
    public enum MessageType : byte
    {
        Login = 1,
        SendMsg = 2,
        BroadcastMsg = 3,
    }

    /// <summary>
    /// 消息编解码：长度前缀 + 消息类型 + UTF8 文本
    /// </summary>
    public static class MessageFraming
    {
        public static byte[] BuildMessage(MessageType type, string text)
        {
            if (text == null) text = string.Empty;
            byte[] payloadBytes = Encoding.UTF8.GetBytes(text);
            int bodyLen = 1 + payloadBytes.Length; // 1 字节类型 + N 字节文本

            byte[] buffer = new byte[4 + bodyLen];
            // 写入 bodyLen（小端）
            byte[] lenBytes = BitConverter.GetBytes(bodyLen);
            Buffer.BlockCopy(lenBytes, 0, buffer, 0, 4);

            // 写入类型
            buffer[4] = (byte)type;

            // 写入正文
            if (payloadBytes.Length > 0)
                Buffer.BlockCopy(payloadBytes, 0, buffer, 5, payloadBytes.Length);

            return buffer;
        }
    }

    /// <summary>
    /// 服务器端每个客户端的会话（仅负责收包缓存与拆包）
    /// </summary>
    internal class ServerClientSession
    {
        public const int ReceiveCacheSize = 8192;

        public Socket Socket { get; }
        public string UserName { get; set; }

        private readonly byte[] _cache = new byte[ReceiveCacheSize];
        private int _dataLength = 0;

        public ServerClientSession(Socket socket)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        public void AppendData(byte[] src, int offset, int count)
        {
            if (count <= 0) return;
            if (_dataLength + count > _cache.Length)
                throw new InvalidOperationException("服务器接收缓存溢出，可以增大 ReceiveCacheSize 或实现更复杂的缓存管理。");

            Buffer.BlockCopy(src, offset, _cache, _dataLength, count);
            _dataLength += count;
        }

        /// <summary>
        /// 从缓存中尝试取出一条完整消息（有则返回 true 并消费掉）
        /// </summary>
        public bool TryGetNextMessage(out MessageType type, out string payload)
        {
            type = 0;
            payload = string.Empty;

            // 至少需要 4 字节长度
            if (_dataLength < 4)
                return false;

            int bodyLen = BitConverter.ToInt32(_cache, 0);
            if (bodyLen <= 0 || bodyLen > _cache.Length)
            {
                // 协议错误，简单起见直接丢弃
                _dataLength = 0;
                return false;
            }

            // 是否收齐一条完整消息
            if (_dataLength < 4 + bodyLen)
                return false;

            byte typeByte = _cache[4];
            type = (MessageType)typeByte;

            int payloadLen = bodyLen - 1;
            if (payloadLen > 0)
            {
                payload = Encoding.UTF8.GetString(_cache, 5, payloadLen);
            }
            else
            {
                payload = string.Empty;
            }

            // 把剩余数据前移
            int remaining = _dataLength - (4 + bodyLen);
            if (remaining > 0)
            {
                Buffer.BlockCopy(_cache, 4 + bodyLen, _cache, 0, remaining);
            }
            _dataLength = remaining;

            return true;
        }
    }

    /// <summary>
    /// IOCP 聊天服务器（支持 Login / SendMsg / BroadcastMsg）
    /// </summary>
    public class IocpChatServer
    {
        private readonly Socket _listenSocket;
        private readonly ConcurrentDictionary<Socket, ServerClientSession> _clients
            = new ConcurrentDictionary<Socket, ServerClientSession>();

        public int Port { get; }

        public IocpChatServer(int port)
        {
            Port = port;
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Start()
        {
            _listenSocket.Bind(new IPEndPoint(IPAddress.Any, Port));
            _listenSocket.Listen(100);
            Console.WriteLine($"[Server] Listening on port {Port}");

            StartAccept();
        }

        #region Accept

        private void StartAccept()
        {
            var acceptArgs = new SocketAsyncEventArgs();
            acceptArgs.Completed += Accept_Completed;

            bool willRaiseEvent = _listenSocket.AcceptAsync(acceptArgs);
            if (!willRaiseEvent)
                ProcessAccept(acceptArgs);
        }

        private void Accept_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                Console.WriteLine($"[Server] Accept error: {e.SocketError}");
                e.Dispose();
                StartAccept();
                return;
            }

            Socket client = e.AcceptSocket;
            Console.WriteLine($"[Server] Client connected: {client.RemoteEndPoint}");

            var session = new ServerClientSession(client);
            _clients[client] = session;

            // 为该客户端准备接收的 SAEA
            var recvArgs = new SocketAsyncEventArgs();
            recvArgs.SetBuffer(new byte[ServerClientSession.ReceiveCacheSize], 0, ServerClientSession.ReceiveCacheSize);
            recvArgs.UserToken = session;
            recvArgs.Completed += IO_Completed;

            StartReceive(recvArgs);

            // 准备接受下一个客户端
            e.AcceptSocket = null;
            bool willRaiseEvent = _listenSocket.AcceptAsync(e);
            if (!willRaiseEvent)
                ProcessAccept(e);
        }

        #endregion

        #region Receive / Send

        private void StartReceive(SocketAsyncEventArgs e)
        {
            var session = (ServerClientSession)e.UserToken;
            try
            {
                bool willRaiseEvent = session.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                    ProcessReceive(e);
            }
            catch (ObjectDisposedException)
            {
                // Socket 已关闭，忽略
                CloseClient(session.Socket);
            }
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
            }
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            var session = (ServerClientSession)e.UserToken;
            Socket client = session.Socket;

            if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
            {
                Console.WriteLine($"[Server] Client disconnected: {client.RemoteEndPoint}");
                CloseClient(client);
                e.Dispose();
                return;
            }

            // 把数据 append 到 session 缓存，然后拆包
            session.AppendData(e.Buffer, e.Offset, e.BytesTransferred);

            while (session.TryGetNextMessage(out MessageType type, out string payload))
            {
                HandleMessage(session, type, payload);
            }

            // 继续异步接收
            StartReceive(e);
        }

        private void HandleMessage(ServerClientSession session, MessageType type, string payload)
        {
            switch (type)
            {
                case MessageType.Login:
                    session.UserName = payload;
                    Console.WriteLine($"[Server] User login: {session.UserName}, from {session.Socket.RemoteEndPoint}");

                    // 简单返回一个 Login 响应
                    SendAsync(session.Socket, MessageType.Login, "Login OK");
                    break;

                case MessageType.SendMsg:
                    Console.WriteLine($"[Server] (SendMsg) {session.UserName}: {payload}");

                    // 服务器回显给该客户端
                    SendAsync(session.Socket, MessageType.SendMsg, $"Echo: {payload}");
                    break;

                case MessageType.BroadcastMsg:
                    Console.WriteLine($"[Server] (Broadcast) {session.UserName}: {payload}");

                    // 广播给所有在线客户端
                    BroadcastFrom(session, payload);
                    break;
            }
        }

        private void BroadcastFrom(ServerClientSession from, string message)
        {
            string finalText = $"{from.UserName}: {message}";
            foreach (var kv in _clients)
            {
                Socket client = kv.Key;
                SendAsync(client, MessageType.BroadcastMsg, finalText);
            }
        }

        private void SendAsync(Socket socket, MessageType type, string text)
        {
            if (!socket.Connected) return;

            byte[] data = MessageFraming.BuildMessage(type, text);
            var sendArgs = new SocketAsyncEventArgs();
            sendArgs.SetBuffer(data, 0, data.Length);
            sendArgs.UserToken = socket;
            sendArgs.Completed += IO_Completed;

            bool willRaiseEvent;
            try
            {
                willRaiseEvent = socket.SendAsync(sendArgs);
            }
            catch (ObjectDisposedException)
            {
                sendArgs.Dispose();
                return;
            }

            if (!willRaiseEvent)
                ProcessSend(sendArgs);
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            // 此示例中发送完成后只需释放资源
            e.Dispose();
        }

        private void CloseClient(Socket client)
        {
            if (client == null) return;

            if (_clients.TryRemove(client, out var _))
            {
                try
                {
                    client.Shutdown(SocketShutdown.Both);
                }
                catch { }

                client.Close();
            }
        }

        #endregion
    }

    /// <summary>
    /// IOCP 聊天客户端（支持 Login / SendMsg / BroadcastMsg）
    /// </summary>
    public class IocpChatClient
    {
        private readonly Socket _socket;
        private SocketAsyncEventArgs _recvArgs;

        private readonly byte[] _cache = new byte[8192];
        private int _dataLength = 0;

        public string UserName { get; private set; }

        public IocpChatClient()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Connect(string host, int port, string userName)
        {
            UserName = userName ?? string.Empty;
            _socket.Connect(host, port);
            Console.WriteLine($"[Client] Connected to {host}:{port}");

            // 启动接收
            _recvArgs = new SocketAsyncEventArgs();
            _recvArgs.SetBuffer(new byte[4096], 0, 4096);
            _recvArgs.Completed += Recv_Completed;
            _recvArgs.UserToken = this;

            StartReceive();

            // 连接后立即登录
            Login(UserName);
        }

        #region Receive

        private void StartReceive()
        {
            bool willRaiseEvent;
            try
            {
                willRaiseEvent = _socket.ReceiveAsync(_recvArgs);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!willRaiseEvent)
                ProcessReceive(_recvArgs);
        }

        private void Recv_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
            {
                Console.WriteLine("[Client] Disconnected from server.");
                Close();
                e.Dispose();
                return;
            }

            AppendData(e.Buffer, e.Offset, e.BytesTransferred);

            while (TryGetNextMessage(out MessageType type, out string payload))
            {
                HandleMessage(type, payload);
            }

            StartReceive();
        }

        private void AppendData(byte[] src, int offset, int count)
        {
            if (count <= 0) return;
            if (_dataLength + count > _cache.Length)
                throw new InvalidOperationException("客户端接收缓存溢出，可以增大 _cache 的大小。");

            Buffer.BlockCopy(src, offset, _cache, _dataLength, count);
            _dataLength += count;
        }

        private bool TryGetNextMessage(out MessageType type, out string payload)
        {
            type = 0;
            payload = string.Empty;

            if (_dataLength < 4)
                return false;

            int bodyLen = BitConverter.ToInt32(_cache, 0);
            if (bodyLen <= 0 || bodyLen > _cache.Length)
            {
                _dataLength = 0;
                return false;
            }

            if (_dataLength < 4 + bodyLen)
                return false;

            byte t = _cache[4];
            type = (MessageType)t;

            int payloadLen = bodyLen - 1;
            if (payloadLen > 0)
                payload = Encoding.UTF8.GetString(_cache, 5, payloadLen);
            else
                payload = string.Empty;

            int remaining = _dataLength - (4 + bodyLen);
            if (remaining > 0)
            {
                Buffer.BlockCopy(_cache, 4 + bodyLen, _cache, 0, remaining);
            }
            _dataLength = remaining;

            return true;
        }

        private void HandleMessage(MessageType type, string payload)
        {
            switch (type)
            {
                case MessageType.Login:
                    Console.WriteLine($"[Client] Login response: {payload}");
                    break;

                case MessageType.SendMsg:
                    Console.WriteLine($"[Client] SendMsg echo from server: {payload}");
                    break;

                case MessageType.BroadcastMsg:
                    Console.WriteLine($"[Client] Broadcast from server: {payload}");
                    break;
            }
        }

        #endregion

        #region Send

        public void Login(string userName)
        {
            SendAsync(MessageType.Login, userName);
        }

        public void SendMsg(string text)
        {
            SendAsync(MessageType.SendMsg, text);
        }

        public void BroadcastMsg(string text)
        {
            SendAsync(MessageType.BroadcastMsg, text);
        }

        private void SendAsync(MessageType type, string text)
        {
            if (!_socket.Connected) return;

            byte[] data = MessageFraming.BuildMessage(type, text);
            var sendArgs = new SocketAsyncEventArgs();
            sendArgs.SetBuffer(data, 0, data.Length);
            sendArgs.Completed += Send_Completed;

            try
            {
                bool willRaiseEvent = _socket.SendAsync(sendArgs);
                if (!willRaiseEvent)
                    ProcessSend(sendArgs);
            }
            catch (ObjectDisposedException)
            {
                sendArgs.Dispose();
            }
        }

        private void Send_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            // 此示例中发送完成不需要额外逻辑，直接 Dispose 即可
            e.Dispose();
        }

        #endregion

        public void Close()
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch { }

            _socket.Close();
        }
    }
}
```

---

### 三、如何使用（示意说明，非测试代码）

- **服务器端**：创建 `IocpChatServer`，调用 `Start()`，常驻运行即可。  
- **客户端**：创建 `IocpChatClient`，调用 `Connect("127.0.0.1", 9000, "UserA")`，之后：
  - `client.SendMsg("hello")` → 服务器回一个 `Echo: hello`；
  - `client.BroadcastMsg("hi all")` → 服务器把 `"UserA: hi all"` 广播给所有客户端。

如果你希望我再进一步：  
- 把这个示例拆成“协议层 / 会话层 / 业务层”三个类结构；  
- 或者改造成适合 Unity 的类（比如挂在一个 `MonoBehaviour` 上、在主线程分发事件），我可以在保持逻辑不变的前提下帮你重构。


