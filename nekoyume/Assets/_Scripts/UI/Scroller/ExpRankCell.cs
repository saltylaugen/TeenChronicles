using System;
using Nekoyume.Game.Controller;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.UI.Module;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace Nekoyume.UI.Scroller
{
    public class ExpRankCell : BaseCell<
        (int rank, RankingInfo rankingInfo),
        ExpRankScroll.ContextModel>
    {
        [SerializeField]
        private Image backgroundImage = null;

        [SerializeField]
        private Button avatarInfoButton = null;

        [SerializeField]
        private TextMeshProUGUI rankText = null;

        [SerializeField]
        private FramedCharacterView characterView = null;

        [SerializeField]
        private TextMeshProUGUI levelText = null;

        [SerializeField]
        private TextMeshProUGUI idText = null;

        [SerializeField]
        private TextMeshProUGUI stageText = null;

        [SerializeField]
        private Tween.DOTweenRectTransformMoveBy tweenMove = null;

        [SerializeField]
        private Tween.DOTweenGroupAlpha tweenAlpha = null;

        private RectTransform _rectTransformCache;
        private bool _isCurrentUser;

        public RectTransform RectTransform => _rectTransformCache
            ? _rectTransformCache
            : _rectTransformCache = GetComponent<RectTransform>();

        public RankingInfo RankingInfo { get; private set; }

        private void Awake()
        {
            avatarInfoButton.OnClickAsObservable()
                .ThrottleFirst(new TimeSpan(0, 0, 1))
                .Subscribe(_ =>
                {
                    AudioController.PlayClick();
                    Context.OnClick.OnNext(this);
                })
                .AddTo(gameObject);

            Game.Event.OnUpdatePlayerEquip
                .Where(_ => _isCurrentUser)
                .Subscribe(characterView.SetByPlayer)
                .AddTo(gameObject);
        }

        public override void UpdateContent((int rank, RankingInfo rankingInfo) itemData)
        {
            var (rank, rankingInfo) = itemData;

            RankingInfo = rankingInfo ?? throw new ArgumentNullException(nameof(rankingInfo));
            _isCurrentUser = States.Instance.CurrentAvatarState?.address ==
                             RankingInfo.AvatarAddress;

            backgroundImage.enabled = Index % 2 == 1;
            rankText.text = rank.ToString();
            levelText.text = RankingInfo.Level.ToString();
            idText.text = RankingInfo.AvatarName;
            stageText.text = RankingInfo.Exp.ToString();

            if (_isCurrentUser)
            {
                var player = Game.Game.instance.Stage.selectedPlayer;
                if (player is null)
                {
                    player = Game.Game.instance.Stage.GetPlayer();
                    characterView.SetByPlayer(player);
                    player.gameObject.SetActive(false);
                }
                else
                {
                    characterView.SetByPlayer(player);
                }
            }
            else
            {
                characterView.SetByAvatarAddress(RankingInfo.AvatarAddress);
            }

            tweenMove.StartDelay = rank * 0.16f;
            tweenAlpha.StartDelay = rank * 0.16f;
        }
    }
}
