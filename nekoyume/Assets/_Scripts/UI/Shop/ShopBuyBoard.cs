using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using mixpanel;
using Nekoyume.Action;
using Nekoyume.EnumType;
using Nekoyume.Game.Controller;
using Nekoyume.L10n;
using Nekoyume.Model.Mail;
using Nekoyume.State;
using Nekoyume.UI.Module;
using Nekoyume.UI.Scroller;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace Nekoyume.UI
{
    public class ShopBuyBoard : MonoBehaviour
    {
        [SerializeField] List<ShopBuyWishItemView> items = new List<ShopBuyWishItemView>();
        [SerializeField] private ShopBuyItems shopItems = null;
        [SerializeField] private GameObject defaultView;
        [SerializeField] private GameObject wishListView;
        [SerializeField] private Button showWishListButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button buyButton;
        [SerializeField] private Button transactionHistoryButton;

        [SerializeField] private TextMeshProUGUI buyText;
        [SerializeField] private TextMeshProUGUI priceText;

        public readonly Subject<bool> OnChangeBuyType = new Subject<bool>();

        public bool IsAcitveWishListView => wishListView.activeSelf;

        private double _price;

        private void Awake()
        {
            showWishListButton.OnClickAsObservable().Subscribe(ShowWishList).AddTo(gameObject);
            cancelButton.OnClickAsObservable().Subscribe(OnCloseBuyWishList).AddTo(gameObject);
            buyButton.OnClickAsObservable().Subscribe(OnClickBuy).AddTo(gameObject);
            transactionHistoryButton.OnClickAsObservable().Subscribe(OnClickTransactionHistory).AddTo(gameObject);
            buyButton.image.enabled = false;
        }

        private void OnEnable()
        {
            buyButton.image.enabled = true;
            ShowDefaultView();
        }

        private void ShowWishList(Unit unit)
        {
            Clear();
            defaultView.SetActive(false);
            wishListView.SetActive(true);
            OnChangeBuyType.OnNext(true);
        }

        public void ShowDefaultView()
        {
            priceText.text = "0";
            defaultView.SetActive(true);
            wishListView.SetActive(false);
            OnChangeBuyType.OnNext(false);
        }

        private void OnCloseBuyWishList(Unit unit)
        {
            if (shopItems.SharedModel.WishItemCount > 0)
            {
                Widget.Find<TwoButtonSystem>().Show(L10nManager.Localize("UI_CLOSE_BUY_WISH_LIST"),
                                                   L10nManager.Localize("UI_YES"),
                                                   L10nManager.Localize("UI_NO"), ShowDefaultView);
            }
            else
            {
                ShowDefaultView();
            }
        }

        private void OnClickBuy(Unit unit)
        {
            if (_price <= 0)
            {
                return;
            }

            var currentGold = double.Parse(States.Instance.GoldBalanceState.Gold.GetQuantityString());
            if (currentGold < _price)
            {
                OneLineSystem.Push(
                    MailType.System,
                    L10nManager.Localize("UI_NOT_ENOUGH_NCG"),
                    NotificationCell.NotificationType.Information);
                return;
            }

            var content = string.Format(L10nManager.Localize("UI_BUY_MULTIPLE_FORMAT"),
                shopItems.SharedModel.WishItemCount, _price);

            Widget.Find<TwoButtonSystem>().Show(content,
                                               L10nManager.Localize("UI_BUY"),
                                               L10nManager.Localize("UI_CANCEL"),
                                               BuyMultiple);
        }

        private async void BuyMultiple()
        {
            var wishItems = shopItems.SharedModel.GetWishItems;
            var purchaseInfos = new ConcurrentBag<PurchaseInfo>();

            await foreach (var item in wishItems.ToAsyncEnumerable())
            {
                var purchaseInfo = await ShopBuy.GetPurchaseInfo(item.OrderId.Value);
                purchaseInfos.Add(purchaseInfo);
            }
            Game.Game.instance.ActionManager.Buy(purchaseInfos.ToList()).Subscribe();

            if (shopItems.SharedModel.WishItemCount > 0)
            {
                var props = new Value
                {
                    ["Count"] = shopItems.SharedModel.WishItemCount,
                };
                Analyzer.Instance.Track("Unity/Number of Purchased Items", props);
            }

            foreach (var shopItem in shopItems.SharedModel.GetWishItems)
            {
                var props = new Value
                {
                    ["Price"] = shopItem.Price.Value.GetQuantityString(),
                };
                Analyzer.Instance.Track("Unity/Buy", props);
                
                var count = shopItem.Count.Value;
                shopItem.Selected.Value = false;
                ReactiveShopState.RemoveBuyDigest(shopItem.OrderId.Value);

                string message;
                if (count > 1)
                {
                    message = string.Format(L10nManager.Localize("NOTIFICATION_MULTIPLE_BUY_START"),
                        shopItem.ItemBase.Value.GetLocalizedName(), count);
                }
                else
                {
                    message = string.Format(L10nManager.Localize("NOTIFICATION_BUY_START"),
                        shopItem.ItemBase.Value.GetLocalizedName());
                }
                OneLineSystem.Push(MailType.Auction, message, NotificationCell.NotificationType.Information);
            }
            AudioController.instance.PlaySfx(AudioController.SfxCode.BuyItem);
            shopItems.SharedModel.ClearWishList();
            UpdateWishList();
        }

        private void OnClickTransactionHistory(Unit unit)
        {
            Widget.Find<Alert>().Show("UI_ALERT_NOT_IMPLEMENTED_TITLE", "UI_ALERT_NOT_IMPLEMENTED_CONTENT");
        }

        private void Clear()
        {
            foreach (var item in items)
            {
                item?.gameObject.SetActive(false);
            }
        }

        public void UpdateWishList()
        {
            Clear();
            _price = 0.0f;
            for (int i = 0; i < shopItems.SharedModel.WishItemCount; i++)
            {
                var item = shopItems.SharedModel.GetWishItems[i];
                _price += double.Parse(item.Price.Value.GetQuantityString());
                items[i].gameObject.SetActive(true);
                items[i].SetData(item, () =>
                {
                    shopItems.SharedModel.RemoveItemInWishList(item);
                    UpdateWishList();
                });
            }

            priceText.text = _price.ToString();
            var currentGold = double.Parse(States.Instance.GoldBalanceState.Gold.GetQuantityString());
            if (currentGold < _price)
            {
                priceText.color = Palette.GetColor(ColorType.ButtonDisabled);
                buyButton.image.color = Palette.GetColor(ColorType.ButtonColorDisabled);
                buyText.color = Palette.GetColor(ColorType.ButtonAlphaDisabled);
            }
            else
            {
                priceText.color = Palette.GetColor(0);
                buyButton.image.color = shopItems.SharedModel.WishItemCount > 0 ?
                    Palette.GetColor(ColorType.ButtonEnabled) : Palette.GetColor(ColorType.ButtonColorDisabled);
                buyText.color = shopItems.SharedModel.WishItemCount > 0 ?
                    Palette.GetColor(ColorType.ButtonEnabled) : Palette.GetColor(ColorType.ButtonAlphaDisabled);
            }
        }

        private void OnDestroy()
        {
            OnChangeBuyType.Dispose();
        }
    }
}
