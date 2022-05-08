using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Bencodex.Types;
using Cysharp.Threading.Tasks;
using Libplanet;
using Libplanet.Blocks;
using Nekoyume.Action;
using Nekoyume.Game.Controller;
using Nekoyume.Helper;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.Model.Mail;
using Nekoyume.UI.Model;
using Nekoyume.UI.Module;
using Nekoyume.UI.Scroller;
using UnityEngine;
using UnityEngine.UI;
using StateExtensions = Nekoyume.Model.State.StateExtensions;

namespace Nekoyume.UI
{
    using Nekoyume.Model.BattleStatus;
    using UniRx;

    public class RankingBoard : Widget
    {
        [SerializeField]
        private Button closeButton;

        [SerializeField]
        private ArenaRankScroll arenaRankScroll = null;

        [SerializeField]
        private ArenaRankCell currentAvatarCellView = null;

        [SerializeField]
        private SpeechBubble speechBubble = null;

        // [TEN Code Block Start]
        [SerializeField]
        private Button upButton;
        [SerializeField]
        private Button downButton;
        private int paginationState = -1;
        // [TEN Code Block End]

        private Nekoyume.Model.State.RankingInfo[] _avatarRankingStates;

        private List<(int rank, ArenaInfo arenaInfo)> _weeklyCachedInfo =
            new List<(int rank, ArenaInfo arenaInfo)>();

        private BlockHash? _cachedBlockHash;

        private readonly List<IDisposable> _disposablesFromShow = new List<IDisposable>();

        private ArenaInfoList _arenaInfoList;

        protected override void Awake()
        {
            base.Awake();

            arenaRankScroll.OnClickAvatarInfo
                .Subscribe(cell => OnClickAvatarInfo(
                    cell.RectTransform,
                    cell.ArenaInfo.AvatarAddress))
                .AddTo(gameObject);
            arenaRankScroll.OnClickChallenge.Subscribe(OnClickChallenge).AddTo(gameObject);
            currentAvatarCellView.OnClickAvatarInfo
                .Subscribe(cell => OnClickAvatarInfo(
                    cell.RectTransform,
                    cell.ArenaInfo.AvatarAddress))
                .AddTo(gameObject);

            closeButton.onClick.AddListener(() =>
            {
                Close(true);
                Game.Event.OnRoomEnter.Invoke(true);
            });

            // [TEN Code Block Start]
            upButton.onClick.AddListener(async () =>
            {
                OneLineSystem.Push(
                    MailType.System,
                    "It can take a long time. Please don't leave the current window",
                    NotificationCell.NotificationType.Information);

                paginationState = 1;
                await UniTask.Run(async () =>
                {
                    await UpdateWeeklyCache(States.Instance.WeeklyArenaState);
                });
                UpdateArena();

                OneLineSystem.Push(
                    MailType.System,
                    "Load Finish",
                    NotificationCell.NotificationType.Information);
            });
            downButton.onClick.AddListener(async () =>
            {
                OneLineSystem.Push(
                    MailType.System,
                    "It can take a long time. Please don't leave the current window",
                    NotificationCell.NotificationType.Information);

                paginationState = 0;
                await UniTask.Run(async () =>
                {
                    await UpdateWeeklyCache(States.Instance.WeeklyArenaState);
                });
                UpdateArena();

                OneLineSystem.Push(
                    MailType.System,
                    "Load Finish",
                    NotificationCell.NotificationType.Information);
            });
            // [TEN Code Block End]

            CloseWidget = () =>
            {
                Close(true);
                Game.Event.OnRoomEnter.Invoke(true);
            };
            SubmitWidget = null;
            _arenaInfoList = new ArenaInfoList();
        }

        public void Show(WeeklyArenaState weeklyArenaState = null) => ShowAsync(weeklyArenaState);

        private async void ShowAsync(WeeklyArenaState weeklyArenaState = null)
        {
            Find<DataLoadingScreen>().Show();

            var stage = Game.Game.instance.Stage;
            stage.LoadBackground("ranking");
            stage.GetPlayer().gameObject.SetActive(false);

            if (weeklyArenaState is null)
            {
                var agent = Game.Game.instance.Agent;
                if (!_cachedBlockHash.Equals(agent.BlockTipHash))
                {
                    _cachedBlockHash = agent.BlockTipHash;
                    await UniTask.Run(async () =>
                    {
                        var gameConfigState = States.Instance.GameConfigState;
                        var weeklyArenaIndex = (int)agent.BlockIndex / gameConfigState.WeeklyArenaInterval;
                        var weeklyArenaAddress = WeeklyArenaState.DeriveAddress(weeklyArenaIndex);
                        weeklyArenaState =
                            new WeeklyArenaState((Bencodex.Types.Dictionary) await agent.GetStateAsync(weeklyArenaAddress));
                        States.Instance.SetWeeklyArenaState(weeklyArenaState);
                        await UpdateWeeklyCache(States.Instance.WeeklyArenaState);
                    });
                }
            }
            else
            {
                await UniTask.Run(async () =>
                {
                    await UpdateWeeklyCache(weeklyArenaState);
                });
            }

            base.Show(true);

            Find<DataLoadingScreen>().Close();
            AudioController.instance.PlayMusic(AudioController.MusicCode.Ranking);
            HelpTooltip.HelpMe(100015, true);
            speechBubble.SetKey("SPEECH_RANKING_BOARD_GREETING_");
            StartCoroutine(speechBubble.CoShowText());

            Find<HeaderMenuStatic>().Show(HeaderMenuStatic.AssetVisibleState.Battle);
            UpdateArena();
            Game.Event.OnUpdatePlayerEquip.Subscribe(player =>
                {
                    arenaRankScroll.UpdateConditionalStateOfChallengeButtons
                        .OnNext(Util.CanBattle(player, Array.Empty<int>()));
                })
                .AddTo(_disposablesFromShow);
        }

        public override void Close(bool ignoreCloseAnimation = false)
        {
            _disposablesFromShow.DisposeAllAndClear();
            base.Close(ignoreCloseAnimation);
            speechBubble.Hide();
        }

        private void UpdateArena()
        {
            var weeklyArenaState = States.Instance.WeeklyArenaState;
            if (weeklyArenaState is null)
            {
                return;
            }

            var avatarAddress = States.Instance.CurrentAvatarState?.address;
            if (!avatarAddress.HasValue)
            {
                return;
            }

            if (!_weeklyCachedInfo.Any())
            {
                currentAvatarCellView.ShowMyDefaultInfo();

                UpdateBoard();
                return;
            }

            var arenaInfo = _weeklyCachedInfo[0].arenaInfo;
            if (!arenaInfo.Active)
            {
                currentAvatarCellView.ShowMyDefaultInfo();
                arenaInfo.Activate();
            }

            UpdateBoard();
        }

        private async void UpdateBoard()
        {
            var weeklyArenaState = States.Instance.WeeklyArenaState;
            if (weeklyArenaState is null)
            {
                arenaRankScroll.ClearData();
                arenaRankScroll.Show();
                return;
            }

            var currentAvatarAddress = States.Instance.CurrentAvatarState?.address;
            if (!currentAvatarAddress.HasValue || await Game.Game.instance.Agent.GetStateAsync(
                    weeklyArenaState.address.Derive(currentAvatarAddress.Value.ToByteArray())
                ) is null)
            {
                currentAvatarCellView.ShowMyDefaultInfo();

                var canBattle = Util.CanBattle(Game.Game.instance.Stage.SelectedPlayer,
                    Array.Empty<int>());
                arenaRankScroll.Show(_weeklyCachedInfo
                    .Select(tuple => new ArenaRankCell.ViewModel
                    {
                        rank = tuple.rank,
                        arenaInfo = tuple.arenaInfo,
                        currentAvatarCanBattle = canBattle,
                    }).ToList(), true);
                arenaRankScroll.UpdateConditionalStateOfChallengeButtons
                    .OnNext(canBattle);
                // NOTE: If you want to test many arena cells, use below instead of above.
                // arenaRankScroll.Show(Enumerable
                //     .Range(1, 1000)
                //     .Select(rank => new ArenaRankCell.ViewModel
                //     {
                //         rank = rank,
                //         arenaInfo = new ArenaInfo(
                //             States.Instance.CurrentAvatarState,
                //             Game.Game.instance.TableSheets.CharacterSheet,
                //             true)
                //         {
                //             ArmorId = States.Instance.CurrentAvatarState.GetPortraitId()
                //         },
                //         currentAvatarArenaInfo = null
                //     }).ToList(), true);

                return;
            }

            var (currentAvatarRank, currentAvatarArenaInfo) = _weeklyCachedInfo
                .FirstOrDefault(info =>
                    info.arenaInfo.AvatarAddress.Equals(currentAvatarAddress));
            if (currentAvatarArenaInfo is null)
            {
                currentAvatarRank = -1;
                currentAvatarArenaInfo = new ArenaInfo(
                    States.Instance.CurrentAvatarState,
                    Game.Game.instance.TableSheets.CharacterSheet,
                    false);
            }

            var currentAvatarCanBattle =
                Util.CanBattle(Game.Game.instance.Stage.SelectedPlayer,
                    Array.Empty<int>());
            arenaRankScroll.Show(_weeklyCachedInfo
                .Select(tuple => new ArenaRankCell.ViewModel
                {
                    rank = tuple.rank,
                    arenaInfo = tuple.arenaInfo,
                    currentAvatarArenaInfo = currentAvatarArenaInfo,
                    currentAvatarCanBattle = currentAvatarCanBattle
                }).ToList(), true);

            currentAvatarCellView.Show((
                currentAvatarRank,
                currentAvatarArenaInfo,
                currentAvatarArenaInfo,
                false));
        }

        private static void OnClickAvatarInfo(RectTransform rectTransform, Address address)
        {
            // NOTE: 블록 익스플로러 연결 코드. 이후에 참고하기 위해 남겨 둡니다.
            // Application.OpenURL(string.Format(GameConfig.BlockExplorerLinkFormat, avatarAddress));
            Find<AvatarTooltip>().Show(rectTransform, address);
        }

        private void OnClickChallenge(ArenaRankCell arenaRankCell)
        {
            var currentAvatarInventory = States.Instance.CurrentAvatarState.inventory;

            Game.Game.instance.ActionManager.RankingBattle(
                arenaRankCell.ArenaInfo.AvatarAddress,
                currentAvatarInventory.Costumes
                    .Where(i => i.equipped)
                    .Select(i => i.ItemId).ToList(),
                currentAvatarInventory.Equipments
                    .Where(i => i.equipped)
                    .Select(i => i.ItemId).ToList()
            ).Subscribe();
            Find<ArenaBattleLoadingScreen>().Show(arenaRankCell.ArenaInfo);
        }

        private void SubscribeBackButtonClick(HeaderMenuStatic headerMenuStatic)
        {
            var avatarInfo = Find<AvatarInfoPopup>();
            var friendInfoPopup = Find<FriendInfoPopup>();
            if (avatarInfo.gameObject.activeSelf)
            {
                avatarInfo.Close();
            }
            else if(friendInfoPopup.gameObject.activeSelf)
            {
                friendInfoPopup.Close();
            }
            else
            {
                if (!CanClose)
                {
                    return;
                }

                Close(true);
                Game.Event.OnRoomEnter.Invoke(true);
            }
        }

        public void GoToStage(BattleLog log)
        {
            Game.Event.OnRankingBattleStart.Invoke(log);
            Close();
        }

        private async Task UpdateWeeklyCache(WeeklyArenaState state)
        {
            var agent = Game.Game.instance.Agent;
            var rawList = await agent.GetStateAsync(state.address.Derive("address_list"));
            if (rawList is List list)
            {
                var avatarAddressList = list.ToList(StateExtensions.ToAddress);
                var arenaInfoAddressList = new List<Address>();
                foreach (var avatarAddress in avatarAddressList)
                {
                    var arenaInfoAddress = state.address.Derive(avatarAddress.ToByteArray());
                    if (!arenaInfoAddressList.Contains(arenaInfoAddress))
                    {
                        arenaInfoAddressList.Add(arenaInfoAddress);
                    }
                }

                // Chunking list for reduce loading time.
                var chunks = arenaInfoAddressList
                    .Select((x, i) => new { Index = i, Value = x })
                    .GroupBy(x => x.Index / 1000)
                    .Select(x => x.Select(v => v.Value).ToList())
                    .ToList();
                Dictionary<Address, IValue> result = new Dictionary<Address, IValue>();
                for (var index = 0; index < chunks.Count; index++)
                {
                    var chunk = chunks[index];
                    var states = await Game.Game.instance.Agent.GetStateBulk(chunk);
                    result = result.Union(states)
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                }
                var infoList = new List<ArenaInfo>();
                foreach (var iValue in result.Values)
                {
                    if (iValue is Dictionary dictionary)
                    {
                        var info = new ArenaInfo(dictionary);
                        infoList.Add(info);
                    }
                }

                _arenaInfoList.Update(infoList);
            }

            // [TEN Code Block Start]
            var infos = _arenaInfoList.GetArenaInfos(1, 30);
            // [TEN Code Block End]
            if (States.Instance.CurrentAvatarState != null)
            {
                // [TEN Code Block Start]
                var upperRange = 90;
                var lowerRange = 10;
                var targetAddress = States.Instance.CurrentAvatarState.address;
                if (paginationState == 1)
                {
                    (int rank, ArenaInfo arenaInfo) wc = _weeklyCachedInfo[31];
                    targetAddress = wc.arenaInfo.AvatarAddress;
                    upperRange = 100;
                    lowerRange = 0;
                }
                else if (paginationState == 0)
                {
                    (int rank, ArenaInfo arenaInfo) wc = _weeklyCachedInfo[_weeklyCachedInfo.Count -1];
                    targetAddress = wc.arenaInfo.AvatarAddress;
                    lowerRange = 100;
                    upperRange = 0;
                }

                var infos2 = _arenaInfoList.GetArenaInfos(targetAddress, upperRange, lowerRange);
                // [TEN Code Block End]

                // Player does not play prev & this week arena.
                if (!infos2.Any() && _arenaInfoList.OrderedArenaInfos.Any())
                {
                    var address = _arenaInfoList.OrderedArenaInfos.Last().AvatarAddress;
                    infos2 = _arenaInfoList.GetArenaInfos(address, 90, 0);
                }

                var infos3 = _arenaInfoList.GetArenaInfos(States.Instance.CurrentAvatarState.address, 1, 1);

                infos.AddRange(infos2);
                infos.AddRange(infos3);
                infos = infos.ToImmutableHashSet().OrderBy(tuple => tuple.rank).ToList();
            }

            var addressList = infos.Select(i => i.arenaInfo.AvatarAddress).ToList();
            var avatarStates = await agent.GetAvatarStates(addressList);
            _weeklyCachedInfo = infos
                .Select(tuple =>
                {
                    var avatarAddress = tuple.arenaInfo.AvatarAddress;
                    if (!avatarStates.ContainsKey(avatarAddress))
                    {
                        return (0, null);
                    }

                    var avatarState = avatarStates[avatarAddress];

                    var arenaInfo = tuple.arenaInfo;
#pragma warning disable 618
                    arenaInfo.Level = avatarState.level;
                    arenaInfo.ArmorId = avatarState.GetArmorIdForPortrait();
                    arenaInfo.CombatPoint = avatarState.GetCP();
#pragma warning restore 618
                    return tuple;
                })
                .Select(t => t)
                .Where(tuple => tuple.rank > 0)
                .ToList();
        }
    }
}
