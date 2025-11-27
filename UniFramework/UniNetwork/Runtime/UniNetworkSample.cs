using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace UniFramework.Network
{
    /// <summary>
    /// UniNetwork 登录 + 玩家位置同步示例
    /// 挂在场景中的任意 GameObject 上，通过调用公共方法演示基本用法。
    /// </summary>
    public class UniNetworkSample : MonoBehaviour
    {
        #region 协议数据结构（需与服务端保持一致）

        [Serializable]
        public class LoginRequest
        {
            public string Account;
            public string Password;
        }

        [Serializable]
        public class LoginResponse
        {
            public int ResultCode;   // 0 = 成功，其它按服务端定义
            public string Message;
            public string Token;
        }

        [Serializable]
        public class PlayerInfo
        {
            public int PlayerId;
            public Vector3 Position;
        }

        [Serializable]
        public class PlayersPositionMessage
        {
            public List<PlayerInfo> Players = new List<PlayerInfo>();
        }

        #endregion

        #region 协议号约定（示例值，需与服务端统一）

        private const int MSG_LOGIN_REQUEST  = 10001;
        private const int MSG_LOGIN_RESPONSE = 10002;
        private const int MSG_PLAYERS_POS    = 20001;

        #endregion

        private TcpClient _client;
        private string _token;

        /// <summary>
        /// 本地缓存：其他玩家的位置（key: playerId, value: position）
        /// </summary>
        private readonly Dictionary<int, Vector3> _otherPlayers = new Dictionary<int, Vector3>();

        /// <summary>
        /// 建立连接并发送登录请求
        /// 在合适的时机调用（例如 Start 或 UI 按钮事件）
        /// </summary>
        public void ConnectAndLogin()
        {
            // 1. 初始化网络系统（只需调用一次，多次会抛异常）
            try
            {
                UniNetwork.Initalize();
            }
            catch (Exception)
            {
                // 已初始化则忽略异常
            }

            // 2. 创建 TCP 客户端，使用默认编解码器
            int packageMaxSize = short.MaxValue;
            var encoder = new DefaultNetPackageEncoder();
            var decoder = new DefaultNetPackageDecoder();
            _client = UniNetwork.CreateTcpClient(packageMaxSize, encoder, decoder);

            // 3. 连接服务器
            var remote = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000);
            _client.ConnectAsync(remote, OnConnectServer);
        }

        /// <summary>
        /// 关闭连接（并不销毁整个 UniNetwork 系统）
        /// </summary>
        public void CloseConnection()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            // 如果整个游戏生命周期内都不再使用网络，可按需调用：
            // UniNetwork.Destroy();
        }

        /// <summary>
        /// 连接结果回调（在主线程执行）
        /// </summary>
        private void OnConnectServer(SocketError error)
        {
            Debug.Log($"[UniNetworkSample] Server connect result : {error}");

            if (error == SocketError.Success)
            {
                Debug.Log("[UniNetworkSample] 服务器连接成功，发送登录请求");
                SendLoginRequest("test_account", "123456");
            }
            else
            {
                Debug.LogError("[UniNetworkSample] 服务器连接失败");
            }
        }

        /// <summary>
        /// 发送登录请求，期望获得登录反馈中的 Token
        /// </summary>
        public void SendLoginRequest(string account, string password)
        {
            if (_client == null || !_client.IsConnected())
            {
                Debug.LogWarning("[UniNetworkSample] 尚未连接服务器，无法发送登录请求");
                return;
            }

            var req = new LoginRequest
            {
                Account = account,
                Password = password
            };

            string json = JsonUtility.ToJson(req);

            var pkg = new DefaultNetPackage
            {
                MsgID = MSG_LOGIN_REQUEST,
                BodyBytes = Encoding.UTF8.GetBytes(json)
            };

            _client.SendPackage(pkg);
        }

        private void Update()
        {
            if (_client == null)
                return;

            // 持续拉取网络包，直到这一帧没有新包
            INetPackage raw = _client.PickPackage();
            while (raw != null)
            {
                var pkg = raw as DefaultNetPackage;
                if (pkg != null)
                {
                    HandlePackage(pkg);
                }

                raw = _client.PickPackage();
            }

            // 示例：此处可以根据 _otherPlayers 的数据来更新场景中其他玩家的表现
            // foreach (var kv in _otherPlayers)
            // {
            //     int playerId = kv.Key;
            //     Vector3 position = kv.Value;
            //     // TODO: 根据 playerId 定位玩家物体并设置其 transform.position
            // }
        }

        /// <summary>
        /// 根据 MsgID 分发不同的业务消息
        /// </summary>
        private void HandlePackage(DefaultNetPackage pkg)
        {
            switch (pkg.MsgID)
            {
                case MSG_LOGIN_RESPONSE:
                    HandleLoginResponse(pkg.BodyBytes);
                    break;
                case MSG_PLAYERS_POS:
                    HandlePlayersPosition(pkg.BodyBytes);
                    break;
                default:
                    Debug.Log($"[UniNetworkSample] 收到未处理的消息：MsgID={pkg.MsgID}, Len={pkg.BodyBytes?.Length ?? 0}");
                    break;
            }
        }

        /// <summary>
        /// 处理登录反馈，解析出 Token
        /// </summary>
        private void HandleLoginResponse(byte[] body)
        {
            string json = Encoding.UTF8.GetString(body);
            var rsp = JsonUtility.FromJson<LoginResponse>(json);

            if (rsp == null)
            {
                Debug.LogError("[UniNetworkSample] 解析 LoginResponse 失败");
                return;
            }

            if (rsp.ResultCode == 0)
            {
                _token = rsp.Token;
                Debug.Log($"[UniNetworkSample] 登录成功，Token = {_token}");
            }
            else
            {
                Debug.LogError($"[UniNetworkSample] 登录失败，Code={rsp.ResultCode}, Msg={rsp.Message}");
            }
        }

        /// <summary>
        /// 处理服务器推送的其他玩家位置列表
        /// </summary>
        private void HandlePlayersPosition(byte[] body)
        {
            string json = Encoding.UTF8.GetString(body);
            var msg = JsonUtility.FromJson<PlayersPositionMessage>(json);

            if (msg == null || msg.Players == null)
            {
                Debug.LogWarning("[UniNetworkSample] 收到的玩家位置数据为空或解析失败");
                return;
            }

            foreach (var p in msg.Players)
            {
                _otherPlayers[p.PlayerId] = p.Position;
            }

            Debug.Log($"[UniNetworkSample] 更新其他玩家位置，数量：{msg.Players.Count}");
        }
    }
}


