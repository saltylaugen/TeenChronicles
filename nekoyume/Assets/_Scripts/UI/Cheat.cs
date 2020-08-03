using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Bencodex.Types;
using Libplanet.Action;
using Nekoyume.Battle;
using Nekoyume.BlockChain;
using Nekoyume.EnumType;
using Nekoyume.Model.BattleStatus;
using Nekoyume.Model.Item;
using Nekoyume.Model.Skill;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.TableData;
using Nekoyume.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Enemy = Nekoyume.Game.Character.Enemy;
using Text = UnityEngine.UI.Text;

namespace Nekoyume
{
    public class Cheat : Widget
    {
        private static Cheat Instance;

        public TextMeshProUGUI Logs;
        public Transform Peers;
        public Transform StagedTxs;
        public Transform Blocks;
        public Button BtnOpen;
        public Button buttonBase;
        public ScrollRect list;
        public ScrollRect skillList;
        public HorizontalLayoutGroup skillPanel;
        public Dropdown TableSheetsDropdown;
        public Transform OnChainTableSheet;
        public Transform LocalTableSheet;
        public GameObject PatchButton;
        public GameObject[] Views;

        private Dictionary<string, string> TableAssets;

        private int _viewIndex;
        private Transform _modal;
        private float _updateTime = 0.0f;
        private StringBuilder _logString = new StringBuilder();
        private BattleLog.Result _result;
        private int[,] _stageRange;
        private Model.Skill.Skill[] _skills;
        private Model.Skill.Skill _selectedSkill;
        public override WidgetType WidgetType => WidgetType.Development;

        public class DebugRandom : IRandom
        {
            private readonly System.Random _random = new System.Random();

            public int Next()
            {
                return _random.Next();
            }

            public int Next(int maxValue)
            {
                return _random.Next(maxValue);
            }

            public int Next(int minValue, int maxValue)
            {
                return _random.Next(minValue, maxValue);
            }

            public void NextBytes(byte[] buffer)
            {
                _random.NextBytes(buffer);
            }

            public double NextDouble()
            {
                return _random.NextDouble();
            }
        }

        public static void Display(string target, string text)
        {
            switch (target)
            {
                case "Logs":
                    Instance.Logs.text = text;
                    break;
                case "Peers":
                    Instance.Peers.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text = text;
                    Instance.Refresh(Instance.Peers);
                    break;
                case "StagedTxs":
                    Instance.StagedTxs.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text = text;
                    Instance.Refresh(Instance.StagedTxs);
                    break;
                case nameof(Blocks):
                    Instance.Blocks.Find("TextRect/InputField").GetComponent<InputField>().text = text;
                    break;
                case nameof(OnChainTableSheet):
                    Instance.OnChainTableSheet.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text = text;
                    Instance.Refresh(Instance.OnChainTableSheet);
                    break;
                case nameof(LocalTableSheet):
                    Instance.LocalTableSheet.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text = text;
                    Instance.Refresh(Instance.LocalTableSheet);
                    break;
            }
        }

        public void RefreshTableSheets()
        {
            var tableName = TableSheetsDropdown.options.Count == 0 ? string.Empty : GetTableName();
            IImmutableDictionary<string, string> tableSheets = GetCurrentTableSheetState().TableSheets;
            if (tableSheets.TryGetValue(tableName, out string onChainTableCsv))
            {
                Display(nameof(OnChainTableSheet), onChainTableCsv);
            }
            else
            {
                Display(nameof(OnChainTableSheet), "No content.");
            }

            if (TableAssets.TryGetValue(tableName, out string localTableCsv))
            {
                Display(nameof(LocalTableSheet), localTableCsv);
            }
            else
            {
                Display(nameof(LocalTableSheet), "No content.");
            }

            PatchButton.SetActive(onChainTableCsv != localTableCsv);
        }

        public static void Log(string text)
        {
            Instance._logString.Insert(0, $"> {text}\n");
            Instance.Logs.text += Instance._logString.ToString();
        }

        private void Refresh(Transform target)
        {
            // 마스크에 짤려서 사이즈 조정
            var delta = target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight -
                        target.GetComponent<RectTransform>().rect.height;
            target.Find("TextRect/Text").GetComponent<RectTransform>().sizeDelta =
                new Vector2(0, delta < 0 ? 0 : delta);

            // 스크롤바 조정
            if (delta < 0)
            {
                target.Find("Scrollbar").gameObject.SetActive(false);
                ScrollBarHandler(target, 0);
            }
            else
            {
                target.Find("Scrollbar").gameObject.SetActive(true);
                target.Find("Scrollbar").GetComponent<Scrollbar>().size =
                    target.GetComponent<RectTransform>().rect.height /
                    target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight;
            }
        }

        private void ScrollBarHandler(Transform target, float location)
        {
            var delta = target.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().preferredHeight -
                        target.GetComponent<RectTransform>().rect.height;
            target.Find("TextRect/Text").GetComponent<RectTransform>().anchoredPosition =
                delta > 0 ? new Vector2(0, delta * (location - 1)) : new Vector2(0, 0);
        }

        public void Patch()
        {
            var tableName = GetTableName();
            var csv = LocalTableSheet.Find("TextRect/Text").GetComponent<TextMeshProUGUI>().text;
            Game.Game.instance.ActionManager.PatchTableSheet(tableName, csv);
        }

        private string GetTableName()
        {
            return TableSheetsDropdown.options[TableSheetsDropdown.value].text;
        }

        protected override void Awake()
        {
            base.Awake();

            Instance = this;
            _modal = transform.Find("Modal");
            _modal.gameObject.SetActive(false);
#if DEBUG
#else
            Transform btn = transform.Find("Btn");
            btn.gameObject.SetActive(false);
#endif

            CloseWidget = null;
        }

        protected override void Update()
        {
            _updateTime += Time.deltaTime;
        }

        public override void Show(bool ignoreShowAnimation = false)
        {
            _modal.gameObject.SetActive(true);

            Peers.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(Peers, location));
            StagedTxs.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(StagedTxs, location));
            Blocks.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(Blocks, location));
            OnChainTableSheet.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(OnChainTableSheet, location));
            LocalTableSheet.Find("Scrollbar")
                .GetComponent<Scrollbar>()
                .onValueChanged
                .AddListener((location) => ScrollBarHandler(LocalTableSheet, location));
            Refresh(OnChainTableSheet);
            Refresh(LocalTableSheet);
            Refresh(Peers);
            Refresh(StagedTxs);
            ScrollBarHandler(Peers, 0);
            ScrollBarHandler(StagedTxs, 0);
            ScrollBarHandler(OnChainTableSheet, 0);
            ScrollBarHandler(LocalTableSheet, 0);

            BtnOpen.gameObject.SetActive(false);
            foreach (var i in Enumerable.Range(1, Game.Game.instance.TableSheets.StageWaveSheet.Count))
            {
                Button newButton = Instantiate(buttonBase, list.content);
                newButton.GetComponentInChildren<Text>().text = i.ToString();
                newButton.onClick.AddListener(() => DummyBattle(i));
                newButton.gameObject.SetActive(true);
            }

            var skills = new List<Model.Skill.Skill>();
            foreach (var skillRow in Game.Game.instance.TableSheets.SkillSheet)
            {
                var skill = SkillFactory.Get(skillRow, 50, 100);
                skills.Add(skill);
                Button newButton = Instantiate(buttonBase, skillList.content);
                newButton.GetComponentInChildren<Text>().text =
                    $"{skillRow.GetLocalizedName()}_{skillRow.ElementalType}";
                newButton.onClick.AddListener(() => SelectSkill(skill));
                newButton.gameObject.SetActive(true);
            }

            _skills = skills.ToArray();

            TableAssets = GetTableAssetsHavingDifference();
            TableSheetsDropdown.options = TableAssets.Keys.Select(s => new Dropdown.OptionData(s)).ToList();
            if (TableSheetsDropdown.options.Count == 0)
            {
                Debug.Log("It seems there is no table having difference.");
                Display(nameof(OnChainTableSheet), "No content.");
                Display(nameof(LocalTableSheet), "No content.");
                PatchButton.SetActive(false);
            }
            else
            {
                RefreshTableSheets();
            }

            base.Show(ignoreShowAnimation);
        }

        private static Dictionary<string, string> GetTableAssetsHavingDifference()
        {
            var tableCsvAssets = Game.Game.GetTableCsvAssets();
            var tableSheetsState = GetCurrentTableSheetState();
            return tableCsvAssets.Where(pair =>
                !tableSheetsState.TableSheets.TryGetValue(pair.Key, out string onChainCsv) ||
                onChainCsv != pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);
        }


        private static TableSheetsState GetCurrentTableSheetState()
        {
            return new TableSheetsState(
                (Dictionary) Game.Game.instance.Agent.GetState(TableSheetsState.Address)
            );
        }

        public override void Close(bool ignoreCloseAnimation = false)
        {
            Peers.Find("Scrollbar").GetComponent<Scrollbar>().onValueChanged.RemoveAllListeners();
            StagedTxs.Find("Scrollbar").GetComponent<Scrollbar>().onValueChanged.RemoveAllListeners();
            foreach (Transform child in list.content.transform)
            {
                Destroy(child.gameObject);
            }

            list.gameObject.SetActive(false);
            skillPanel.gameObject.SetActive(false);

            _modal.gameObject.SetActive(false);
            BtnOpen.gameObject.SetActive(true);
        }

        public override bool IsActive()
        {
            return _modal.gameObject.activeSelf;
        }

        public void SwitchView()
        {
            Views[_viewIndex].SetActive(false);
            _viewIndex = (_viewIndex + 1) % Views.Length;
            Views[_viewIndex].SetActive(true);
        }

        public void HandleClick(GameObject sender)
        {
#if DEBUG
            Invoke(sender.name, 0.0f);
#endif
        }

        private void LevelUp()
        {
            GameObject enemyObj = GameObject.Find("Enemy");
            if (enemyObj == null)
            {
                Log("Need Enemy.");
                return;
            }

            GameObject playerObj = GameObject.Find("Player");
            if (playerObj != null)
            {
                var player = playerObj.GetComponent<Game.Character.Player>();
                player.Level += 1;
                Log($"Level Up to {player.Level}");
            }

            var enemy = enemyObj.GetComponent<Enemy>();
            Game.Event.OnEnemyDeadStart.Invoke(enemy);
        }

        private void SpeedUp()
        {
            Time.timeScale = 2.0f;
            Log($"Speed Up to {Time.timeScale}");
        }

        private void DummyBattleWin()
        {
            _result = BattleLog.Result.Win;
            list.gameObject.SetActive(true);
        }

        private void DummyBattleLose()
        {
            _result = BattleLog.Result.Lose;
            list.gameObject.SetActive(true);
        }

        private void DummyBattle(int stageId)
        {
            Find<BattleResult>()?.Close();
            Find<Menu>()?.Close();
            Find<Menu>()?.ShowWorld();

            if (!Game.Game.instance.TableSheets.WorldSheet.TryGetByStageId(stageId, out var worldRow))
                throw new KeyNotFoundException($"WorldSheet.TryGetByStageId() {nameof(stageId)}({stageId})");

            var simulator = new StageSimulator(
                new DebugRandom(),
                States.Instance.CurrentAvatarState,
                new List<Guid>(),
                worldRow.Id,
                stageId,
                Game.Game.instance.TableSheets,
                _selectedSkill
            );
            simulator.Simulate();
            simulator.Log.result = _result;

            var stage = Game.Game.instance.Stage;
            stage.PlayStage(simulator.Log);

            Close();
        }

        private void DummySkill()
        {
            skillPanel.gameObject.SetActive(true);
        }

        private void ExportState()
        {
            var avatarStates = new Bencodex.Types.Dictionary(
                States.Instance.AvatarStates.Select(kv =>
                    new KeyValuePair<IKey, IValue>(
                        new Binary(BitConverter.GetBytes(kv.Key)),
                        kv.Value.Serialize()
                    )
                )
            );
            var states = Bencodex.Types.Dictionary.Empty
                .Add("AgentState", States.Instance.AgentState.Serialize())
                .Add("AvatarStates", avatarStates)
                .Add("RankingState", States.Instance.RankingState.Serialize())
                .Add("ShopState", States.Instance.ShopState.Serialize())
                .Add("TableSheetsState", GetCurrentTableSheetState().Serialize());
            var codec = new Bencodex.Codec();
            var path = Path.Combine(
                Application.persistentDataPath,
                $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.states");
            using (FileStream stream = File.Create(path))
            {
                codec.Encode(states, stream);
            }
        }

        private void SelectSkill(Model.Skill.Skill skill)
        {
            _selectedSkill = skill;
            DummyBattle(1);
        }
    }
}
