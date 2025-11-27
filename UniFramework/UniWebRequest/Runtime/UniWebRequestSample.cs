using System.Collections;
using UnityEngine;

namespace UniFramework.WebRequest
{
    /// <summary>
    /// UniWebRequest 使用示例脚本集合
    /// 将本脚本挂在场景中的任意 GameObject 上，根据需要启用对应示例组件。
    /// </summary>
    public class UniWebRequestSample : MonoBehaviour
    {
        // 作为占位入口，本类目前不包含逻辑。
        // 可以在 Inspector 中挂载下面任意一个具体 Sample 组件来演示。
    }

    /// <summary>
    /// GET 文本 / 二进制 请求示例
    /// </summary>
    public class WebRequestGetSample : MonoBehaviour
    {
        [SerializeField] private string _url = "https://httpbin.org/get";

        private void Start()
        {
            StartCoroutine(GetRequestCoroutine());
        }

        private IEnumerator GetRequestCoroutine()
        {
            var request = new WebRequestGet(_url);

            // 可选：注册完成回调
            request.Completed += OnRequestCompleted;

            // 发送请求（timeout 单位：秒，0 表示使用 Unity 默认）
            request.SendRequest(timeout: 10);

            // 利用 WebRequestBase 的 IEnumerator 实现等待请求结束
            yield return request;

            if (request.Status == EReqeustStatus.Succeed)
            {
                string text = request.GetText();
                Debug.Log($"[WebRequestGetSample] GET 成功\nURL = {_url}\nResponse = {text}");
            }
            else
            {
                Debug.LogError(
                    $"[WebRequestGetSample] GET 失败\n" +
                    $"URL      = {_url}\n" +
                    $"Status   = {request.Status}\n" +
                    $"Code     = {request.ResponseCode}\n" +
                    $"Error    = {request.RequestError}"
                );
            }

            request.Dispose();
        }

        private void OnRequestCompleted(WebRequestBase reqBase)
        {
            var req = reqBase as WebRequestGet;
            if (req != null)
            {
                Debug.Log($"[WebRequestGetSample] Completed 回调触发，Status = {req.Status}, Code = {req.ResponseCode}");
            }
        }
    }

    /// <summary>
    /// 下载文件到本地的示例
    /// </summary>
    public class WebRequestFileSample : MonoBehaviour
    {
        [SerializeField] private string _fileUrl = "https://httpbin.org/bytes/1024";
        [SerializeField] private string _localFileName = "test.bin";

        private WebRequestFile _request;

        private void Start()
        {
            StartCoroutine(DownloadFileCoroutine());
        }

        private IEnumerator DownloadFileCoroutine()
        {
            string savePath = System.IO.Path.Combine(Application.persistentDataPath, _localFileName);
            Debug.Log($"[WebRequestFileSample] SavePath = {savePath}");

            _request = new WebRequestFile(_fileUrl);
            _request.SendRequest(savePath, timeout: 30);

            yield return _request;

            if (_request.Status == EReqeustStatus.Succeed)
            {
                Debug.Log(
                    $"[WebRequestFileSample] 文件下载成功\n" +
                    $"URL  = {_fileUrl}\n" +
                    $"Path = {savePath}"
                );
            }
            else
            {
                Debug.LogError(
                    $"[WebRequestFileSample] 文件下载失败\n" +
                    $"URL   = {_fileUrl}\n" +
                    $"Code  = {_request.ResponseCode}\n" +
                    $"Error = {_request.RequestError}"
                );
            }

            _request.Dispose();
        }
    }

    /// <summary>
    /// 只读取响应头部信息的示例（HEAD 请求）
    /// </summary>
    public class WebRequestHeaderSample : MonoBehaviour
    {
        [SerializeField] private string _url = "https://httpbin.org/get";

        private WebRequestHeader _request;

        private void Start()
        {
            StartCoroutine(CheckHeaderCoroutine());
        }

        private IEnumerator CheckHeaderCoroutine()
        {
            _request = new WebRequestHeader(_url);
            _request.SendRequest(timeout: 10);

            yield return _request;

            if (_request.Status != EReqeustStatus.Succeed)
            {
                Debug.LogError(
                    $"[WebRequestHeaderSample] 请求失败\n" +
                    $"URL   = {_url}\n" +
                    $"Code  = {_request.ResponseCode}\n" +
                    $"Error = {_request.RequestError}"
                );
                _request.Dispose();
                yield break;
            }

            string contentLength = _request.GetResponseHeader("Content-Length");
            string lastModified = _request.GetResponseHeader("Last-Modified");
            string contentType = _request.GetResponseHeader("Content-Type");

            Debug.Log(
                $"[WebRequestHeaderSample] 请求成功\n" +
                $"URL            = {_url}\n" +
                $"Content-Length = {contentLength}\n" +
                $"Last-Modified  = {lastModified}\n" +
                $"Content-Type   = {contentType}"
            );

            _request.Dispose();
        }
    }

    /// <summary>
    /// 下载并播放音频的示例
    /// </summary>
    public class WebRequestAudioSample : MonoBehaviour
    {
        [SerializeField] private string _audioUrl = "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3";
        [SerializeField] private AudioSource _audioSource;

        private WebRequestAudio _request;
        private RequestAsset _requestAsset;

        private void Start()
        {
            StartCoroutine(LoadAndPlayAudio());
        }

        private IEnumerator LoadAndPlayAudio()
        {
            _request = new WebRequestAudio(_audioUrl);
            _request.SendRequest(
                audioType: AudioType.MPEG,
                streamAudio: false,
                compressed: false,
                timeout: 15
            );

            yield return _request;

            if (_request.Status != EReqeustStatus.Succeed)
            {
                Debug.LogError(
                    $"[WebRequestAudioSample] 下载音频失败\n" +
                    $"URL   = {_audioUrl}\n" +
                    $"Code  = {_request.ResponseCode}\n" +
                    $"Error = {_request.RequestError}"
                );
                _request.Dispose();
                yield break;
            }

            _requestAsset = _request.GetRequestAsset();
            if (_requestAsset == null)
            {
                Debug.LogError("[WebRequestAudioSample] GetRequestAsset 返回为空");
                _request.Dispose();
                yield break;
            }

            AudioClip clip = _requestAsset.GetAudioClip();
            if (clip == null)
            {
                Debug.LogError("[WebRequestAudioSample] RequestAsset 中不是 AudioClip");
                _request.Dispose();
                yield break;
            }

            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();

            _audioSource.clip = clip;
            _audioSource.Play();

            Debug.Log("[WebRequestAudioSample] 音频加载并开始播放");

            // 如需在播放结束后回收资源，可根据业务时机调用：
            // _audioSource.Stop();
            // _requestAsset.UnloadAsset();
            // _request.Dispose();
        }
    }

    /// <summary>
    /// 下载纹理并应用到 UI / Sprite 的示例
    /// </summary>
    public class WebRequestTextureSample : MonoBehaviour
    {
        [SerializeField] private string _url = "https://via.placeholder.com/256";

        [SerializeField] private UnityEngine.UI.RawImage _rawImage;
        [SerializeField] private SpriteRenderer _spriteRenderer;

        private WebRequestTexture _request;
        private RequestAsset _requestAsset;

        private void Start()
        {
            StartCoroutine(LoadTextureCoroutine());
        }

        private IEnumerator LoadTextureCoroutine()
        {
            _request = new WebRequestTexture(_url);

            // 使用统一风格的接口：手动创建 DownloadHandlerTexture
            _request.SendRequest(nonReadable: false, timeout: 15);

            yield return _request;

            if (_request.Status != EReqeustStatus.Succeed)
            {
                Debug.LogError(
                    $"[WebRequestTextureSample] 下载图片失败\n" +
                    $"URL   = {_url}\n" +
                    $"Code  = {_request.ResponseCode}\n" +
                    $"Error = {_request.RequestError}"
                );
                _request.Dispose();
                yield break;
            }

            _requestAsset = _request.GetRequestAsset();
            if (_requestAsset == null)
            {
                Debug.LogError("[WebRequestTextureSample] GetRequestAsset 返回为空");
                _request.Dispose();
                yield break;
            }

            Texture2D tex = _requestAsset.GetTexture();
            if (tex == null)
            {
                Debug.LogError("[WebRequestTextureSample] RequestAsset 中不是 Texture2D");
                _request.Dispose();
                yield break;
            }

            if (_rawImage != null)
            {
                _rawImage.texture = tex;
            }

            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = _requestAsset.GetSprite();
            }

            Debug.Log("[WebRequestTextureSample] 图片加载完成并应用到界面");

            // 如需在不再使用时回收资源，可根据业务时机调用：
            // _requestAsset.UnloadAsset();
            // _request.Dispose();
        }
    }
}


