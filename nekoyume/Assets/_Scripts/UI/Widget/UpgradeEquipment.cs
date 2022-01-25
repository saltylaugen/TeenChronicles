using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nekoyume.BlockChain;
using Nekoyume.L10n;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.State;
using Nekoyume.UI.Module;
using UnityEngine;
using System.Numerics;
using Nekoyume.Action;
using Nekoyume.EnumType;
using Nekoyume.Extensions;
using Nekoyume.Game.Controller;
using Nekoyume.Helper;
using Nekoyume.Model.Stat;
using Nekoyume.TableData;
using Nekoyume.UI.Model;
using TMPro;
using UnityEngine.UI;
using EquipmentInventory = Nekoyume.UI.Module.EquipmentInventory;

namespace Nekoyume.UI
{
    using Nekoyume.UI.Scroller;
    using UniRx;

    public class UpgradeEquipment : Widget
    {
        [SerializeField]
        private EquipmentInventory inventory;

        [SerializeField]
        private ConditionalCostButton upgradeButton;

        [SerializeField]
        private Button closeButton;

        [SerializeField]
        private UpgradeEquipmentSlot baseSlot;

        [SerializeField]
        private UpgradeEquipmentSlot materialSlot;

        [SerializeField]
        private TextMeshProUGUI successRatioText;

        [SerializeField]
        private TextMeshProUGUI requiredBlockIndexText;

        [SerializeField]
        private TextMeshProUGUI itemNameText;

        [SerializeField]
        private TextMeshProUGUI currentLevelText;

        [SerializeField]
        private TextMeshProUGUI nextLevelText;

        [SerializeField]
        private TextMeshProUGUI materialGuideText;

        [SerializeField]
        private EnhancementOptionView mainStatView;

        [SerializeField]
        private List<EnhancementOptionView> statViews;

        [SerializeField]
        private List<EnhancementOptionView> skillViews;

        [SerializeField]
        private GameObject noneContainer;

        [SerializeField]
        private GameObject itemInformationContainer;

        [SerializeField]
        private GameObject blockInformationContainer;

        [SerializeField]
        private GameObject buttonDisabled;

        private Animator _animator;
        private EnhancementCostSheetV2 _costSheet;
        private Equipment _baseItem;
        private Equipment _materialItem;
        private BigInteger _costNcg = 0;
        private string errorMessage;

        private enum State
        {
            Show,
            Empty,
            RegisterBase,
            RegisterMaterial,
            UnregisterMaterial,
            Close,
        }

        private static readonly int HashToShow = Animator.StringToHash("Show");
        private static readonly int HashToRegisterBase = Animator.StringToHash("RegisterBase");
        private static readonly int HashToPostRegisterBase = Animator.StringToHash("PostRegisterBase");
        private static readonly int HashToPostRegisterMaterial = Animator.StringToHash("PostRegisterMaterial");
        private static readonly int HashToUnregisterMaterial = Animator.StringToHash("UnregisterMaterial");
        private static readonly int HashToClose = Animator.StringToHash("Close");

        private State _state = State.Empty;

        protected override void Awake()
        {
            base.Awake();
            upgradeButton.OnSubmitSubject
                .Subscribe(_ => Action())
                .AddTo(gameObject);
            closeButton.onClick.AddListener(Close);
            CloseWidget = Close;
        }

        public override void Initialize()
        {
            base.Initialize();
            inventory.SharedModel.SelectedItemView.Subscribe(SubscribeSelectItem).AddTo(gameObject);
            inventory.SharedModel.DeselectItemView();
            inventory.SharedModel.State.Value = ItemSubType.Weapon;
            inventory.SharedModel.DimmedFunc.Value = DimFunc;
            inventory.SharedModel.EffectEnabledFunc.Value = Contains;

            _costSheet = Game.Game.instance.TableSheets.EnhancementCostSheetV2;
            _animator = GetComponent<Animator>();

            baseSlot.RemoveButton.onClick.AddListener(() =>
            {
                materialSlot.RemoveButton.onClick.Invoke();
                UpdateState(State.Empty);
            });

            materialSlot.RemoveButton.onClick.AddListener(() =>
            {
                UpdateState(State.UnregisterMaterial);
            });
        }

        private void UpdateState(State state)
        {
            switch (state)
            {
                case State.Show:
                case State.Empty:
                    inventory.ClearItemState(_baseItem);
                    baseSlot.RemoveMaterial();
                    _baseItem = null;
                    inventory.ClearItemState(_materialItem);
                    materialSlot.RemoveMaterial();
                    _materialItem = null;
                    ClearInformation();
                    SetActiveContainer(true);
                    materialGuideText.text = string.Empty;
                    _animator.Play(state == State.Show ? HashToShow : HashToRegisterBase);
                    break;

                case State.RegisterBase:
                case State.UnregisterMaterial:
                    inventory.ClearItemState(_materialItem);
                    materialSlot.RemoveMaterial();
                    _materialItem = null;
                    SetActiveContainer(false);
                    materialGuideText.text = L10nManager.Localize("UI_SELECT_MATERIAL_TO_UPGRADE");
                    if (ItemEnhancement.TryGetRow(_baseItem, _costSheet, out var row))
                    {
                        _costNcg = row.Cost;
                        UpdateInformation(row, _baseItem);
                    }
                    _animator.Play(state == State.RegisterBase ? HashToPostRegisterBase : HashToUnregisterMaterial);
                    break;

                case State.RegisterMaterial:
                    SetActiveContainer(false);
                    materialGuideText.text = L10nManager.Localize("UI_UPGRADE_GUIDE");
                    _animator.Play(HashToPostRegisterMaterial, -1, 0);
                    break;

                case State.Close:
                    _animator.Play(HashToClose);
                    break;
            }

            buttonDisabled.SetActive(!IsInteractableButton(_baseItem, _materialItem));
            inventory.SharedModel.UpdateDimAndEffectAll();
        }

        public override void Show(bool ignoreShowAnimation = false)
        {
            base.Show(ignoreShowAnimation);
            UpdateState(State.Show);
            HelpTooltip.HelpMe(100017, true);
        }

        private void Close()
        {
            UpdateState(State.Close);
            Close(true);
            Find<CombinationMain>().Show();
        }

        private bool DimFunc(InventoryItem inventoryItem)
        {
            if (_baseItem is null && _materialItem is null)
                return false;

            var selectedItem = (Equipment)inventoryItem.ItemBase.Value;

            if (!(_baseItem is null))
            {
                if (CheckDim(selectedItem, _baseItem))
                {
                    return true;
                }
            }

            if (!(_materialItem is null))
            {
                if (CheckDim(selectedItem, _materialItem))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CheckDim(Equipment selectedItem, Equipment slotItem)
        {
            if (selectedItem.ItemId.Equals(slotItem.ItemId))
            {
                return true;
            }

            if (selectedItem.ItemSubType != slotItem.ItemSubType)
            {
                return true;
            }

            if (selectedItem.Grade != slotItem.Grade)
            {
                return true;
            }

            if (selectedItem.level != slotItem.level)
            {
                return true;
            }

            return false;
        }

        private bool Contains(InventoryItem inventoryItem)
        {
            var selectedItem = (Equipment)inventoryItem.ItemBase.Value;

            if (!(_baseItem is null))
            {
                if (selectedItem.ItemId.Equals(_baseItem.ItemId))
                {
                    return true;
                }
            }

            if (!(_materialItem is null))
            {
                if (selectedItem.ItemId.Equals(_materialItem.ItemId))
                {
                    return true;
                }
            }

            return false;
        }

        private void Action()
        {
            if (!IsInteractableButton(_baseItem, _materialItem))
            {
                NotificationSystem.Push(MailType.System, errorMessage, NotificationCell.NotificationType.Alert);
                return;
            }

            if (States.Instance.GoldBalanceState.Gold.MajorUnit < _costNcg)
            {
                errorMessage = L10nManager.Localize("UI_NOT_ENOUGH_NCG");
                NotificationSystem.Push(MailType.System, errorMessage, NotificationCell.NotificationType.Alert);
                return;
            }

            var slots = Find<CombinationSlotsPopup>();
            if (!slots.TryGetEmptyCombinationSlot(out var slotIndex))
            {
                return;
            }

            var sheet = Game.Game.instance.TableSheets.EnhancementCostSheetV2;
            if (ItemEnhancement.TryGetRow(_baseItem, sheet, out var row))
            {
                slots.SetCaching(slotIndex, true, row.SuccessRequiredBlockIndex, itemUsable:_baseItem);
            }

            NotificationSystem.Push(MailType.Workshop,
                L10nManager.Localize("NOTIFICATION_ITEM_ENHANCEMENT_START"),
                NotificationCell.NotificationType.Information);

            Game.Game.instance.ActionManager.ItemEnhancement(_baseItem, _materialItem, slotIndex, _costNcg).Subscribe();

            StartCoroutine(CoCombineNPCAnimation(_baseItem, row.SuccessRequiredBlockIndex, () => UpdateState(State.Empty)));
        }

        private IEnumerator CoCombineNPCAnimation(ItemBase itemBase,
            long blockIndex,
            System.Action action,
            bool isConsumable = false)
        {
            var loadingScreen = Find<CombinationLoadingScreen>();
            loadingScreen.Show();
            loadingScreen.SetItemMaterial(new Item(itemBase), isConsumable);
            loadingScreen.SetCloseAction(action);
            Push();
            yield return new WaitForSeconds(.5f);

            var format = L10nManager.Localize("UI_COST_BLOCK");
            var quote = string.Format(format, blockIndex);
            loadingScreen.AnimateNPC(itemBase.ItemType, quote);
        }

        private void SubscribeSelectItem(BigInventoryItemView view)
        {
            var tooltip = Find<ItemInformationTooltip>();
            if (view is null || view.RectTransform == tooltip.Target)
            {
                tooltip.Close();
                return;
            }

            if (view.Model is null)
            {
                return;
            }

            var equipment = view.Model.ItemBase.Value as Equipment;
            if (equipment.ItemId == _baseItem?.ItemId)
            {
                baseSlot.RemoveButton.onClick.Invoke();
            }
            else if (equipment.ItemId == _materialItem?.ItemId)
            {
                materialSlot.RemoveButton.onClick.Invoke();
            }
            else
            {
                tooltip.Show(view.RectTransform, view.Model,
                    value => IsEnableSubmit(view),
                    GetSubmitText(view),
                    _ => OnSubmit(view),
                    _ => { inventory.SharedModel.DeselectItemView(); });
            }
        }

        private bool IsEnableSubmit(BigInventoryItemView view)
        {
            if (view.Model.Dimmed.Value)
            {
                var equipment = view.Model.ItemBase.Value as Equipment;
                if (equipment.ItemId != _baseItem?.ItemId && equipment.ItemId != _materialItem?.ItemId)
                {
                    return false;
                }
            }
            return true;
        }

        private string GetSubmitText(BigInventoryItemView view)
        {
            if (view.Model.Dimmed.Value)
            {
                var equipment = view.Model.ItemBase.Value as Equipment;
                if (equipment.ItemId != _baseItem?.ItemId && equipment.ItemId != _materialItem?.ItemId)
                {
                    return L10nManager.Localize("UI_COMBINATION_REGISTER_MATERIAL");
                }

                return L10nManager.Localize("UI_COMBINATION_UNREGISTER_MATERIAL");
            }

            return _baseItem is null
                ? L10nManager.Localize("UI_COMBINATION_REGISTER_ITEM")
                : L10nManager.Localize("UI_COMBINATION_REGISTER_MATERIAL");
        }

        private void OnSubmit(BigInventoryItemView view)
        {
            if (view.Model.Dimmed.Value)
            {
                return;
            }

            if (_baseItem is null)
            {
                _baseItem = (Equipment)view.Model.ItemBase.Value;
                baseSlot.AddMaterial(view.Model.ItemBase.Value);
                view.Select(true);
                UpdateState(State.RegisterBase);
            }
            else
            {
                if(!(_materialItem is null))
                {
                    inventory.ClearItemState(_materialItem);
                }

                _materialItem = (Equipment)view.Model.ItemBase.Value;
                materialSlot.AddMaterial(view.Model.ItemBase.Value);
                view.Select(false);
                UpdateState(State.RegisterMaterial);
            }

            AudioController.instance.PlaySfx(AudioController.SfxCode.ChainMail2);
        }

        private void SetActiveContainer(bool isClear)
        {
            noneContainer.SetActive(isClear);
            itemInformationContainer.SetActive(!isClear);
            blockInformationContainer.SetActive(!isClear);
        }

        private void ClearInformation()
        {
            itemNameText.text = string.Empty;
            currentLevelText.text = string.Empty;
            nextLevelText.text = string.Empty;
            successRatioText.text = "0%";
            requiredBlockIndexText.text = "0";
            buttonDisabled.SetActive(true);

            mainStatView.gameObject.SetActive(false);
            foreach (var stat in statViews)
            {
                stat.gameObject.SetActive(false);
            }

            foreach (var skill in skillViews)
            {
                skill.gameObject.SetActive(false);
            }
        }

        private void UpdateInformation(EnhancementCostSheetV2.Row row, Equipment equipment)
        {
            ClearInformation();
            upgradeButton.SetCost(ConditionalCostButton.CostType.NCG, (int) row.Cost);
            var slots = Find<CombinationSlotsPopup>();
            upgradeButton.Interactable = slots.TryGetEmptyCombinationSlot(out var _);

            itemNameText.text = equipment.GetLocalizedName();
            currentLevelText.text = $"+{equipment.level}";
            nextLevelText.text = $"+{equipment.level + 1}";
            successRatioText.text =
                ((row.GreatSuccessRatio + row.SuccessRatio).NormalizeFromTenThousandths())
                .ToString("P0");
            requiredBlockIndexText.text = $"{row.SuccessRequiredBlockIndex} +";

            var itemOptionInfo = new ItemOptionInfo(equipment);

            if (row.BaseStatGrowthMin != 0 && row.BaseStatGrowthMax != 0)
            {
                var (mainStatType, mainValue, _) = itemOptionInfo.MainStat;
                var mainAdd = Math.Max(1, (int)(mainValue * row.BaseStatGrowthMax.NormalizeFromTenThousandths()));
                mainStatView.gameObject.SetActive(true);
                mainStatView.Set(mainStatType.ToString(),
                    mainStatType.ValueToString(mainValue),
                    $"(<size=80%>max</size> +{mainStatType.ValueToString(mainAdd)})");
            }

            var stats = itemOptionInfo.StatOptions;
            for (var i = 0; i < stats.Count; i++)
            {
                statViews[i].gameObject.SetActive(true);
                var statType = stats[i].type;
                var statValue = stats[i].value;
                var count = stats[i].count;

                if (row.ExtraStatGrowthMin == 0 && row.ExtraStatGrowthMax == 0)
                {
                    statViews[i].Set(statType.ToString(),
                        statType.ValueToString(statValue),
                        string.Empty,
                        count);
                }
                else
                {
                    var statAdd = Math.Max(1, (int)(statValue * row.ExtraStatGrowthMax.NormalizeFromTenThousandths()));
                    statViews[i].Set(statType.ToString(),
                        statType.ValueToString(statValue),
                        $"(<size=80%>max</size> +{statType.ValueToString(statAdd)})",
                        count);
                }
            }

            var skills = itemOptionInfo.SkillOptions;
            for (var i = 0; i < skills.Count; i++)
            {
                skillViews[i].gameObject.SetActive(true);
                var skillName = skills[i].name;
                var power = skills[i].power;
                var chance = skills[i].chance;

                if (row.ExtraSkillDamageGrowthMin == 0 && row.ExtraSkillDamageGrowthMax == 0 &&
                    row.ExtraSkillChanceGrowthMin == 0 && row.ExtraSkillChanceGrowthMax == 0)
                {
                    skillViews[i].Set(skillName,
                        $"{L10nManager.Localize("UI_SKILL_POWER")} : {power}",
                        string.Empty,
                        $"{L10nManager.Localize("UI_SKILL_CHANCE")} : {chance}",
                        string.Empty);
                }
                else
                {
                    var powerAdd = Math.Max(1, (int)(power * row.ExtraSkillDamageGrowthMax.NormalizeFromTenThousandths()));
                    var chanceAdd = Math.Max(1, (int)(chance * row.ExtraSkillChanceGrowthMax.NormalizeFromTenThousandths()));

                    skillViews[i].Set(skillName,
                        $"{L10nManager.Localize("UI_SKILL_POWER")} : {power}",
                        $"(<size=80%>max</size> +{powerAdd})",
                        $"{L10nManager.Localize("UI_SKILL_CHANCE")} : {chance}",
                        $"(<size=80%>max</size> +{chanceAdd}%)");
                }
            }
        }

        private bool IsInteractableButton(IItem item, IItem material)
        {
            if (item is null || material is null)
            {
                errorMessage = L10nManager.Localize("UI_SELECT_MATERIAL_TO_UPGRADE");
                return false;
            }

            if (States.Instance.CurrentAvatarState.actionPoint < GameConfig.EnhanceEquipmentCostAP)
            {
                errorMessage = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_ACTION_POWER");
                return false;
            }

            if (!Find<CombinationSlotsPopup>().TryGetEmptyCombinationSlot(out _))
            {
                errorMessage = L10nManager.Localize("NOTIFICATION_NOT_ENOUGH_SLOTS");
                return false;
            }

            return true;
        }

        private static Color GetNcgColor(BigInteger cost)
        {
            return States.Instance.GoldBalanceState.Gold.MajorUnit < cost
                ? Palette.GetColor(ColorType.TextDenial)
                : Color.white;
        }
    }
}

