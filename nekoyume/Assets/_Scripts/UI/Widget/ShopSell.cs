using System;
using System.Globalization;
using Libplanet.Assets;
using mixpanel;
using Nekoyume.Action;
using Nekoyume.EnumType;
using Nekoyume.Game.Character;
using Nekoyume.Game.Controller;
using Nekoyume.Helper;
using Nekoyume.L10n;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.Model.State;
using Nekoyume.State;
using Nekoyume.UI.Model;
using Nekoyume.UI.Module;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using ShopItem = Nekoyume.UI.Model.ShopItem;

namespace Nekoyume.UI
{
    using Nekoyume.UI.Scroller;
    using UniRx;

    public class ShopSell : Widget
    {
        private enum PriorityType
        {
            Price,
            Count,
        }

        [SerializeField] private Module.Inventory inventory = null;
        [SerializeField] private Module.ShopSellItems shopItems = null;
        [SerializeField] private TextMeshProUGUI noticeText = null;
        [SerializeField] private SpeechBubble speechBubble = null;
        [SerializeField] private Button buyButton = null;
        [SerializeField] private Button closeButton = null;
        [SerializeField] private Button spineButton = null;

        private NPC _npc;
        private static readonly Vector3 NPCPosition = new Vector3(1000.2f, 998, 1.7f);
        private const int NPCId = 300000;
        private const int LimitPrice  = 100000000;

        private Shop SharedModel { get; set; }

        protected override void Awake()
        {
            base.Awake();
            SharedModel = new Shop();
            noticeText.text = L10nManager.Localize("UI_SHOP_NOTICE");
            CloseWidget = null;

            buyButton.onClick.AddListener(() =>
            {
                speechBubble.gameObject.SetActive(false);
                Find<TwoButtonSystem>().Close();
                Find<ItemCountableAndPricePopup>().Close();
                Find<ShopBuy>().gameObject.SetActive(true);
                Find<ShopBuy>().Open();
                _npc?.gameObject.SetActive(false);
                gameObject.SetActive(false);
            });

            closeButton.onClick.AddListener(() =>
            {
                Close(true);
                Game.Event.OnRoomEnter.Invoke(true);
            });

            spineButton.onClick.AddListener(() => _npc.PlayAnimation(NPCAnimation.Type.Emotion));

            CloseWidget = () =>
            {
                Close(true);
                Game.Event.OnRoomEnter.Invoke(true);
            };
        }

        public override void Initialize()
        {
            base.Initialize();

            // inventory
            inventory.SharedModel.SelectedItemView
                .Subscribe(ShowTooltip)
                .AddTo(gameObject);

            shopItems.SharedModel.SelectedItemView
                .Subscribe(ShowTooltip)
                .AddTo(gameObject);

            // sell
            SharedModel.ItemCountableAndPricePopup.Value.Item
                .Subscribe(SubscribeSellPopup)
                .AddTo(gameObject);
            SharedModel.ItemCountableAndPricePopup.Value.OnClickSubmit
                .Subscribe(SubscribeSellPopupSubmit)
                .AddTo(gameObject);
            SharedModel.ItemCountableAndPricePopup.Value.OnClickReregister
                .Subscribe(SubscribeSellPopupUpdateSell)
                .AddTo(gameObject);
            SharedModel.ItemCountableAndPricePopup.Value.OnClickCancel
                .Subscribe(SubscribeSellPopupCancel)
                .AddTo(gameObject);
            SharedModel.ItemCountableAndPricePopup.Value.OnChangeCount
                .Subscribe(SubscribeSellPopupCount)
                .AddTo(gameObject);
            SharedModel.ItemCountableAndPricePopup.Value.OnChangePrice
                .Subscribe(SubscribeSellPopupPrice)
                .AddTo(gameObject);

            // sell cancellation
            SharedModel.ItemCountAndPricePopup.Value.Item
                .Subscribe(SubscribeSellCancellationPopup)
                .AddTo(gameObject);
        }

        public void Show()
        {
            base.Show();
            ShowNPC();
            Refresh(true);
            AudioController.instance.PlayMusic(AudioController.MusicCode.Shop);
        }

        public void Refresh(bool isResetType = false)
        {
            ReactiveShopState.InitAndUpdateSellDigests();
            shopItems.Show();
            if (isResetType)
            {
                inventory.SharedModel.State.Value = ItemType.Equipment;
            }
            inventory.SharedModel.ActiveFunc.SetValueAndForceNotify(inventoryItem => (inventoryItem.ItemBase.Value is ITradableItem));
        }

        public override void Close(bool ignoreCloseAnimation = false)
        {
            shopItems.Close();
            Find<TwoButtonSystem>().Close();
            Find<ItemCountableAndPricePopup>().Close();
            speechBubble.gameObject.SetActive(false);
            Find<ShopBuy>().Close();
            _npc?.gameObject.SetActive(false);
            base.Close(ignoreCloseAnimation);
        }

        private void ShowNPC()
        {
            var go = Game.Game.instance.Stage.npcFactory.Create(
                NPCId,
                NPCPosition,
                LayerType.InGameBackground,
                3);
            _npc = go.GetComponent<NPC>();
            _npc.GetComponent<SortingGroup>().sortingLayerName = LayerType.InGameBackground.ToLayerName();
            _npc.GetComponent<SortingGroup>().sortingOrder = 3;
            _npc.SpineController.Appear();
            go.SetActive(true);

            ShowSpeech("SPEECH_SHOP_GREETING_", CharacterAnimation.Type.Greeting);
        }

        private void ShowTooltip(InventoryItemView view)
        {
            var tooltip = Find<ItemInformationTooltip>();

            shopItems.SharedModel.DeselectItemView();

            if (view is null ||
                view.RectTransform == tooltip.Target)
            {
                tooltip.Close();
                return;
            }

            ShowSpeech("SPEECH_SHOP_REGISTER_ITEM_");
            tooltip.Show(
                view.RectTransform,
                view.Model,
                value => !DimmedFuncForSell(value.ItemBase.Value),
                L10nManager.Localize("UI_SELL"),
                _ => ShowSellPopup(tooltip.itemInformation.Model.item.Value as InventoryItem),
                _ => inventory.SharedModel.DeselectItemView());
        }

        private void ShowTooltip(ShopItemView view)
        {
            var tooltip = Find<ItemInformationTooltip>();
            inventory.SharedModel.DeselectItemView();

            if (view is null || view.RectTransform == tooltip.Target)
            {
                tooltip.Close();
                return;
            }

            tooltip.ShowForSell(
                view.RectTransform,
                view.Model,
                ButtonEnabledFuncForSell,
                L10nManager.Localize("UI_RETRIEVE"),
                _ => ShowUpdateSellPopup(tooltip.itemInformation.Model.item.Value as ShopItem),
                _ => ShowRetrievePopup(tooltip.itemInformation.Model.item.Value as ShopItem),
                _ => shopItems.SharedModel.DeselectItemView());
        }

        private void ShowUpdateSellPopup(ShopItem shopItem)
        {
            if (shopItem is null || shopItem.Dimmed.Value)
            {
                return;
            }

            var data = SharedModel.ItemCountableAndPricePopup.Value;

            if (decimal.TryParse(shopItem.Price.Value.GetQuantityString(),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var totalPrice))
            {
                var price = totalPrice / shopItem.Count.Value;
                var majorUnit = (int) price;
                var minorUnit = (int)((price - majorUnit) * 100);
                var currency = States.Instance.GoldBalanceState.Gold.Currency;
                data.Price.Value = new FungibleAssetValue(currency, majorUnit, minorUnit);
            }
            data.PreTotalPrice.Value = shopItem.Price.Value;
            data.TotalPrice.Value = shopItem.Price.Value;
            data.Count.Value = shopItem.Count.Value;
            data.IsSell.Value = false;

            data.TitleText.Value = shopItem.ItemBase.Value.GetLocalizedName();
            data.InfoText.Value = string.Empty;
            data.CountEnabled.Value = true;
            data.Submittable.Value = !DimmedFuncForSell(shopItem.ItemBase.Value);
            data.Item.Value = new CountEditableItem(shopItem.ItemBase.Value,
                shopItem.Count.Value,
                shopItem.Count.Value,
                shopItem.Count.Value);
            data.Item.Value.CountEnabled.Value = false;
        }

        private void ShowSellPopup(InventoryItem inventoryItem)
        {
            if (inventoryItem is null || inventoryItem.Dimmed.Value)
            {
                return;
            }

            var data = SharedModel.ItemCountableAndPricePopup.Value;
            var currency = States.Instance.GoldBalanceState.Gold.Currency;
            data.TotalPrice.Value = new FungibleAssetValue(currency, Shop.MinimumPrice, 0);
            data.Price.Value = new FungibleAssetValue(currency, Shop.MinimumPrice, 0);
            data.Count.Value = 1;
            data.IsSell.Value = true;

            data.TitleText.Value = inventoryItem.ItemBase.Value.GetLocalizedName();
            data.InfoText.Value = string.Empty;
            data.CountEnabled.Value = true;
            data.Submittable.Value = !DimmedFuncForSell(inventoryItem.ItemBase.Value);
            data.Item.Value = new CountEditableItem(inventoryItem.ItemBase.Value,
                1,
                1,
                inventoryItem.Count.Value);
            data.Item.Value.CountEnabled.Value = false;
        }

        private void ShowRetrievePopup(ShopItem shopItem)
        {
            if (shopItem is null ||
                shopItem.Dimmed.Value)
            {
                return;
            }

            SharedModel.ItemCountAndPricePopup.Value.TitleText.Value =
                L10nManager.Localize("UI_RETRIEVE");
            SharedModel.ItemCountAndPricePopup.Value.InfoText.Value =
                L10nManager.Localize("UI_RETRIEVE_INFO");
            SharedModel.ItemCountAndPricePopup.Value.CountEnabled.Value = true;
            SharedModel.ItemCountAndPricePopup.Value.Submittable.Value =
                ButtonEnabledFuncForSell(shopItem);
            SharedModel.ItemCountAndPricePopup.Value.Price.Value = shopItem.Price.Value;
            SharedModel.ItemCountAndPricePopup.Value.PriceInteractable.Value = false;
            SharedModel.ItemCountAndPricePopup.Value.Item.Value = new CountEditableItem(
                shopItem.ItemBase.Value,
                shopItem.Count.Value,
                shopItem.Count.Value,
                shopItem.Count.Value);
        }

        // sell
        private void SubscribeSellPopup(CountableItem data)
        {
            if (data is null)
            {
                Find<ItemCountableAndPricePopup>().Close();
                return;
            }

            Find<ItemCountableAndPricePopup>().Show(SharedModel.ItemCountableAndPricePopup.Value,
                SharedModel.ItemCountableAndPricePopup.Value.IsSell.Value);
        }

        private void SubscribeSellPopupSubmit(Model.ItemCountableAndPricePopup data)
        {
            if (!(data.Item.Value.ItemBase.Value is ITradableItem tradableItem))
            {
                return;
            }

            if (data.TotalPrice.Value.MinorUnit > 0)
            {
                OneLineSystem.Push(
                    MailType.System,
                    L10nManager.Localize("UI_TOTAL_PRICE_WARNING"),
                    NotificationCell.NotificationType.Alert);
                return;
            }

            if (data.TotalPrice.Value.Sign * data.TotalPrice.Value.MajorUnit < Model.Shop.MinimumPrice)
            {
                throw new InvalidSellingPriceException(data);
            }

            var totalPrice = data.TotalPrice.Value;
            var count = data.Count.Value;
            var itemSubType = data.Item.Value.ItemBase.Value.ItemSubType;
            Game.Game.instance.ActionManager.Sell(tradableItem, count, totalPrice, itemSubType).Subscribe();
            Analyzer.Instance.Track("Unity/Sell");
            ResponseSell();
        }

        private void SubscribeSellPopupUpdateSell(Model.ItemCountableAndPricePopup data)
        {
            if (!(data.Item.Value.ItemBase.Value is ITradableItem tradableItem))
            {
                return;
            }

            if (data.TotalPrice.Value.MinorUnit > 0)
            {
                OneLineSystem.Push(
                    MailType.System,
                    L10nManager.Localize("UI_TOTAL_PRICE_WARNING"),
                    NotificationCell.NotificationType.Alert);
                return;
            }

            if (data.TotalPrice.Value.Sign * data.TotalPrice.Value.MajorUnit < Shop.MinimumPrice)
            {
                throw new InvalidSellingPriceException(data);
            }

            var requiredBlockIndex = tradableItem.RequiredBlockIndex;
            var totalPrice = data.TotalPrice.Value;
            var preTotalPrice = data.PreTotalPrice.Value;
            var count = data.Count.Value;
            var digest =
                ReactiveShopState.GetSellDigest(tradableItem.TradableId, requiredBlockIndex, preTotalPrice, count);
            if (digest == null)
            {
                return;
            }

            var itemSubType = data.Item.Value.ItemBase.Value.ItemSubType;
            Game.Game.instance.ActionManager.UpdateSell(
                digest.OrderId,
                tradableItem,
                count,
                totalPrice,
                itemSubType).Subscribe();
            Analyzer.Instance.Track("Unity/UpdateSell");
            ResponseSell();
        }

        private void SubscribeSellPopupCancel(Model.ItemCountableAndPricePopup data)
        {
            SharedModel.ItemCountableAndPricePopup.Value.Item.Value = null;
            inventory.SharedModel.DeselectItemView();
            Find<ItemCountableAndPricePopup>().Close();
        }

        private void SubscribeSellPopupCount(int count)
        {
            SharedModel.ItemCountableAndPricePopup.Value.Count.Value = count;
            UpdateTotalPrice(PriorityType.Count);
        }

        private void SubscribeSellPopupPrice(decimal price)
        {
            var majorUnit = (int) price;
            var minorUnit = (int)((Math.Truncate((price - majorUnit) * 100) / 100) * 100);
            var model = SharedModel.ItemCountableAndPricePopup.Value;
            model.Price.SetValueAndForceNotify(new FungibleAssetValue(model.Price.Value.Currency,
                majorUnit, minorUnit));
            UpdateTotalPrice(PriorityType.Price);
        }

        private void UpdateTotalPrice(PriorityType priorityType)
        {
            var model = SharedModel.ItemCountableAndPricePopup.Value;
            decimal price = 0;
            if (decimal.TryParse(model.Price.Value.GetQuantityString(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var result))
            {
                price = result;
            }

            var count = model.Count.Value;
            var totalPrice = price * count;

            if (totalPrice > LimitPrice)
            {
                switch (priorityType)
                {
                    case PriorityType.Price:
                        price = LimitPrice / model.Count.Value;
                        var majorUnit = (int) price;
                        var minorUnit = (int)((price - majorUnit) * 100);
                        model.Price.Value = new FungibleAssetValue(model.Price.Value.Currency, majorUnit, minorUnit);
                        break;
                    case PriorityType.Count:
                        count = LimitPrice / (int)price;
                        model.Count.Value = count;
                        break;
                }

                OneLineSystem.Push(
                    MailType.System,
                    L10nManager.Localize("UI_SELL_LIMIT_EXCEEDED"),
                    NotificationCell.NotificationType.Alert);
            }

            var currency = model.TotalPrice.Value.Currency;
            var sum = price * count;
            var major = (int) sum;
            var minor = (int) ((sum - (int) sum) * 100);
            var fungibleAsset = new FungibleAssetValue(currency, major, minor);
            model.TotalPrice.SetValueAndForceNotify(fungibleAsset);
        }

        // sell cancellation
        private void SubscribeSellCancellationPopup(CountableItem data)
        {
            if (data is null)
            {
                Find<TwoButtonSystem>().Close();
                return;
            }

            Find<TwoButtonSystem>().Show(L10nManager.Localize("UI_SELL_CANCELLATION"),
                                        L10nManager.Localize("UI_YES"),
                                        L10nManager.Localize("UI_NO"),
                                        SubscribeSellCancellationPopupSubmit,
                                        SubscribeSellCancellationPopupCancel);
        }

        private void SubscribeSellCancellationPopupSubmit()
        {
            var model = SharedModel.ItemCountAndPricePopup.Value;
            if (!(model.Item.Value.ItemBase.Value is ITradableItem tradableItem))
            {
                return;
            }

            var avatarAddress = States.Instance.CurrentAvatarState.address;
            var tradableId = tradableItem.TradableId;
            var requiredBlockIndex = tradableItem.RequiredBlockIndex;
            var price = model.Price.Value;
            var count = model.Item.Value.Count.Value;
            var subType = tradableItem.ItemSubType;

            var digest = ReactiveShopState.GetSellDigest(tradableId, requiredBlockIndex, price, count);
            if (digest != null)
            {
                Analyzer.Instance.Track("Unity/Sell Cancellation");
                Game.Game.instance.ActionManager.SellCancellation(
                    avatarAddress,
                    digest.OrderId,
                    digest.TradableId,
                    subType).Subscribe();
                ResponseSellCancellation(digest.OrderId, digest.TradableId);
            }
        }

        private void SubscribeSellCancellationPopupCancel()
        {
            SharedModel.ItemCountAndPricePopup.Value.Item.Value = null;
            Find<TwoButtonSystem>().Close();
        }

        private static bool DimmedFuncForSell(ItemBase itemBase)
        {
            if (itemBase.ItemType == ItemType.Material)
            {
                return !(itemBase is TradableMaterial);
            }
            return false;
        }

        private static bool ButtonEnabledFuncForSell(CountableItem inventoryItem)
        {
            switch (inventoryItem)
            {
                case null:
                    return false;
                case ShopItem _:
                    return true;
                default:
                    return !inventoryItem.Dimmed.Value;
            }
        }

        private void ResponseSell()
        {
            var item = SharedModel.ItemCountableAndPricePopup.Value.Item.Value;
            var count = SharedModel.ItemCountableAndPricePopup.Value.Count.Value;
            SharedModel.ItemCountableAndPricePopup.Value.Item.Value = null;

            AudioController.instance.PlaySfx(AudioController.SfxCode.InputItem);

            string message = string.Empty;
            if (count > 1)
            {
                message = string.Format(L10nManager.Localize("NOTIFICATION_MULTIPLE_SELL_START"),
                    item.ItemBase.Value.GetLocalizedName(),
                    count);
            }
            else
            {
                message = string.Format(L10nManager.Localize("NOTIFICATION_SELL_START"),
                    item.ItemBase.Value.GetLocalizedName());
            }
            OneLineSystem.Push(MailType.Auction, message, NotificationCell.NotificationType.Information);
            inventory.SharedModel.ActiveFunc.SetValueAndForceNotify(inventoryItem => (inventoryItem.ItemBase.Value is ITradableItem));
            // Refresh();
        }

        private async void ResponseSellCancellation(Guid orderId, Guid tradableId)
        {
            SharedModel.ItemCountAndPricePopup.Value.Item.Value = null;
            var itemName = await Util.GetItemNameByOrderId(orderId);
            ReactiveShopState.RemoveSellDigest(orderId);
            AudioController.instance.PlaySfx(AudioController.SfxCode.InputItem);
            var format = L10nManager.Localize("NOTIFICATION_SELL_CANCEL_START");
            OneLineSystem.Push(
                MailType.Auction,
                string.Format(format, itemName),
                NotificationCell.NotificationType.Information);
            inventory.SharedModel.ActiveFunc.SetValueAndForceNotify(inventoryItem => (inventoryItem.ItemBase.Value is ITradableItem));
            // Refresh();
        }

        private void ShowSpeech(string key,
            CharacterAnimation.Type type = CharacterAnimation.Type.Emotion)
        {
            _npc.PlayAnimation(type == CharacterAnimation.Type.Greeting
                ? NPCAnimation.Type.Greeting
                : NPCAnimation.Type.Emotion);

            speechBubble.SetKey(key);
        }
    }
}
