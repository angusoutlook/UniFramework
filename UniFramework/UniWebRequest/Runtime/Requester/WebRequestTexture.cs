using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine;

namespace UniFramework.WebRequest
{
    public sealed class WebRequestTexture : WebRequestBase
    {
        private RequestAsset _cachedAsset;

        public WebRequestTexture(string url) : base(url)
        {
        }

        /// <summary>
        /// 发送资源请求
        /// </summary>
        /// <param name="timeout">超时：从请求开始计时</param>
        public void SendRequest(int timeout = 0)
		{
			// 兼容旧接口：默认使用可读纹理
			if (_webRequest == null)
			{
				SendRequest(false, timeout);
			}
		}

		/// <summary>
		/// 发送资源请求（手动创建 DownloadHandlerTexture，风格与其它请求器保持一致）
		/// </summary>
		/// <param name="nonReadable">是否创建为不可读纹理（节省内存，但无法在 CPU 侧读写像素）</param>
		/// <param name="timeout">超时：从请求开始计时</param>
		public void SendRequest(bool nonReadable, int timeout = 0)
        {
            if (_webRequest == null)
            {
				_webRequest = new UnityWebRequest(URL, UnityWebRequest.kHttpVerbGET);
				var downloadHandler = new DownloadHandlerTexture(!nonReadable); // 参数是 readable
				_webRequest.downloadHandler = downloadHandler;
				_webRequest.disposeDownloadHandlerOnDispose = true;
                _webRequest.timeout = timeout;
                _operation = _webRequest.SendWebRequest();
                _operation.completed += CompleteInternal;
            }
        }

        public RequestAsset GetRequestAsset()
        {
            if (IsDone() == false)
            {
                UniLogger.Warning("Web request is not finished yet!");
                return null;
            }

            if (Status != EReqeustStatus.Succeed)
                return null;

            if (_cachedAsset == null)
            {
                var texture = DownloadHandlerTexture.GetContent(_webRequest);
                _cachedAsset = new RequestAsset(URL, texture);
            }
            return _cachedAsset;
        }
    }
}