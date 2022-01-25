
// [TEN Code Block Start]
using TMPro;
using UniRx;
using System.Collections;
using System.Collections.Generic;
using Nekoyume.Game.Controller;
using Nekoyume.Helper;
using Nekoyume.Model.Mail;
using Nekoyume.UI.Scroller;
// [TEN Code Block End]
using Libplanet;
using Libplanet.Blocks;
// [TEN Code Block Start]
using UnityEngine;
using UnityEngine.Networking;
using Nekoyume.Game.TenScriptableObject;
// [TEN Code Block End]


namespace Nekoyume.UI
{
    public class VersionSystem : SystemWidget
    {
        public TextMeshProUGUI informationText;
        private int _version;
        private long _blockIndex;
        private BlockHash _hash;
        
        // [TEN Code Block Start]
        public TextMeshProUGUI nineScanBlockInfo;
        private bool isTrying = false;
        // [TEN Code Block End]

        protected override void Awake()
        {
            base.Awake();
            Game.Game.instance.Agent.BlockIndexSubject.Subscribe(SubscribeBlockIndex).AddTo(gameObject);
            Game.Game.instance.Agent.BlockTipHashSubject.Subscribe(SubscribeBlockHash).AddTo(gameObject);
        }

        // [TEN Code Block Start]
        void Update() {
            if (Input.GetKey(KeyCode.I))
            {
                if (!isTrying)
                {
                    Update9cinfoText();
                }
            }
        }
        // [TEN Code Block End]

        public void SetVersion(int version)
        {
            _version = version;
            UpdateText();
        }

        private void SubscribeBlockIndex(long blockIndex)
        {
            _blockIndex = blockIndex;
            UpdateText();
        }

        private void SubscribeBlockHash(BlockHash hash)
        {
            _hash = hash;
            UpdateText();
        }

        private void UpdateText()
        {
            const string format = "APV: {0} / #{1} / Hash: {2}";
            var hash = _hash.ToString();
            var text = string.Format(
                format,
                _version,
                _blockIndex,
                hash.Length >= 4 ? hash.Substring(0, 4) : "...");
            informationText.text = text;
        }

        // [TEN Code Block Start]
        public void Update9cinfoText()
        {
            nineScanBlockInfo.fontSize = nineScanBlockInfo.fontSize + 2;
            informationText.fontSize = informationText.fontSize + 2;

            nineScanBlockInfo.gameObject.SetActive(true);
            isTrying = true;
            const string format = "9cscan's block: {0}";
            nineScanBlockInfo.text = string.Format(
                format,
                "..."
            );
            StartCoroutine(Fetch9cscanInfo());
        }

        private IEnumerator Fetch9cscanInfo()
        {
            const string format = "9cscan's block: {0}";

            // URL hard coding
            using (UnityWebRequest webRequest = UnityWebRequest.Get("https://api.9cscan.com/blocks?limit=1"))
            {
                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                switch (webRequest.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.DataProcessingError:
                    case UnityWebRequest.Result.ProtocolError:
                        OneLineSystem.Push(
                            MailType.System,
                            "Fail get 9cscan's block info",
                            NotificationCell.NotificationType.Notification);
                        break;
                    case UnityWebRequest.Result.Success:
                        var result = JsonUtility.FromJson<Response>(webRequest.downloadHandler.text);
                    
                        nineScanBlockInfo.text = string.Format(
                            format,
                            result.Blocks[0].Index
                        );
                        break;
                }
            }

            StartCoroutine(Cooldown());
        }

        private IEnumerator Cooldown()
        {
            yield return new WaitForSeconds(4f);
            isTrying = false;
            nineScanBlockInfo.gameObject.SetActive(false);

            nineScanBlockInfo.fontSize = nineScanBlockInfo.fontSize - 2;
            informationText.fontSize = informationText.fontSize - 2;
        }
        // [TEN Code Block End]
    }
}
